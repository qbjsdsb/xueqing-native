package com.xueqing.app.durability

import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.observation.ObservationCommandRemote
import com.xueqing.app.application.observation.ObservationCommandResult
import kotlin.math.min

class ObservationOutboxDrainer(
    private val dao: DurableIntentDao,
    private val bootstrapRemote: PersonalBootstrapRemote,
    private val observationRemote: ObservationCommandRemote,
    private val environmentId: String,
    private val clock: () -> Long = System::currentTimeMillis,
    private val leaseDurationMillis: Long = DEFAULT_LEASE_MILLIS,
) {
    data class Result(
        val processedCount: Int,
        val nextWakeAtEpochMillis: Long?,
        val blockedReason: BlockedReason? = null,
    )

    enum class BlockedReason {
        AuthenticationRequired,
        BootstrapTemporarilyUnavailable,
        BootstrapAccessUnavailable,
        BootstrapProtocolFailure,
    }

    suspend fun drainReady(maxItems: Int = DEFAULT_MAX_ITEMS): Result {
        require(maxItems > 0)

        val bootstrap = bootstrapRemote.fetch()
        val appUserId = when (bootstrap) {
            is PersonalBootstrapResult.Loaded -> bootstrap.bootstrap.actor.appUserId.toString()
            PersonalBootstrapResult.AuthenticationRequired -> {
                return Result(
                    processedCount = 0,
                    nextWakeAtEpochMillis = Math.addExact(clock(), AUTH_RETRY_MILLIS),
                    blockedReason = BlockedReason.AuthenticationRequired,
                )
            }
            is PersonalBootstrapResult.UnknownResult -> {
                return Result(
                    processedCount = 0,
                    nextWakeAtEpochMillis = Math.addExact(clock(), TRANSIENT_BOOTSTRAP_RETRY_MILLIS),
                    blockedReason = BlockedReason.BootstrapTemporarilyUnavailable,
                )
            }
            is PersonalBootstrapResult.AccessUnavailable -> {
                return Result(0, null, BlockedReason.BootstrapAccessUnavailable)
            }
            is PersonalBootstrapResult.ProtocolFailure -> {
                return Result(0, null, BlockedReason.BootstrapProtocolFailure)
            }
        }

        var processed = 0
        var shouldStopAfterCurrent = false
        while (processed < maxItems && !shouldStopAfterCurrent) {
            val now = clock()
            val claimed = dao.claimNextReady(
                environmentId = environmentId,
                appUserId = appUserId,
                nowEpochMillis = now,
                leaseDurationMillis = leaseDurationMillis,
            ) ?: break

            processed += 1
            val leaseId = requireNotNull(claimed.leaseId)

            if (claimed.commandType != ObservationOutboxCodec.COMMAND_TYPE) {
                dao.deadLetter(
                    localSequence = claimed.localSequence,
                    leaseId = leaseId,
                    errorClass = "LocalProtocol:UnknownCommandType",
                )
                continue
            }

            val request = runCatching { ObservationOutboxCodec.decodeRequest(claimed.payloadJson) }
                .getOrNull()
            if (
                request == null ||
                request.operationId.toString() != claimed.operationId ||
                request.organizationId.toString() != claimed.organizationId
            ) {
                dao.deadLetter(
                    localSequence = claimed.localSequence,
                    leaseId = leaseId,
                    errorClass = "LocalProtocol:CorruptPayload",
                )
                continue
            }

            when (val remoteResult = observationRemote.createObservation(request)) {
                is ObservationCommandResult.Accepted -> {
                    dao.acknowledge(
                        localSequence = claimed.localSequence,
                        leaseId = leaseId,
                        acknowledgedAtEpochMillis = clock(),
                        serverReceiptJson = ObservationOutboxCodec.encodeReceipt(remoteResult.receipt),
                    )
                }

                is ObservationCommandResult.Rejected -> {
                    dao.deadLetter(
                        localSequence = claimed.localSequence,
                        leaseId = leaseId,
                        errorClass = remoteResult.rejection.wireCode,
                    )
                }

                is ObservationCommandResult.AuthenticationRequired -> {
                    val retryAt = Math.addExact(clock(), AUTH_RETRY_MILLIS)
                    dao.retry(
                        localSequence = claimed.localSequence,
                        leaseId = leaseId,
                        errorClass = "AuthenticationRequired",
                        nextAttemptAtEpochMillis = retryAt,
                    )
                    shouldStopAfterCurrent = true
                }

                is ObservationCommandResult.UnknownResult -> {
                    val retryAt = Math.addExact(
                        clock(),
                        retryDelayMillis(attemptCount = claimed.attemptCount),
                    )
                    dao.retry(
                        localSequence = claimed.localSequence,
                        leaseId = leaseId,
                        errorClass = "Unknown:${remoteResult.reason.name}",
                        nextAttemptAtEpochMillis = retryAt,
                    )
                    // A transient transport/server failure is likely shared by
                    // the remaining queue. Stop rather than hammering it.
                    shouldStopAfterCurrent = true
                }

                is ObservationCommandResult.ProtocolFailure -> {
                    dao.deadLetter(
                        localSequence = claimed.localSequence,
                        leaseId = leaseId,
                        errorClass = "Protocol:${remoteResult.failure.name}",
                    )
                }
            }
        }

        return Result(
            processedCount = processed,
            nextWakeAtEpochMillis = dao.readNextWakeAt(environmentId, appUserId),
        )
    }

    private fun retryDelayMillis(attemptCount: Int): Long {
        val exponent = (attemptCount - 1).coerceIn(0, 6)
        val multiplier = 1L shl exponent
        return min(MAX_TRANSIENT_RETRY_MILLIS, BASE_TRANSIENT_RETRY_MILLIS * multiplier)
    }

    private companion object {
        const val DEFAULT_MAX_ITEMS = 20
        const val DEFAULT_LEASE_MILLIS = 2 * 60 * 1000L
        const val BASE_TRANSIENT_RETRY_MILLIS = 15 * 1000L
        const val MAX_TRANSIENT_RETRY_MILLIS = 15 * 60 * 1000L
        const val AUTH_RETRY_MILLIS = 5 * 60 * 1000L
        const val TRANSIENT_BOOTSTRAP_RETRY_MILLIS = 30 * 1000L
    }
}
