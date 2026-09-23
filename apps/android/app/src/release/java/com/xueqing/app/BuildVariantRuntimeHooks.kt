package com.xueqing.app

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.session.ClientSessionController
import com.xueqing.app.application.session.ClientSessionStage
import com.xueqing.app.application.session.UnavailableClientSessionController
import com.xueqing.app.application.support.DiagnosticCompatibility
import com.xueqing.app.application.support.DiagnosticCompatibilityState
import com.xueqing.app.application.support.DiagnosticDeployment
import com.xueqing.app.application.support.DiagnosticQueueCounts
import com.xueqing.app.application.support.DiagnosticSessionState
import com.xueqing.app.application.support.DiagnosticSyncCategory
import com.xueqing.app.application.support.DiagnosticsSnapshot
import com.xueqing.app.durability.AttachmentSyncRuntime
import com.xueqing.app.durability.ObservationSyncRuntime
import com.xueqing.app.infrastructure.auth.ProviderAuthCoordinator
import com.xueqing.app.infrastructure.production.ProductionClientRuntime
import com.xueqing.app.infrastructure.support.DiagnosticsArchiveWriter
import java.io.OutputStream
import java.time.Instant

object BuildVariantRuntimeHooks {
    private val unavailableSessionController =
        UnavailableClientSessionController()
    @Volatile
    private var initialized = false

    @Volatile
    private var runtime: ProductionClientRuntime? = null

    @Synchronized
    fun onApplicationCreate(context: Context) {
        if (initialized) return
        initialized = true

        runtime = runCatching {
            ProductionClientRuntime.create(context).also { production ->
                production.startSessionRestore()
                production.installSyncRuntimes()
            }
        }.getOrNull()

        if (runtime == null) {
            ObservationSyncRuntime.clear()
            AttachmentSyncRuntime.clear()
        }
    }

    fun environmentId(context: Context): String =
        runtime(context)?.profile?.environmentId ?: "configuration-unavailable"

    fun bootstrapRemote(context: Context): PersonalBootstrapRemote =
        runtime(context)?.bootstrapRemote
            ?: ProductionClientRuntime.unavailableBootstrapRemote()

    fun todayRemote(context: Context): PersonalTodayActionsRemote =
        runtime(context)?.todayRemote
            ?: ProductionClientRuntime.unavailableTodayRemote()

    fun focusRemote(context: Context): StudentLearningFocusRemote =
        runtime(context)?.focusRemote
            ?: ProductionClientRuntime.unavailableFocusRemote()

    fun authCoordinator(context: Context): ProviderAuthCoordinator? =
        runtime(context)?.authCoordinator

    fun sessionController(context: Context): ClientSessionController? =
        runtime(context) ?: unavailableSessionController

    fun writeDiagnostics(context: Context, output: OutputStream) {
        val production = requireNotNull(runtime(context)) {
            "Production runtime is unavailable."
        }
        val profile = production.profile
        val packageInfo = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.packageManager.getPackageInfo(
                context.packageName,
                PackageManager.PackageInfoFlags.of(0),
            )
        } else {
            @Suppress("DEPRECATION")
            context.packageManager.getPackageInfo(context.packageName, 0)
        }
        val appVersion = requireNotNull(packageInfo.versionName) {
            "Installed package versionName is unavailable."
        }
        val sessionState = when (production.state.value.stage) {
            ClientSessionStage.Authenticated -> DiagnosticSessionState.Authenticated
            ClientSessionStage.ReconnectRequired -> DiagnosticSessionState.RefreshRequired
            ClientSessionStage.SignedOut -> DiagnosticSessionState.SignedOut
            ClientSessionStage.ConfigurationUnavailable -> DiagnosticSessionState.ConfigurationUnavailable
            ClientSessionStage.Restoring,
            ClientSessionStage.Busy,
            -> DiagnosticSessionState.RefreshRequired
        }

        DiagnosticsArchiveWriter().write(
            DiagnosticsSnapshot(
                generatedAt = Instant.now(),
                osVersion = Build.VERSION.RELEASE.ifBlank { Build.VERSION.SDK_INT.toString() },
                architecture = Build.SUPPORTED_ABIS.firstOrNull() ?: "unknown",
                packageIdentity = context.packageName,
                appVersion = appVersion,
                sourceCommit = profile.source.commit,
                clientContractVersion = 1,
                localSchemaVersion = 3,
                deployment = DiagnosticDeployment(
                    profileId = profile.profileId,
                    environmentId = profile.environmentId,
                    trustDomainId = profile.trustDomainId,
                    providerId = profile.providerId,
                ),
                sessionState = sessionState,
                lastSyncCategory = DiagnosticSyncCategory.Unknown,
                compatibility = DiagnosticCompatibility(
                    state = DiagnosticCompatibilityState.Unknown,
                    reasonCode = null,
                    policyRevision = null,
                ),
                queues = DiagnosticQueueCounts(
                    pendingIntents = null,
                    outboxItems = null,
                    attachmentStaging = null,
                ),
                recentErrorCodes = emptyList(),
            ),
            output,
        )
    }

    private fun runtime(context: Context): ProductionClientRuntime? {
        if (!initialized) {
            onApplicationCreate(context.applicationContext)
        }
        return runtime
    }
}
