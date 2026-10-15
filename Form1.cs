using OmpSessionManager.Models;
using OmpSessionManager.Services;

namespace OmpSessionManager;

/// <summary>
/// 提供 OMP 会话的扫描、搜索、标签管理和恢复界面。
/// </summary>
public partial class Form1 : Form
{
    private readonly OmpSessionScanner sessionScanner = new();
    private readonly OmpWindowScanner windowScanner = new();
    private readonly OmpWindowMatcher windowMatcher = new();

    private readonly AppLaunchOptions launchOptions;
    private readonly SessionStore sessionStore;
    private readonly SessionSearch sessionSearch = new();
    private readonly OmpLauncher ompLauncher;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 15_000 };
    private readonly CancellationTokenSource closeCancellationSource = new();

    private readonly DataGridView sessionsGrid = new();
    private readonly CheckBox showRemovedCheckBox = new();
    private readonly CheckBox showIncompleteCheckBox = new();
    private readonly Button refreshButton = new();
    private readonly Button resumeButton = new();
    private readonly Button removeButton = new();
    private readonly Button restoreRecordButton = new();
    private readonly TextBox searchTextBox = new();
    private readonly Button searchButton = new();
    private readonly Button markActiveIncompleteButton = new();
    private readonly Button markIncompleteButton = new();
    private readonly Button clearIncompleteButton = new();
    private readonly Button restoreIncompleteButton = new();
    private readonly TextBox sessionIdTextBox = new();
    private readonly TextBox workingDirectoryTextBox = new();
    private readonly TextBox filePathTextBox = new();
    private readonly TextBox summaryTextBox = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripStatusLabel countLabel = new();

    private SessionIndex sessionIndex = new();
    private List<SessionRecord> visibleSessions = new();
    private HashSet<string> openSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private bool isRefreshing;
    private bool isClosing;

    /// <summary>
    /// 初始化窗口控件、事件和定时扫描器；可注入隔离数据路径以支持安全演示和 UI 测试。
    /// </summary>
    public Form1(AppLaunchOptions? launchOptions = null)
    {
        this.launchOptions = launchOptions ?? new AppLaunchOptions();
        sessionStore = new SessionStore(this.launchOptions.IndexFilePath);
        ompLauncher = new OmpLauncher(this.launchOptions.OmpExecutablePath);
        BuildUserInterface();
        ConfigureAutomationMetadata();
        refreshTimer.Tick += RefreshTimer_Tick;
        sessionsGrid.SelectionChanged += SessionsGrid_SelectionChanged;
        showRemovedCheckBox.CheckedChanged += FilterCheckBox_CheckedChanged;
        showIncompleteCheckBox.CheckedChanged += FilterCheckBox_CheckedChanged;
        searchTextBox.KeyDown += SearchTextBox_KeyDown;
        Shown += Form1_Shown;
        FormClosed += Form1_FormClosed;
    }

    /// <summary>
    /// 为关键控件提供稳定的自动化标识，使 UI 测试不依赖会因翻译变化的可见文本。
    /// </summary>
    private void ConfigureAutomationMetadata()
    {
        sessionsGrid.Name = "SessionsGrid";
        searchTextBox.Name = "SearchTextBox";
        refreshButton.Name = "RefreshButton";
        resumeButton.Name = "ResumeButton";
        removeButton.Name = "RemoveButton";
        restoreRecordButton.Name = "RestoreRecordButton";
        searchButton.Name = "SearchButton";
        markActiveIncompleteButton.Name = "MarkActiveIncompleteButton";
        markIncompleteButton.Name = "MarkIncompleteButton";
        clearIncompleteButton.Name = "ClearIncompleteButton";
        restoreIncompleteButton.Name = "RestoreIncompleteButton";
        showRemovedCheckBox.Name = "ShowRemovedCheckBox";
        showIncompleteCheckBox.Name = "ShowIncompleteCheckBox";
        sessionsGrid.AccessibleName = "会话列表";
        searchTextBox.AccessibleName = "会话搜索";
        restoreIncompleteButton.AccessibleName = "恢复未完成会话";
    }

    /// <summary>
    /// 构建固定尺寸约束的双行工具栏、会话列表和详情区。
    /// </summary>
    private void BuildUserInterface()
    {
        Text = "OMP 会话恢复器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1_024, 640);
        ClientSize = new Size(1_280, 760);
        Font = new Font("Microsoft YaHei UI", 9F);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 84,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 6, 8, 4)
        };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        ConfigureButton(refreshButton, "刷新会话", 92);
        ConfigureButton(resumeButton, "恢复对话", 92);
        ConfigureButton(removeButton, "移除记录", 92);
        ConfigureButton(restoreRecordButton, "恢复记录", 92);
        resumeButton.Enabled = false;
        removeButton.Enabled = false;
        restoreRecordButton.Enabled = false;
        refreshButton.Click += RefreshButton_Click;
        resumeButton.Click += ResumeButton_Click;
        removeButton.Click += RemoveButton_Click;
        restoreRecordButton.Click += RestoreRecordButton_Click;

        ConfigureFilterCheckBox(showRemovedCheckBox, "显示已移除");
        ConfigureFilterCheckBox(showIncompleteCheckBox, "只显示未完成");
        actionRow.Controls.Add(refreshButton);
        actionRow.Controls.Add(resumeButton);
        actionRow.Controls.Add(removeButton);
        actionRow.Controls.Add(restoreRecordButton);
        actionRow.Controls.Add(showRemovedCheckBox);
        actionRow.Controls.Add(showIncompleteCheckBox);

        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        var searchLabel = new Label
        {
            Text = "搜索标题和简介",
            AutoSize = true,
            Margin = new Padding(0, 7, 6, 0)
        };
        searchTextBox.Width = 220;
        searchTextBox.Height = 26;
        searchTextBox.Margin = new Padding(0, 1, 6, 0);
        searchTextBox.PlaceholderText = "多个关键词用空格分隔";
        ConfigureButton(searchButton, "搜索", 76);
        searchButton.Click += SearchButton_Click;
        ConfigureButton(markActiveIncompleteButton, "标记打开窗口", 116);
        markActiveIncompleteButton.Click += MarkActiveIncompleteButton_Click;
        ConfigureButton(markIncompleteButton, "标记未完成", 100);
        markIncompleteButton.Click += MarkIncompleteButton_Click;
        ConfigureButton(clearIncompleteButton, "取消未完成", 100);
        clearIncompleteButton.Click += ClearIncompleteButton_Click;
        ConfigureButton(restoreIncompleteButton, "恢复未完成", 116);
        restoreIncompleteButton.Click += RestoreIncompleteButton_Click;

        searchRow.Controls.Add(searchLabel);
        searchRow.Controls.Add(searchTextBox);
        searchRow.Controls.Add(searchButton);
        searchRow.Controls.Add(markActiveIncompleteButton);
        searchRow.Controls.Add(markIncompleteButton);
        searchRow.Controls.Add(clearIncompleteButton);
        searchRow.Controls.Add(restoreIncompleteButton);
        toolbar.Controls.Add(actionRow, 0, 0);
        toolbar.Controls.Add(searchRow, 0, 1);

        ConfigureGrid();
        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 8) };
        gridPanel.Controls.Add(sessionsGrid);

        var detailsPanel = BuildDetailsPanel();
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(1_000, 650),
            SplitterDistance = 400,
            Panel1MinSize = 220,
            Panel2MinSize = 160
        };
        splitContainer.Panel1.Controls.Add(gridPanel);
        splitContainer.Panel2.Controls.Add(detailsPanel);

        var statusStrip = new StatusStrip();
        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        countLabel.TextAlign = ContentAlignment.MiddleRight;
        statusStrip.Items.Add(statusLabel);
        statusStrip.Items.Add(countLabel);

        Controls.Add(splitContainer);
        Controls.Add(toolbar);
        Controls.Add(statusStrip);
    }

    /// <summary>
    /// 配置表格列和只读行为，使会话信息适合扫描比较。
    /// </summary>
    private void ConfigureGrid()
    {
        sessionsGrid.Dock = DockStyle.Fill;
        sessionsGrid.AllowUserToAddRows = false;
        sessionsGrid.AllowUserToDeleteRows = false;
        sessionsGrid.AllowUserToResizeRows = false;
        sessionsGrid.AutoGenerateColumns = false;
        sessionsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        sessionsGrid.BackgroundColor = SystemColors.Window;
        sessionsGrid.BorderStyle = BorderStyle.FixedSingle;
        sessionsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        sessionsGrid.MultiSelect = false;
        sessionsGrid.ReadOnly = true;
        sessionsGrid.RowHeadersVisible = false;
        sessionsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        sessionsGrid.Columns.Add(CreateTextColumn("Title", "标题", 170));
        sessionsGrid.Columns.Add(CreateTextColumn("Summary", "内容简介", 330));
        sessionsGrid.Columns.Add(CreateTextColumn("WorkingDirectory", "工作目录", 250));
        sessionsGrid.Columns.Add(CreateTextColumn("LastSeen", "最近活动", 140));
        sessionsGrid.Columns.Add(CreateTextColumn("State", "状态", 160));
    }

    /// <summary>
    /// 创建带稳定宽度约束的列表列，避免动态内容导致布局跳动。
    /// </summary>
    private static DataGridViewTextBoxColumn CreateTextColumn(string name, string headerText, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            DataPropertyName = name,
            Width = width,
            MinimumWidth = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(4, 2, 4, 2) }
        };
    }

    /// <summary>
    /// 构建会话详情区，提供可复制的 session ID、路径和摘要。
    /// </summary>
    private Control BuildDetailsPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8, 0, 8, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ConfigureDetailTextBox(sessionIdTextBox);
        ConfigureDetailTextBox(workingDirectoryTextBox);
        ConfigureDetailTextBox(filePathTextBox);
        ConfigureDetailTextBox(summaryTextBox);
        summaryTextBox.Multiline = true;
        summaryTextBox.ScrollBars = ScrollBars.Vertical;

        AddDetailRow(layout, 0, "Session ID", sessionIdTextBox);
        AddDetailRow(layout, 1, "工作目录", workingDirectoryTextBox);
        AddDetailRow(layout, 2, "原始文件", filePathTextBox);
        AddDetailRow(layout, 3, "内容简介", summaryTextBox);
        return layout;
    }

    /// <summary>配置详情文本框为只读但允许用户复制内容。</summary>
    private static void ConfigureDetailTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.ReadOnly = true;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Margin = new Padding(0, 3, 0, 3);
    }

    /// <summary>将一个带标签的详情控件加入指定行。</summary>
    private static void AddDetailRow(TableLayoutPanel layout, int row, string labelText, Control control)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 8, 0)
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    /// <summary>统一配置筛选复选框的尺寸和间距。</summary>
    private static void ConfigureFilterCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Margin = new Padding(14, 7, 0, 0);
    }

    /// <summary>
    /// 窗口首次显示时执行首次扫描；定时器只在首轮完成后启动。
    /// </summary>
    private async void Form1_Shown(object? sender, EventArgs e)
    {
        await RefreshSessionsAsync();
        if (!isClosing && !launchOptions.DisableAutoRefresh)
        {
            refreshTimer.Start();
        }
    }

    /// <summary>窗口关闭时取消正在进行的刷新续体并释放定时器，避免后台扫描完成后访问已释放控件。</summary>
    private void Form1_FormClosed(object? sender, FormClosedEventArgs e)
    {
        isClosing = true;
        closeCancellationSource.Cancel();
        refreshTimer.Stop();
        refreshTimer.Dispose();
        closeCancellationSource.Dispose();
    }

    /// <summary>响应定时刷新，避免刷新过程重入。</summary>
    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshSessionsAsync();
    }

    /// <summary>响应用户点击刷新按钮并更新会话列表。</summary>
    private async void RefreshButton_Click(object? sender, EventArgs e)
    {
        await RefreshSessionsAsync();
    }

    /// <summary>
    /// 在后台读取 OMP 文件和窗口状态，在 UI 线程更新列表；失败时保留上一次可用数据。
    /// </summary>
    private async Task RefreshSessionsAsync()
    {
        if (isRefreshing || isClosing || IsDisposed || Disposing)
        {
            return;
        }

        var cancellationToken = closeCancellationSource.Token;
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        isRefreshing = true;
        refreshButton.Enabled = false;
        UpdateActionButtons();
        SetStatus("正在扫描 OMP 会话…");
        var selectedSessionId = GetSelectedSession()?.SessionId;

        try
        {
            var result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var discovered = sessionScanner.Scan(launchOptions.AgentDirectory);
                var openWindows = launchOptions.DisableWindowScan
                    ? Array.Empty<OmpWindowInfo>()
                    : windowScanner.Scan();
                var index = sessionStore.Load();

                // 先合并 OMP JSONL 的最新标题，再按完整标题和唯一目录名匹配窗口。
                sessionStore.MergeDiscovered(index, discovered);
                var matchedSessionIds = windowMatcher.MatchSessionIds(index.Sessions, openWindows);
                foreach (var session in index.Sessions)
                {
                    session.IsWindowOpen = matchedSessionIds.Contains(session.SessionId);
                }

                var workerWindowCount = openWindows.Count(window => string.Equals(
                    window.WindowTitle,
                    "omp daemon broker",
                    StringComparison.OrdinalIgnoreCase));
                sessionStore.Save(index);
                return (
                    Index: index,
                    OpenSessionIds: matchedSessionIds.ToArray(),
                    CommandLineWindowCount: openWindows.Count,
                    WorkerWindowCount: workerWindowCount);
            }, cancellationToken);

            if (isClosing || IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            sessionIndex = result.Index;
            openSessionIds = result.OpenSessionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            RebindGrid(selectedSessionId);
            SetStatus($"已完成扫描：{openSessionIds.Count} 个可恢复会话窗口，{result.WorkerWindowCount} 个 OMP worker 窗口，共 {result.CommandLineWindowCount} 个命令行窗口。 ");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (isClosing || IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SetStatus("扫描失败，已保留上次列表。");
            MessageBox.Show(
                this,
                "无法读取本地会话索引或 OMP 数据。请确认文件可访问后重试。",
                "扫描 OMP 会话失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 窗口关闭取消后不再显示状态或对话框；后台文件读取允许自行完成。
        }
        catch (Exception)
        {
            if (isClosing || IsDisposed || Disposing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // async void 事件不能让未预期异常越过 UI 消息循环，否则一次异常 JSON 即可终止应用。
            SetStatus("扫描发生意外错误，已保留上次列表。");
            MessageBox.Show(
                this,
                "无法完成会话扫描。请更新应用或检查会话数据后重试。",
                "扫描 OMP 会话失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            isRefreshing = false;
            if (!isClosing && !IsDisposed && !Disposing && !cancellationToken.IsCancellationRequested)
            {
                refreshButton.Enabled = true;
                UpdateActionButtons();
            }
        }
    }

    /// <summary>
    /// 按移除、未完成和搜索条件重新绑定列表，并尽量恢复原选中项。
    /// </summary>
    private void RebindGrid(string? selectedSessionId)
    {
        var candidates = sessionIndex.Sessions
            .Where(record => (showRemovedCheckBox.Checked || !record.IsRemoved)
                && (!showIncompleteCheckBox.Checked || record.IsIncomplete));
        visibleSessions = sessionSearch.Filter(candidates, searchTextBox.Text)
            .OrderByDescending(record => record.LastSeenAtUtc)
            .ToList();

        sessionsGrid.Rows.Clear();
        foreach (var session in visibleSessions)
        {
            var rowIndex = sessionsGrid.Rows.Add(
                session.Title,
                session.Summary,
                session.WorkingDirectory,
                FormatTimestamp(session.LastSeenAtUtc),
                FormatState(session));
            sessionsGrid.Rows[rowIndex].Tag = session.SessionId;
        }

        var selectedIndex = selectedSessionId is null
            ? -1
            : visibleSessions.FindIndex(record => string.Equals(
                record.SessionId,
                selectedSessionId,
                StringComparison.OrdinalIgnoreCase));
        if (selectedIndex >= 0)
        {
            sessionsGrid.Rows[selectedIndex].Selected = true;
            sessionsGrid.CurrentCell = sessionsGrid.Rows[selectedIndex].Cells[0];
        }
        else if (sessionsGrid.Rows.Count > 0)
        {
            sessionsGrid.Rows[0].Selected = true;
            sessionsGrid.CurrentCell = sessionsGrid.Rows[0].Cells[0];
        }
        UpdateDetails(GetSelectedSession());
        countLabel.Text = $"显示 {visibleSessions.Count} 条，索引共 {sessionIndex.Sessions.Count} 条";
        UpdateActionButtons();
    }

    /// <summary>执行当前搜索框中的多关键词筛选。</summary>
    private void SearchButton_Click(object? sender, EventArgs e)
    {
        RebindGrid(GetSelectedSession()?.SessionId);
        SetStatus(string.IsNullOrWhiteSpace(searchTextBox.Text) ? "已清除搜索条件。" : "已完成搜索。 ");
    }

    /// <summary>按回车执行搜索，减少重复点击操作。</summary>
    private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        SearchButton_Click(sender, e);
        e.SuppressKeyPress = true;
    }

    /// <summary>显示或隐藏筛选条件下的会话，并保留当前选中项。</summary>
    private void FilterCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        RebindGrid(GetSelectedSession()?.SessionId);
    }

    /// <summary>将选中会话的详情写入下方详情区。</summary>
    private void SessionsGrid_SelectionChanged(object? sender, EventArgs e)
    {
        var session = GetSelectedSession();
        UpdateDetails(session);
        UpdateActionButtons();
    }

    /// <summary>
    /// 同步详情区与当前会话；列表重绑时即使默认选中行没有触发 SelectionChanged，也能显示其详情，
    /// 演示模式会隐藏 JSONL 的实际本机路径。
    /// </summary>
    private void UpdateDetails(SessionRecord? session)
    {
        if (session is null)
        {
            ClearDetails();
            return;
        }

        sessionIdTextBox.Text = session.SessionId;
        workingDirectoryTextBox.Text = session.WorkingDirectory;
        filePathTextBox.Text = launchOptions.RedactFilePath ? "（演示模式已隐藏原始文件路径）" : session.SessionFilePath;
        summaryTextBox.Text = session.Summary;
    }

    /// <summary>
    /// 启动 OMP 恢复选中会话；原始文件缺失时通过启动器错误提示用户。
    /// </summary>
    private void ResumeButton_Click(object? sender, EventArgs e)
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        try
        {
            using var process = ompLauncher.Start(session);
            SetStatus($"已请求恢复会话：{session.Title}");
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, "恢复 OMP 会话失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 将选中保留记录标记为移除；不会删除 OMP 原始 session 文件。
    /// </summary>
    private void RemoveButton_Click(object? sender, EventArgs e)
    {
        var session = GetSelectedSession();
        if (session is null || session.IsRemoved)
        {
            return;
        }

        sessionStore.MarkRemoved(sessionIndex, session.SessionId, true);
        RebindGrid(null);
        SetStatus("记录已移除；OMP 原始会话仍然保留。 ");
    }

    /// <summary>将已移除记录恢复到主列表。</summary>
    private void RestoreRecordButton_Click(object? sender, EventArgs e)
    {
        var session = GetSelectedSession();
        if (session is null || !session.IsRemoved)
        {
            return;
        }

        sessionStore.MarkRemoved(sessionIndex, session.SessionId, false);
        RebindGrid(session.SessionId);
        SetStatus("记录已恢复到主列表。 ");
    }

    /// <summary>
    /// 将标题匹配到任务栏命令行窗口的会话批量设置为未完成；匹配失败时不修改其他会话。
    /// </summary>
    private void MarkActiveIncompleteButton_Click(object? sender, EventArgs e)
    {
        if (openSessionIds.Count == 0)
        {
            SetStatus("当前没有匹配到已打开的 OMP 会话窗口。 ");
            return;
        }

        var changedCount = sessionStore.MarkIncomplete(sessionIndex, openSessionIds, true);
        RebindGrid(GetSelectedSession()?.SessionId);
        SetStatus($"已将 {changedCount} 个打开窗口会话标记为未完成。 ");
    }

    /// <summary>为当前选中会话添加未完成标签并立即保存。</summary>
    private void MarkIncompleteButton_Click(object? sender, EventArgs e)
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        sessionStore.MarkIncomplete(sessionIndex, new[] { session.SessionId }, true);
        RebindGrid(session.SessionId);
        SetStatus("已标记为未完成。 ");
    }

    /// <summary>移除当前选中会话的未完成标签并立即保存。</summary>
    private void ClearIncompleteButton_Click(object? sender, EventArgs e)
    {
        var session = GetSelectedSession();
        if (session is null)
        {
            return;
        }

        sessionStore.MarkIncomplete(sessionIndex, new[] { session.SessionId }, false);
        RebindGrid(session.SessionId);
        SetStatus("已取消未完成标签。 ");
    }

    /// <summary>
    /// 逐个启动所有未移除且原始文件存在的未完成会话，继续处理单项启动失败并汇总结果。
    /// </summary>
    private void RestoreIncompleteButton_Click(object? sender, EventArgs e)
    {
        var incompleteSessions = sessionIndex.Sessions
            .Where(session => session.IsIncomplete && !session.IsRemoved)
            .ToList();
        var restorableSessions = incompleteSessions
            .Where(session => File.Exists(session.SessionFilePath))
            .ToList();
        var skippedCount = incompleteSessions.Count - restorableSessions.Count;

        if (restorableSessions.Count == 0)
        {
            var message = skippedCount == 0
                ? "当前没有可恢复的未完成会话。"
                : $"未完成会话均缺少原始文件，已跳过 {skippedCount} 个。";
            MessageBox.Show(this, message, "恢复未完成会话", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmation = $"将逐个启动 {restorableSessions.Count} 个未完成会话。\n"
            + "已启动的会话不会自动取消未完成标签。继续吗？";
        if (MessageBox.Show(
                this,
                confirmation,
                "恢复未完成会话",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var failures = new List<string>();
        foreach (var session in restorableSessions)
        {
            try
            {
                using var process = ompLauncher.Start(session);
            }
            catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                failures.Add(session.Title);
            }
        }

        if (failures.Count == 0)
        {
            SetStatus($"已请求恢复 {restorableSessions.Count} 个未完成会话。 ");
            return;
        }

        SetStatus($"已恢复 {restorableSessions.Count - failures.Count} 个，{failures.Count} 个启动失败。 ");
        MessageBox.Show(
            this,
            $"成功请求恢复 {restorableSessions.Count - failures.Count} 个会话，{failures.Count} 个会话启动失败。\n\n{string.Join("\n", failures.Take(10))}",
            "部分恢复失败",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    /// <summary>根据当前行的 Tag 返回对应会话记录。</summary>
    private SessionRecord? GetSelectedSession()
    {
        if (sessionsGrid.CurrentRow?.Tag is not string sessionId)
        {
            return null;
        }

        return visibleSessions.FirstOrDefault(record =>
            string.Equals(record.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>根据选中记录、筛选状态和窗口匹配结果启用或禁用操作按钮。</summary>
    private void UpdateActionButtons()
    {
        var session = GetSelectedSession();
        var canChangeSessionState = !isRefreshing;
        resumeButton.Enabled = canChangeSessionState && session is not null;
        removeButton.Enabled = canChangeSessionState && session is not null && !session.IsRemoved;
        restoreRecordButton.Enabled = canChangeSessionState && session is not null && session.IsRemoved;
        markIncompleteButton.Enabled = canChangeSessionState && session is not null && !session.IsIncomplete;
        clearIncompleteButton.Enabled = canChangeSessionState && session is not null && session.IsIncomplete;
        markActiveIncompleteButton.Enabled = canChangeSessionState && openSessionIds.Count > 0;
        restoreIncompleteButton.Enabled = canChangeSessionState && sessionIndex.Sessions.Any(
            record => record.IsIncomplete && !record.IsRemoved && File.Exists(record.SessionFilePath));
    }

    /// <summary>清空没有选中会话时的详情内容。</summary>
    private void ClearDetails()
    {
        sessionIdTextBox.Clear();
        workingDirectoryTextBox.Clear();
        filePathTextBox.Clear();
        summaryTextBox.Clear();
    }

    /// <summary>更新底部状态栏，并保留当前操作结果供用户确认。</summary>
    private void SetStatus(string message)
    {
        statusLabel.Text = message;
    }

    /// <summary>将 UTC 时间转换为本地时间并使用稳定格式显示。</summary>
    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>将会话状态组合成列表中的可读标签。</summary>
    private string FormatState(SessionRecord session)
    {
        var labels = new List<string>();
        if (session.IsRemoved)
        {
            labels.Add("已移除");
        }

        if (session.IsIncomplete)
        {
            labels.Add("未完成");
        }

        if (openSessionIds.Contains(session.SessionId))
        {
            labels.Add("窗口打开");
        }

        return labels.Count == 0 ? "已保存" : string.Join(" · ", labels);
    }

    /// <summary>配置工具栏按钮的统一尺寸和可访问文本。</summary>
    private static void ConfigureButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 28;
        button.Margin = new Padding(0, 0, 6, 0);
        button.UseVisualStyleBackColor = true;
    }
}
