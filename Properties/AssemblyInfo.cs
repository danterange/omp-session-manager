using System.Runtime.CompilerServices;

// 仅向同仓库测试项目公开内部定位逻辑，避免为测试而扩大应用的公共 API。
[assembly: InternalsVisibleTo("OmpSessionManager.Tests")]
