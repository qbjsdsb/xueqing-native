# Xueqing Native

> Xueqing 2.0 — 面向教师与教培机构的教学判断与行动工作台。

Xueqing Native 是对原 `qbjsdsb/xueqing` 的第二代原生重构。它不是把旧 Flutter 客户端逐行翻译成 Kotlin / C#，而是在保留已经验证过的领域模型、权限约束、事务语义和教学闭环的基础上，重新建立 Android 与 Windows 的原生体验、Local-first 客户端和长期可维护的工程边界。

## 产品核心

Xueqing 始终回答三个问题：

1. 这个学生现在最需要解决什么？
2. 老师下一步应该做什么？
3. 前一次教学是否有证据证明有效？

核心闭环：

```text
Student
  → Student Subject Profile
  → Learning Case
  → Evidence
  → Intervention
  → Assessment / Verification
  → Next Action
  → Stable / Closed
```

Xueqing 不是 ERP、CRM、排课系统、收费系统、Excel 网页版、Todo 工具或 AI 聊天机器人。收费、招生 CRM、完整排课和财务等能力不进入核心产品范围。

## 技术方向

### Windows

- C# / .NET 10 LTS
- WinUI 3
- Windows App SDK Stable
- CommunityToolkit.Mvvm
- SQLite3MC + DPAPI CurrentUser 保护 Durable Intent
- MSIX 作为正式打包方向

### Android

- Kotlin
- Jetpack Compose + Material 3
- ViewModel + Coroutines + Flow / StateFlow
- Room
- WorkManager

### Backend

- PostgreSQL-first
- 开发阶段默认使用 Supabase
- RLS / Grants 负责访问边界
- Database Functions / RPC 负责高风险事务命令
- Storage 负责受控附件对象
- Provider SDK 只允许存在于 Infrastructure Adapter 内

## 架构原则

### Share semantics, not UI

Android 与 Windows 不共享 UI，也不要求共享客户端业务实现。共享的是数据与领域语义、命令与不变量、权限模型、状态机、错误码、同步协议、服务端事务和验收测试。

Android = **Capture**：课堂现场、快速记录、拍照、今日行动。

Windows = **Organize + Think**：整理、筛选、时间线、深度查看、机构管理、键鼠效率。

### Server-authoritative domain

服务端 PostgreSQL 是正式业务状态的权威来源。客户端本地数据库用于即时体验、离线读取、可恢复草稿与受控 Outbox，不通过客户端时间戳或 Last Write Wins 擅自覆盖服务端状态。

高风险命令采用：

```text
operation_id
+ expected_version
+ server-side authorization
+ deterministic locking / invariant checks
+ atomic transaction
+ operation receipt
```

重复同一 `operation_id` 不得产生重复副作用。

### Local-first Command Sync, not CRDT

允许离线的首先是课堂捕捉、草稿和经过明确允许的低风险追加事实。生命周期、权限、成员、任课交接、学生合并等高风险命令在首版要求在线确认。

客户端 Outbox 负责安全重试；冲突由服务端 `expected_version` 和权威快照解决。首版不建设通用 CRDT、多主任意对象合并或依赖客户端 `updated_at` 的同步系统。

### Authorization is not a UI feature

Teaching Evidence、Intervention、Assessment、Quick Capture 等教学事实必须满足完整 Teaching Fact Gate。机构管理身份不能自动成为教学 actor，也不能绕过合法任课关系。

### Privacy before convenience

公开仓库只能使用虚构数据、脱敏示例和无 Secret 配置。真实学生、教师、家长数据、访问令牌、service role key、数据库密码、临时凭据、生产附件均不得进入 GitHub。

Local-first 引入新的本地数据安全风险，因此离线授权期限、本地缓存范围、加密方式、退出/停用清理和附件留存策略必须先通过安全 Gate 冻结。

## 当前阶段：Phase 1 核心教学闭环跨端落地

Phase 0 已完成并接受 **Architecture Baseline v1**。广泛架构研究已经停止；后续方向变化必须由新的实验/生产证据和 superseding ADR 驱动。

