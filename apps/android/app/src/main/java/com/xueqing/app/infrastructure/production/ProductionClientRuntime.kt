package com.xueqing.app.infrastructure.production

import android.content.Context
import com.xueqing.app.BuildConfig
import com.xueqing.app.application.bootstrap.PersonalBootstrapProtocolFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.compatibility.ClientCompatibilityRequest
import com.xueqing.app.application.compatibility.ConsequentialWriteGate
import com.xueqing.app.application.compatibility.ServerClientCompatibilityWriteGate
import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.learning.StudentLearningFocusResult
import com.xueqing.app.application.session.ClientSessionController
import com.xueqing.app.application.session.ClientSessionStage
import com.xueqing.app.application.session.ClientSessionState
import com.xueqing.app.durability.AttachmentOutboxDrainer
import com.xueqing.app.durability.AttachmentStagingRecoveryScheduler
import com.xueqing.app.durability.AttachmentStagingStore
import com.xueqing.app.durability.AttachmentSyncRuntime
import com.xueqing.app.durability.DraftDatabase
import com.xueqing.app.durability.ObservationOutboxDrainer
import com.xueqing.app.durability.ObservationSyncRuntime
import com.xueqing.app.durability.ProtectedAttachmentFileStore
import com.xueqing.app.infrastructure.auth.AndroidKeystoreRefreshTokenVault
import com.xueqing.app.infrastructure.auth.HttpAuthTransport
import com.xueqing.app.infrastructure.auth.ProductionStartupAvailability
import com.xueqing.app.infrastructure.auth.ProviderAuthCoordinator
import com.xueqing.app.infrastructure.auth.ProviderAuthTransportException
import com.xueqing.app.infrastructure.auth.ProviderAuthTransportFailureKind
import com.xueqing.app.infrastructure.auth.ProviderRefreshOutcome
import com.xueqing.app.infrastructure.auth.ProviderSignOutOutcome
import com.xueqing.app.infrastructure.auth.SessionStartupBootstrapRemote
import com.xueqing.app.infrastructure.auth.SupabaseAuthTransport
import com.xueqing.app.infrastructure.deployment.DeploymentProfile
import com.xueqing.app.infrastructure.deployment.DeploymentProfileParser
import com.xueqing.app.infrastructure.remote.HttpRpcTransport
import com.xueqing.app.infrastructure.remote.HttpStorageTransport
import com.xueqing.app.infrastructure.remote.ProviderSessionTokenSource
import com.xueqing.app.infrastructure.remote.SupabaseClientCompatibilityAdapter
import com.xueqing.app.infrastructure.remote.SupabaseCommitObservationAttachmentAdapter
import com.xueqing.app.infrastructure.remote.SupabaseCreateObservationAdapter
import com.xueqing.app.infrastructure.remote.SupabaseObservationAttachmentStorageAdapter
import com.xueqing.app.infrastructure.remote.SupabaseObservationAttachmentReadAdapter
import com.xueqing.app.infrastructure.remote.SupabasePersonalBootstrapAdapter
import com.xueqing.app.infrastructure.remote.SupabasePersonalTodayActionsAdapter
import com.xueqing.app.infrastructure.remote.SupabaseStudentLearningFocusAdapter
import java.util.concurrent.CancellationException
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.runBlocking

