package com.xueqing.app.infrastructure.auth

import com.xueqing.app.application.bootstrap.PersonalBootstrapProtocolFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalBootstrapUnknownReason
import com.xueqing.app.infrastructure.remote.ProviderSessionStatus
import com.xueqing.app.infrastructure.remote.ProviderSessionTokenSource

enum class ProductionStartupAvailability {
    Ready,
    Unavailable,
}

/**
 * Synchronous bootstrap boundary for an already-backgrounded provider transport.
 *
 * Android presentation and WorkManager invoke PersonalBootstrapRemote from IO
 * dispatchers. This gate waits for the one process-start session restore before
 * allowing any provider read. It never treats provider subject as AppUser id.
 */
class SessionStartupBootstrapRemote(
    private val tokenSource: ProviderSessionTokenSource,
    private val awaitStartup: () -> ProductionStartupAvailability,
    private val delegate: PersonalBootstrapRemote,
) : PersonalBootstrapRemote {
    override fun fetch(): PersonalBootstrapResult {
        if (awaitStartup() == ProductionStartupAvailability.Unavailable) {
            return PersonalBootstrapResult.ProtocolFailure(
                PersonalBootstrapProtocolFailure.UnrecognizedResponse,
            )
        }

        return when (tokenSource.snapshot.status) {
            ProviderSessionStatus.Usable -> delegate.fetch()
            ProviderSessionStatus.SignedOut,
            ProviderSessionStatus.Invalid,
            -> PersonalBootstrapResult.AuthenticationRequired

            ProviderSessionStatus.RefreshRequired ->
                PersonalBootstrapResult.UnknownResult(
                    PersonalBootstrapUnknownReason.NetworkFailure,
                )
        }
    }
}