Windows 已经从 Native UX Reference 进入真实业务闭环：Personal Student / recent Observation、Observation → Learning Case、Current Focus、Personal Today，以及 **Primary Action 改期 / Verification + atomic Next Action** 均已接入真实 reference provider。正式 Action 写入采用 exact Case/Action version、server-side Teaching Fact Gate、operation receipt 与加密 Durable Intent；ResultUnknown / Transient / unverifiable response 会保留原 operation 与原 payload，只允许 same-operation retry。该链已经通过 Windows Core、真实 Supabase E2E、WinUI cloud build、MSIX 原位升级、加密 LocalState 与 Native UX smoke 的 exact-head 证明。

Android 已完成稳定 API 36 的 Kotlin + Compose 基线、PersonalBootstrap-backed 学生目录、Student → Quick Capture、Room Draft Engine、process-death recovery、SQLCipher + Android Keystore Durable Intent、Observation Outbox + WorkManager，以及真实 **Quick Capture → reference provider → authoritative Observation** 写入链。Observation Attachment 也已完成系统 Photo Picker、受保护本地 staging、图片衍生与 EXIF/GPS 清理、process-death 恢复、private Storage 上传、稳定 attachment/commit identity、CommitObservationAttachment receipt 与真实 reference-provider E2E。Offline Access Lease 的 72 小时上限、同 boot monotonic expiry 与 wall-clock rollback 检测也已形成可执行安全基线。

Provider Session/Auth、**IdentityLink 身份映射**、Projection 与 private Storage conformance 均已完成。应用业务身份由 application-owned `AppUser` 持有；provider-specific identity、SDK 与 transport 继续限制在 Infrastructure Adapter 边界。四层 provider evidence 已共同关闭 Provider Adapter Conformance Spike；生产 provider/region 与数据驻留仍是单独的发布前 Gate。

Windows 的 authoritative Case workspace 已完成：Student Detail 按需完整 Case history、Current Focus、Today、Action progression，以及正式 Learning Case lifecycle（confirm / intervene / pending verification / stable / close / reopen）均已接入真实 reference provider。Lifecycle 写入使用 exact Case/Action version、server-side authorization、operation receipt 与加密 Durable Intent；ResultUnknown 只允许复用原 operation_id 继续确认。该链已经通过 Windows Core、真实 Supabase E2E、WinUI cloud build、MSIX 原位升级、加密 LocalState 与 Native UX smoke 的 exact-head 证明。

Evidence / Attachment staging 已在 Android 端完成第一条端到端闭环，并通过 exact-head 的 Foundation、Android device/durability 与真实 Supabase reference-provider 门禁。

Organization Management vertical slice 已完成第一条真实闭环：Owner/Admin 的权威管理投影、Windows 机构工作区入口门控、成员列表，以及幂等 Organization Invitation intent 均已通过真实 reference-provider E2E；管理可见性与教学责任继续严格分离。

Organization Invitation Acceptance 已完成并合入主线：已认证邀请人身份通过 provider-neutral invite email 与锁定邀请匹配；新的外部身份可原子创建 application-owned AppUser + IdentityLink + Membership，已有活动 IdentityLink 则复用原 AppUser；邀请状态、Membership 与 operation receipt 同事务提交，重放幂等，并发创建被序列化，且接受邀请永不创建 StudentTeacherAssignment。该链已经通过 backend pgTAP/concurrency、Windows real-provider E2E、Windows real-app/MSIX/Native UX 与 Android device reference-provider exact-head 门禁。

Organization Invitation Delivery 已完成并合入主线：邀请发送先持久化 provider-side delivery intent，再由受信任服务端适配器发送；same-operation replay 不会重复投递，provider/network ResultUnknown 保留为 `dispatching`，客户端不接触 secret，邮件成功也不会创建 Membership 或 StudentTeacherAssignment。该链已通过真实本地 Mailpit 单封邮件证明以及 Windows / Android reference-provider 回归。

Provider Projection Conformance 已完成并合入主线：两个独立真实外部 Auth 身份通过 `IdentityLink` 映射到同一 application-owned AppUser 后，PersonalBootstrap、Recent Observation、Current Focus、Case history、Personal Today 与 Organization Management 保持相同业务 payload；provider/session 字段不得泄漏，最终迁移后的业务 projection 不得直接调用 `auth.jwt()` / `auth.uid()`，真实 provider session 在跨机构/缺失 Assignment/teacher-only management 场景继续 fail closed。该 Gate 已通过 exact-head Backend、Windows 与 Android reference-provider 回归。

