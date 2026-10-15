# 更新日志 / Changelog

本文件采用 [Semantic Versioning](https://semver.org/lang/zh-CN/) 的版本号约定。发布日期以 GitHub Release 为准。

## [Unreleased]

### 新增

- 首个公开发布版本：Windows x64 self-contained 便携版与 Inno Setup 安装程序。
- 双语 README、安装/使用/隐私/测试/发布/自动更新文档，以及完全虚构数据生成的界面截图。
- 发布前隐私扫描、SHA-256 校验文件、GitHub Release 发布脚本与 GitHub Actions 质量门禁。
- 单一版本来源、隔离演示启动参数、稳定 UI automation 标识和 STA UI smoke test。

### 修复与加固

- JSONL 字段类型异常、可编辑本地索引中的 null 字段和非法工作目录不再导致扫描路径崩溃。
- 刷新期间禁用会写入索引的操作，避免旧扫描快照覆盖用户刚保存的标签或移除状态。
- 关闭窗体时取消刷新续体对已释放控件的访问。
- OMP 可执行文件支持显式路径、默认安装目录与 `PATH` 的顺序查找。

在 GitHub Release、tag 和资产校验全部完成前，本节不代表已发布版本。
