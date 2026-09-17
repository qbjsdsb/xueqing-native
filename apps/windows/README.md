# Windows client

Windows 是 Xueqing Native 的 **Organize + Think** 工作区。

当前阶段：**Phase 1 / Native UX Prototype Gate**。

## 已接受的 Windows 基线

Windows 架构与真实本地数据/打包链已经完成关键证明：

- .NET 10 + WinUI 3 可从 Git 在 GitHub Windows runner 上稳定构建；
- Core / Infrastructure / UI 分层已建立，关键逻辑可在适合的最低层测试；
- Durable Intent 使用 SQLite3MC 加密数据库；
- 数据库主密钥由 DPAPI `CurrentUser` 保护；
- 真实 WinUI App 使用 MSIX 包身份与 `ApplicationData.Current.LocalFolder`；
- committed lock graph + locked restore 已进入 CI；
- 真实 App 已通过 v1 安装/运行 → 重启 → v2 原位升级 → 重新打开同一加密数据的生命周期 Gate；
- package family、installation id、数据库路径与稳定 `operation_id` 连续性已验证；
- SQLite3MC native runtime、raw payload marker 不落明文、scope-path 隐私和卸载清理已有自动化证据；
- Windows/SQLite3MC 加密长路径边界已经有回归测试，避免再次出现 SQLite Error 14。

上述基线不是业务 UI 的验收，也不意味着 Offline Access Lease、正式备份恢复、生产签名或全部本地数据安全策略已经完成。

## 当前下一道 Gate：Issue #11 Native UX Prototype

在不接生产 backend、不使用真实学生数据的前提下，用确定性虚构数据把 Native UX Candidate 变成真实 WinUI 可执行证据。首批只覆盖：

1. Today work queue；
2. Students list/detail + Student Detail；
3. Learning Case narrative timeline；
4. Organization Management 的密度与列降级。

必须重点验证：

- 800 / 960 / 1024 / 1280 / 1600 DIP；
- 100 / 150 / 200% 文本缩放，并在支持时做 225% destructive pass；
- Light / Dark / High Contrast；
- 键盘-only 核心流程、鼠标 hover/selection/context menu；
- detail/dialog/light-layer 关闭后的 selection/focus/context 恢复；
- 中文 IME；
- 1000 名确定性虚构学生的列表虚拟化与快速选择；
- Today hostile fixture、100-entry Case timeline 和大量 Organization rows；
- 一个统一、可解释的桌面布局断点来源。

UX v1 的最终 breakpoint、pane width、Case reading width 和视觉 token 不得从 mockup 直接冻结，必须来自真实 WinUI 原型证据。

## 本阶段边界

- 只使用确定性虚构数据；
- 不接生产 Supabase/provider；
- 不实现正式 auth/user/org selection；
- 不借 UX Prototype 修改已经验证的 DPAPI + SQLite3MC + MSIX Durable Intent 安全语义；
- 不同时推进 Android、backend schema 或正式学生业务 Vertical Slice；
- 不为了“好看”引入 Card Soup、KPI dashboard、渐变/玻璃/发光或 AI 风格装饰。

权威 UX 语义见 `../../docs/ux/NATIVE_UX_FOUNDATION.md`、`CORE_SCREEN_ARCHITECTURE.md`、`INTERACTION_STATE_CONTRACT.md` 与 `PROTOTYPE_ACCEPTANCE_MATRIX.md`。执行验收由 GitHub issue #11 跟踪。
