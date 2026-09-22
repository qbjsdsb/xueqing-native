package com.xueqing.app.infrastructure.production

import android.content.Context
import com.xueqing.app.application.bootstrap.PersonalBootstrapProtocolFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.learning.StudentLearningFocusResult
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
import com.xueqing.app.infrastructure.auth.SessionStartupBootstrapRemote
import com.xueqing.app.infrastructure.auth.SupabaseAuthTransport
import com.xueqing.app.infrastructure.deployment.DeploymentProfile
import com.xueqing.app.infrastructure.deployment.DeploymentProfileParser
import com.xueqing.app.infrastructure.remote.HttpRpcTransport
import com.xueqing.app.infrastructure.remote.HttpStorageTransport
import com.xueqing.app.infrastructure.remote.ProviderSessionTokenSource
import com.xueqing.app.infrastructure.remote.SupabaseCommitObservationAttachmentAdapter
import com.xueqing.app.infrastructure.remote.SupabaseCreateObservationAdapter
import com.xueqing.app.infrastructure.remote.SupabaseObservationAttachmentStorageAdapter
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
import kotlinx.coroutines.runBlocking

class ProductionClientRuntime private constructor(
    private val appContext: Context,
    val profile: DeploymentProfile,
    val tokenSource: ProviderSessionTokenSource,
    val authCoordinator: ProviderAuthCoordinator,
    private val rawBootstrapRemote: PersonalBootstrapRemote,
    private val rawTodayRemote: PersonalTodayActionsRemote,
    private val rawFocusRemote: StudentLearningFocusRemote,
    private val observationRemote: SupabaseCreateObservationAdapter,
    private val attachmentStorageRemote: SupabaseObservationAttachmentStorageAdapter,
    private val attachmentCommandRemote: SupabaseCommitObservationAttachmentAdapter,
) {
    private val processScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val restoreStarted = AtomicBoolean(false)
    private val startupReady = CompletableDeferred<Unit>()

    @Volatile
    private var startupFailure: Throwable? = null

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
                authCoordinator.restore()
            } catch (error: CancellationException) {
                startupFailure = error
            } catch (error: Throwable) {
                startupFailure = error
            } finally {
                startupReady.complete(Unit)
            }
        }
    }

    fun installSyncRuntimes() {
        ObservationSyncRuntime.install { context ->
            val database = DraftDatabase.get(context)
            ObservationOutboxDrainer(
                dao = database.durableIntentDao(),
                bootstrapRemote = bootstrapRemote,
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
        return if (startupFailure is null) {
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
                observationRemote = SupabaseCreateObservationAdapter(
                    sessionTokenSource = tokenSource,
                    transport = rpcTransport,
                ),
                attachmentStorageRemote = SupabaseObservationAttachmentStorageAdapter(
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
