# 项目自动化协作与发布约定

本文件面向在本仓库工作的自动化代理和维护者。除用户明确授权外，不得上传敏感数据、改写共享历史、发布 Release、修改仓库可见性或执行其他外部副作用。

详细约定请先阅读：

- [隐私与数据处理](docs/隐私与数据处理.md)
- [测试与质量](docs/测试与质量.md)
- [发布流程](docs/发布流程.md)
- [自动更新说明](docs/自动更新.md)

## 基本边界

- 项目是 `net9.0-windows` WinForms 应用；所有 Windows 命令使用 PowerShell，并以项目根目录为工作目录。
- 不得提交 `bin/`、`obj/`、`artifacts/`、`.user`、证书、私钥、环境文件、真实 OMP JSONL、真实截图或本机索引。
- 不得把真实用户名、邮箱、机器名、绝对用户目录、会话标题/摘要/ID、工作目录、令牌或密码写入代码、文档、测试夹具、图片、提交信息或 Release notes。
- 展示功能时只能使用 `samples/demo-agent/` 内的虚构数据，并通过 `scripts/Start-Demo.ps1` 以隔离索引、关闭窗口扫描和自动刷新、隐藏原始文件路径的方式启动。
- `Directory.Build.props` 是唯一版本来源。不得在多个项目文件中手工复制版本号；通过 `scripts/Set-ProjectVersion.ps1 -Version <语义化版本>` 更新。
- 当前应用没有内置自动更新器。文档只能说明手动更新和未来设计，不得声称程序会自动下载或安装更新。

## 每次代码或行为变更后的必经流程

按以下顺序执行，任何失败都必须先修复或如实记录，不能把未执行的检查写成通过。

1. 阅读受影响的代码、测试与文档；为新增或修改的方法补充准确的中文 XML 注释，并做独立代码审查。
2. 运行隐私和密钥预检：`pwsh -NoLogo -NoProfile -File .\scripts\Test-PublicReleaseContent.ps1`。
3. 运行格式、静态分析、Release build、Unit test、Integration test：`pwsh -NoLogo -NoProfile -File .\scripts\Invoke-QualityGate.ps1 -SkipUi`。
4. 在可用的交互式 Windows 桌面上运行 UI smoke test：`dotnet test .\OmpSessionManager.Tests\OmpSessionManager.Tests.csproj --configuration Release --filter "TestCategory=UI"`。该测试只验证窗体构造与自动化标识；涉及用户交互时，还要使用虚构演示数据完成实际 UI 验收。
5. 更新根 `README.md` 的中文和 English 内容、`CHANGELOG.md`、相关 `docs/` 文档；UI 可见变化必须重新生成并核验脱敏截图。
6. 复查 `git diff --check` 和完整差异，确认没有无关文件、敏感数据或不准确的发布声明。

## 构建、安装包与发布

用户已明确授权本次 Release 时，执行下列完整流程；常规代码修改不自动假定拥有发布授权。

1. 运行 `pwsh -NoLogo -NoProfile -File .\scripts\Publish-GitHubRelease.ps1 -Version <语义化版本>`。
2. 脚本会更新唯一版本来源，执行质量门禁，生成无 PDB 的 self-contained `win-x64` publish 输出、Inno Setup 安装程序、便携 ZIP 和 SHA-256 文件。
3. 脚本在通过历史隐私扫描后创建本地 Git commit、annotated tag、推送 `main` 和 tag，并用已认证的 GitHub CLI 创建 Release 与上传资产。
4. 发布后下载或检查 Release 资产，验证 SHA-256，确认安装器、便携版和版本号一致。未签名安装程序的 Windows 来源提示必须如实保留。

若 tag 和 `main` 已推送但 GitHub Release 创建中断，先核实 tag 与 `main` HEAD 一致且工作区干净，再以相同版本号加 `-Resume` 重试；不得新建第二个 tag 或额外 commit。

如 `gh` 未登录、权限不足、版本 tag 已存在、分支不是 `main`、安装器编译器缺失或质量门禁失败，立即停止；不得跳过检查、猜测凭据或伪称已发布。

## 自动化质量门禁

`.github/workflows/quality.yml` 在推送到 `main` 或向 `main` 提交 Pull Request 时自动运行非交互检查。它不替代本机 UI 验收，也不替代人工审查、发布授权或发布后安装验证。

## 文档语言

除根 README 的双语内容外，细节文档默认使用中文；产品名、命令、文件名、API 与公认技术术语保留英文。文档中的示例必须可复制、无个人数据且与实际代码一致。
