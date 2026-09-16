# Windows client

Windows 是 Xueqing Native 的 **Organize + Think** 工作区。

当前阶段：**Phase 1 / WinUI 3 Architecture Spike**。

本阶段不实现正式学生业务，只验证：

- .NET 10 + WinUI 3 云端构建；
- 单一布局断点来源，重点覆盖旧版 1280px 边界问题；
- 1000 名确定性虚构学生的列表与 List/Detail 壳；
- Core 与 UI 分层，使关键逻辑可在非 Windows runner 上测试；
- DPI、暗色/高对比度、中文 IME、键盘、SQLite、Durable Outbox、MSIX、安装/卸载 smoke 等后续 Spike Gate。

第一层 bootstrap 暂时使用 unpackaged self-contained WinUI，只用于隔离验证 XAML/SDK/云端构建链。正式分发方向仍是 MSIX；只有基础编译通过后才引入打包和签名变量。

所有数据必须是确定性虚构数据。不得把真实学生、教师、家长信息带入本目录或 CI artifact。