class ProductionClientRuntime private constructor(
    private val appContext: Context,
    val profile: DeploymentProfile,
    val tokenSource: ProviderSessionTokenSource,
    val authCoordinator: ProviderAuthCoordinator,
    private val rawBootstrapRemote: PersonalBootstrapRemote,
    private val rawTodayRemote: PersonalTodayActionsRemote,
    private val rawFocusRemote: StudentLearningFocusRemote,
    private val compatibilityGate: ConsequentialWriteGate,
    private val observationRemote: SupabaseCreateObservationAdapter,
    private val attachmentStorageRemote: SupabaseObservationAttachmentStorageAdapter,
    val attachmentReadRemote: SupabaseObservationAttachmentReadAdapter,
    private val attachmentCommandRemote: SupabaseCommitObservationAttachmentAdapter,
) : ClientSessionController {
    private val processScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val restoreStarted = AtomicBoolean(false)
    private val startupReady = CompletableDeferred<Unit>()

    @Volatile
    private var startupFailure: Throwable? = null

    private val mutableSessionState = MutableStateFlow(
        ClientSessionState(
            ClientSessionStage.Restoring,
            "正在恢复安全登录状态…",
        ),
    )

    override val state: StateFlow<ClientSessionState> =
        mutableSessionState.asStateFlow()

    val bootstrapRemote: PersonalBootstrapRemote =
        SessionStartupBootstrapRemote(
            tokenSource = tokenSource,
            awaitStartup = ::awaitStartup,
            delegate = rawBootstrapRemote,
        )

    val todayRemote: PersonalTodayActionsRemote =
        PersonalTodayActionsRemote { expectedActorAppUserId ->
            if (awaitStartup() == ProductionStartupAvailability.Unavailable) {
                PersonalTodayActionsResult.Failed(configurationFailure())
            } else {
                rawTodayRemote.fetch(expectedActorAppUserId)
            }
        }

    val focusRemote: StudentLearningFocusRemote =
        StudentLearningFocusRemote { scope, expectedActorAppUserId ->
            if (awaitStartup() == ProductionStartupAvailability.Unavailable) {
                StudentLearningFocusResult.Failed(configurationFailure())
            } else {
                rawFocusRemote.fetch(scope, expectedActorAppUserId)
            }
        }

    fun startSessionRestore() {
        if (!restoreStarted.compareAndSet(false, true)) return

        processScope.launch {
            try {
                applyRefreshOutcome(authCoordinator.restore())
            } catch (error: CancellationException) {
                startupFailure = error
                mutableSessionState.value = ClientSessionState(
                    ClientSessionStage.ConfigurationUnavailable,
                    "安全登录恢复被中断，教学数据未加载。",
                )
            } catch (error: Throwable) {
                startupFailure = error
                mutableSessionState.value = ClientSessionState(
                    ClientSessionStage.ConfigurationUnavailable,
                    "安全登录状态不可用，教学数据未加载。",
                )
            } finally {
                startupReady.complete(Unit)
            }
        }
    }

    override suspend fun signIn(email: String, password: String) {
        mutableSessionState.value = ClientSessionState(
            ClientSessionStage.Busy,
            "正在登录…",
        )
        try {
            authCoordinator.signInWithPassword(email, password)
            // A later successful auth mutation must release a process-start
            // failure; otherwise the synchronous read barrier would keep
            // returning ConfigurationUnavailable after the session recovered.
            startupFailure = null
            mutableSessionState.value = ClientSessionState(
                ClientSessionStage.Authenticated,
            )
        } catch (error: CancellationException) {
            throw error
        } catch (error: ProviderAuthTransportException) {
            mutableSessionState.value = ClientSessionState(
                ClientSessionStage.SignedOut,
                when (error.kind) {
                    ProviderAuthTransportFailureKind.Rejected ->
                        "邮箱或密码不正确，或账号当前不可用。"
                    ProviderAuthTransportFailureKind.RateLimited ->
                        "尝试次数过多，请稍后再试。"
                    ProviderAuthTransportFailureKind.Transient,
                    ProviderAuthTransportFailureKind.ResultUnknown,
                    -> "网络暂时不可用，登录没有完成，请稍后重试。"
                    ProviderAuthTransportFailureKind.InvalidResponse ->
                        "登录服务返回无法验证的结果，已拒绝建立会话。"
                },
            )
        } catch (_: Throwable) {
            mutableSessionState.value = ClientSessionState(
                ClientSessionStage.ConfigurationUnavailable,
                "无法建立安全登录会话，教学数据未加载。",
            )
        }
    }

    override suspend fun retryRestore() {
        mutableSessionState.value = ClientSessionState(
            ClientSessionStage.Restoring,
            "正在重新连接…",
        )
        try {
            val outcome = authCoordinator.restore()
            // Restore may succeed after a transient vault/provider failure at
            // process start. Do not let that old failure poison future reads.
            startupFailure = null
            applyRefreshOutcome(outcome)
        } catch (error: CancellationException) {
            throw error
        } catch (_: Throwable) {
            mutableSessionState.value = ClientSessionState(
                ClientSessionStage.ReconnectRequired,
                "仍然无法确认登录状态，请检查网络后重试。",
            )
        }
    }

    override suspend fun clearLocalSession() {
        signOutInternal(
            signedOutMessage = "已退出此设备账号，请重新登录。",
        )
    }

    override suspend fun signOut() {
        signOutInternal(
            signedOutMessage = "已退出登录。",
        )
    }

    private suspend fun signOutInternal(signedOutMessage: String) {
        mutableSessionState.value = ClientSessionState(
            ClientSessionStage.Busy,
            "正在退出登录…",
        )
        try {
            val outcome = authCoordinator.signOut()
            // A confirmed local session mutation means the auth/runtime
            // boundary is usable again even if initial process restore failed.
            startupFailure = null
            mutableSessionState.value = ClientSessionState(
                ClientSessionStage.SignedOut,
                if (outcome == ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed) {
                    "已退出本机登录；服务器撤销暂未确认。"
                } else {
                    signedOutMessage
                },
            )
        } catch (error: CancellationException) {
            throw error
        } catch (_: Throwable) {
            mutableSessionState.value = ClientSessionState(
                ClientSessionStage.ConfigurationUnavailable,
                "本机安全会话无法完整清除，已停止继续使用旧会话。",
            )
        }
    }

    private fun applyRefreshOutcome(outcome: ProviderRefreshOutcome) {
        mutableSessionState.value = when (outcome) {
            ProviderRefreshOutcome.Usable -> ClientSessionState(
                ClientSessionStage.Authenticated,
            )
            ProviderRefreshOutcome.SignedOut -> ClientSessionState(
                ClientSessionStage.SignedOut,
                "请使用你的学情账号登录。",
            )
            ProviderRefreshOutcome.Invalid -> ClientSessionState(
                ClientSessionStage.SignedOut,
                "登录状态已失效，请重新登录。",
            )
            ProviderRefreshOutcome.RefreshRequired -> ClientSessionState(
                ClientSessionStage.ReconnectRequired,
                "暂时无法确认当前登录状态。可以重试连接，或退出此设备账号后重新登录。",
            )
        }
    }

    fun installSyncRuntimes() {
        ObservationSyncRuntime.install { context ->
            val database = DraftDatabase.get(context)
            ObservationOutboxDrainer(
                dao = database.durableIntentDao(),
                bootstrapRemote = bootstrapRemote,
                compatibilityGate = compatibilityGate,
                observationRemote = observationRemote,
                environmentId = profile.environmentId,
            )
        }

        AttachmentSyncRuntime.install { context ->
            val database = DraftDatabase.get(context)
            val stagingStore = AttachmentStagingStore(
                dao = database.attachmentStagingDao(),
                protectedFiles = ProtectedAttachmentFileStore(context),
            )
            AttachmentOutboxDrainer(
                dao = database.attachmentStagingDao(),
                stagingStore = stagingStore,
                bootstrapRemote = bootstrapRemote,
                compatibilityGate = compatibilityGate,
                storageRemote = attachmentStorageRemote,
                commandRemote = attachmentCommandRemote,
                environmentId = profile.environmentId,
                onCleanupNeeded = {
                    AttachmentStagingRecoveryScheduler.scheduleAfterGrace(context)
                },
            )
        }
    }

    private fun awaitStartup(): ProductionStartupAvailability {
        startSessionRestore()
        runBlocking {
            startupReady.await()
        }
        return if (startupFailure == null) {
            ProductionStartupAvailability.Ready
        } else {
            ProductionStartupAvailability.Unavailable
        }
    }

    private fun configurationFailure() =
        LearningReadFailure(
            LearningReadFailureKind.InvalidResponse,
            "XQ_CLIENT_CONFIGURATION_UNAVAILABLE",
        )

    companion object {
        private const val PROFILE_ASSET = "deployment_profile.json"
        private const val CLIENT_CONTRACT_VERSION = 1
        private val REQUIRED_CAPABILITIES = setOf(
            "auth",
            "database-rpc",
            "edge-functions",
            "private-storage",
        )

        fun create(context: Context): ProductionClientRuntime {
            val appContext = context.applicationContext
            val profileJson = appContext.assets.open(PROFILE_ASSET)
                .bufferedReader(Charsets.UTF_8)
                .use { it.readText() }
            val profile = DeploymentProfileParser.parse(profileJson)
            require(profile.providerId == "supabase") {
                "Unsupported production provider."
            }
            require(profile.capabilities.toSet().containsAll(REQUIRED_CAPABILITIES)) {
                "Production deployment profile is missing required capabilities."
            }

            val tokenSource = ProviderSessionTokenSource(
                environmentId = profile.environmentId,
                trustDomainId = profile.trustDomainId,
            )
            val refreshVault = AndroidKeystoreRefreshTokenVault(
                context = appContext,
                environmentId = profile.environmentId,
                trustDomainId = profile.trustDomainId,
            )
            val authTransport = SupabaseAuthTransport(
                publishableKey = profile.publishableKey,
                http = HttpAuthTransport(profile.projectOrigin),
            )
            val authCoordinator = ProviderAuthCoordinator(
                tokenSource = tokenSource,
                refreshTokenVault = refreshVault,
                transport = authTransport,
            )
            val rpcTransport = HttpRpcTransport(
                baseUrl = profile.projectOrigin,
                publishableKey = profile.publishableKey,
            )
            val storageTransport = HttpStorageTransport(
                baseUrl = profile.projectOrigin,
                publishableKey = profile.publishableKey,
            )
            val compatibilityGate = ServerClientCompatibilityWriteGate(
                remote = SupabaseClientCompatibilityAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
                request = ClientCompatibilityRequest(
                    platform = "android",
                    appVersion = BuildConfig.VERSION_NAME,
                    contractVersion = CLIENT_CONTRACT_VERSION,
                ),
            )

            return ProductionClientRuntime(
                appContext = appContext,
                profile = profile,
                tokenSource = tokenSource,
                authCoordinator = authCoordinator,
                rawBootstrapRemote = SupabasePersonalBootstrapAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
                rawTodayRemote = SupabasePersonalTodayActionsAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
                rawFocusRemote = SupabaseStudentLearningFocusAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
                compatibilityGate = compatibilityGate,
                observationRemote = SupabaseCreateObservationAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
                attachmentStorageRemote = SupabaseObservationAttachmentStorageAdapter(
                    sessionTokenSource = tokenSource,
                    transport = storageTransport,
                ),
                attachmentReadRemote = SupabaseObservationAttachmentReadAdapter(
                    sessionTokenSource = tokenSource,
                    transport = storageTransport,
                ),
                attachmentCommandRemote = SupabaseCommitObservationAttachmentAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
            )
        }

        fun unavailableBootstrapRemote(): PersonalBootstrapRemote =
            PersonalBootstrapRemote {
                PersonalBootstrapResult.ProtocolFailure(
                    PersonalBootstrapProtocolFailure.UnrecognizedResponse,
                )
            }

        fun unavailableTodayRemote(): PersonalTodayActionsRemote =
            PersonalTodayActionsRemote {
                PersonalTodayActionsResult.Failed(
                    LearningReadFailure(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_CLIENT_CONFIGURATION_UNAVAILABLE",
                    ),
                )
            }

        fun unavailableFocusRemote(): StudentLearningFocusRemote =
            StudentLearningFocusRemote { _, _ ->
                StudentLearningFocusResult.Failed(
                    LearningReadFailure(
                        LearningReadFailureKind.InvalidResponse,
                        "XQ_CLIENT_CONFIGURATION_UNAVAILABLE",
                    ),
                )
            }
    }
}
