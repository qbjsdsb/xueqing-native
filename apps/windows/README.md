# Windows client

Windows 是 Xueqing Native 的 **Organize + Think** 工作区。

当前阶段：**Phase 1 / Native UX Prototype**。

## 已证明的 Windows 基线

Windows 不再处于最初的 unpackaged bootstrap 阶段。当前 `main` 已经证明：

- .NET 10 + WinUI 3 可在标准 GitHub Windows runner 上从 committed lock graph 构建；
- 真实 `Xueqing.Windows` App 使用 MSIX 打包；
- `ApplicationData.Current.LocalFolder` 下的 Durable Intent 使用 SQLite3MC `2.4.0` 加密；
- 随机数据库主密钥由 DPAPI `CurrentUser` 包装；
- environment + app user + organization + installation 作用域参与 LocalState 隔离，原始身份不进入文件路径；
- 真实 MSIX v1 安装、App 启动/重启、v2 原位升级后可以继续打开同一加密数据库并保持稳定 `operation_id`/Outbox 幂等语义；
- SQLite3MC native runtime、raw payload marker absence、key sidecar、作用域路径隐私与卸载清理均由 CI gate 验证；
- Windows Core/Infrastructure/Security 测试包含对 SQLite3MC encrypted-VFS 长路径限制的回归覆盖。

这些能力属于基础设施基线，不代表正式业务 UI、生产签名、正式账号/provider、Offline Access Lease、附件协议、备份恢复或完整本地安全策略已经完成。

## 当前下一 Gate：Issue #11 Native UX Prototype

接下来不立即接生产 Supabase 或正式学生数据。先使用确定性虚构数据把 Native UX Foundation 从文档 Candidate 变成可执行 WinUI 证据。

首批原型只覆盖：

1. Today work queue；
2. Students list/detail + Student Detail；
3. Learning Case narrative timeline；
4. Organization Management 的信息密度与列降级。

验收重点：

- 800 / 960 / 1024 / 1280 / 1600 DIP；
- 100 / 150 / 200% 文本缩放，并在可用时做 225% destructive pass；
- Light / Dark / High Contrast；
- 键盘完整核心旅程、鼠标 hover/selection/context menu；
- detail/dialog/light-layer 关闭后的焦点恢复；
- 中文 IME；
- 长中文文本自然换行；
- 单一 authoritative desktop breakpoint source；
- 1000 学生等 hostile fixture 下的列表虚拟化与响应性；
- 不出现 Card Soup、KPI dashboard、chip wall、装饰性 gradient/glass/AI styling。

UX 语义以 `docs/ux/NATIVE_UX_FOUNDATION.md`、`CORE_SCREEN_ARCHITECTURE.md`、`INTERACTION_STATE_CONTRACT.md` 与 `PROTOTYPE_ACCEPTANCE_MATRIX.md` 为准；任何 breakpoint、pane width、Case reading width 或视觉 token 都必须由原生可执行证据支持后才能冻结。

本阶段仍只使用确定性虚构数据。不得把真实学生、教师、家长信息带入本目录或 CI artifact。