Provider Storage Conformance 也已完成并合入主线：两个独立真实外部 Auth 身份只通过 `IdentityLink` 解析到同一 application-owned AppUser 后，private Attachment upload/read/cross-commit 保持同一教学权限语义；单个 IdentityLink revoke 只撤销对应 provider identity；匿名读取继续拒绝；最终迁移后的 Storage authorization helper 不得直接依赖 `auth.jwt()` / `auth.uid()`。Session/Auth、IdentityLink、Projection 与 Storage 四层证据因此共同关闭原先更宽泛的 Provider Adapter Conformance Spike。

Windows Organization Invitation Product Closure 已完成并合入主线：WinUI 端已接入 capability-gated 邀请创建与可信 Delivery，ResultUnknown 保留同一 operation/intent，Create → Delivery → Authentication → Acceptance → Membership 的产品闭环已经有真实 provider 与 MSIX/Native UX 证据。

Windows 原生产品化已完成当前 Phase 1 收口。PR #68 已接受 Mica + WinUI TitleBar + NavigationView 原生 Shell 和可下载测试 MSIX；PR #69 已将 Today、Learning 与 Organization Management 从工程/原型表达收敛为真实工作面，并把视觉证据门禁加固到可检测 stale screenshot；Issue #70 通过 PR #72 / #73 完成 Students list/detail 的原生键盘效率、焦点恢复、可见文案、列表密度和无障碍语义，且保持既有 320-DIP 列宽与窗口断点不变。当前没有新的 Windows UI 代码执行线，不应为了“继续优化”而重开已经通过的产品化 Gate。

Android Final Native UX PR #66 已同步到最新 main，自动化 exact-head 再次通过 Foundation、API 36 device/durability、reference-provider、adaptive/large-text、accessibility 与视觉证据 Gate；剩余的是明确的真机人工验收：中文拼音 IME、TalkBack 播报质量，以及 predictive/system Back + Photo Picker 返回上下文。

生产 Provider Region / Data Residency 已完成运营决策：V1 不要求学生/教师数据位于中国大陆，第一权威生产拓扑选择 **Supabase Hosted / Singapore `ap-southeast-1`**。Private Attachment 继续使用 private bucket 与 Xueqing 授权，接受 Supabase global CDN/edge transit；Invitation Delivery 必须显式请求并由服务端 fail-closed 校验 `ap-southeast-1` 执行区域。PR #63 现为当前唯一自动代码/证据执行线，仍须证明真实 Xueqing Native 生产项目区域、运行时拓扑、日志/诊断策略和独立数据库 + private Storage 备份路径后才能关闭。其后顺序收敛为 **Backup/Restore → CloudBase second-provider conformance → Signing/Recovery → V1 RC**，不做 active-active 或 live dual-write。

动态工程状态以 `docs/project/PROJECT_STATE.yaml` 为准。

## 仓库布局

```text
apps/
  windows/
  android/
backend/
  supabase/
contracts/
  domain/
  commands/
  permissions/
  sync/
  errors/
  schemas/
docs/
  product/
  architecture/
  adr/
  references/
  ux/
  migration/
tools/
  diagnostics/
  migration/
  fixtures/
.github/
  workflows/
```

## Legacy Xueqing

原项目 `qbjsdsb/xueqing` 保留为只读经验来源与迁移依据：迁移领域语义，不复制 Flutter UI；迁移已验证的安全与事务规则，不逐文件复制历史 migration；迁移真实 UX 教训，不迁移跨平台响应式技术债。

旧项目不得因为新项目初始化而被修改或删除。

## 开发纪律

- Git migrations 是数据库结构的事实源；
- ADR 是已接受架构决策的事实源；
- 业务规则改变必须先更新契约 / ADR，再改客户端；
- Provider-specific SDK 不得渗透 View / ViewModel / Domain；
- 不为“架构漂亮”制造无意义抽象；
- 不因为开源项目功能多就照搬产品范围；
- 首先复用思想和模式，直接复制代码前必须确认许可证兼容性。

## License

Apache License 2.0。详见 `LICENSE`。
