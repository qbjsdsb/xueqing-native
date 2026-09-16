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
- SQLite（本地缓存、Outbox 与同步状态；具体加密方案经 Spike 冻结）
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

Local-first 引入新的本地数据安全风险，因此离线授权期限、本地缓存范围、加密方式、退出/停用清理和附件留存策略必须先通过安全 Spike 冻结。

## 当前阶段：Architecture Spikes

Phase 0 已完成并接受 **Architecture Baseline v1**。广泛架构研究已经停止；后续方向变化必须由新的实验/生产证据和 superseding ADR 驱动。

当前第一优先级是 **WinUI 3 Architecture Spike**。Spike 只使用确定性的虚构数据，先验证 WinUI 3、SQLite、列表/时间线虚拟化、窗口缩放与单一断点来源、100/150/200% DPI、暗色/高对比度、键盘与中文 IME、Durable Outbox、云端 Windows CI、MSIX 打包和安装/卸载 smoke。

随后进入 Android Architecture Spike 与 Backend/API-schema + Provider Conformance Spike。Spike 通过后才开始正式学生业务 Vertical Slice。

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
