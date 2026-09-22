package com.xueqing.app.infrastructure.auth

import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.infrastructure.remote.ProviderSessionTokenSource
import java.time.Instant
import java.util.UUID
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SessionStartupBootstrapRemoteTest {
    @Test
    fun `signed out never delegates to provider`() {
        val tokenSource = ProviderSessionTokenSource(
            "production-sg-v1",
            "xueqing-native-prod-sg",
        )
        var calls = 0
        val remote = SessionStartupBootstrapRemote(
            tokenSource = tokenSource,
            awaitStartup = { ProductionStartupAvailability.Ready },
            delegate = PersonalBootstrapRemote {
                calls += 1
                loaded()
            },
        )

        assertEquals(PersonalBootstrapResult.AuthenticationRequired, remote.fetch())
        assertEquals(0, calls)
    }

    @Test
    fun `usable session delegates after startup barrier`() {
        val tokenSource = ProviderSessionTokenSource(
            "production-sg-v1",
            "xueqing-native-prod-sg",
            now = { Instant.parse("2026-09-22T00:00:00Z") },
        )
        tokenSource.establish(
            "fictional-access",
            Instant.parse("2026-09-22T01:00:00Z"),
        )
        var calls = 0
        val remote = SessionStartupBootstrapRemote(
            tokenSource = tokenSource,
            awaitStartup = { ProductionStartupAvailability.Ready },
            delegate = PersonalBootstrapRemote {
                calls += 1
                loaded()
            },
        )

        assertTrue(remote.fetch() is PersonalBootstrapResult.Loaded)
        assertEquals(1, calls)
    }

    @Test
    fun `startup failure fails closed without provider call`() {
        val tokenSource = ProviderSessionTokenSource(
            "production-sg-v1",
            "xueqing-native-prod-sg",
        )
        var calls = 0
        val remote = SessionStartupBootstrapRemote(
            tokenSource = tokenSource,
            awaitStartup = { ProductionStartupAvailability.Unavailable },
            delegate = PersonalBootstrapRemote {
                calls += 1
                loaded()
            },
        )

        assertTrue(remote.fetch() is PersonalBootstrapResult.ProtocolFailure)
        assertEquals(0, calls)
    }

    @Test
    fun `refresh required remains transient and never claims signed out`() {
        val tokenSource = ProviderSessionTokenSource(
            "production-sg-v1",
            "xueqing-native-prod-sg",
        )
        tokenSource.markRefreshRequired()
        val remote = SessionStartupBootstrapRemote(
            tokenSource = tokenSource,
            awaitStartup = { ProductionStartupAvailability.Ready },
            delegate = PersonalBootstrapRemote { loaded() },
        )

        assertTrue(remote.fetch() is PersonalBootstrapResult.UnknownResult)
    }

    @Test
    fun `startup availability can recover without replacing remote`() {
        val tokenSource = ProviderSessionTokenSource(
            "production-sg-v1",
            "xueqing-native-prod-sg",
            now = { Instant.parse("2026-09-22T00:00:00Z") },
        )
        tokenSource.establish(
            "fictional-access",
            Instant.parse("2026-09-22T01:00:00Z"),
        )
        var availability = ProductionStartupAvailability.Unavailable
        var calls = 0
        val remote = SessionStartupBootstrapRemote(
            tokenSource = tokenSource,
            awaitStartup = { availability },
            delegate = PersonalBootstrapRemote {
                calls += 1
                loaded()
            },
        )

        assertTrue(remote.fetch() is PersonalBootstrapResult.ProtocolFailure)
        assertEquals(0, calls)

        availability = ProductionStartupAvailability.Ready

        assertTrue(remote.fetch() is PersonalBootstrapResult.Loaded)
        assertEquals(1, calls)
    }

    private fun loaded(): PersonalBootstrapResult =
        PersonalBootstrapResult.Loaded(
            PersonalBootstrap(
                generatedAtServer = Instant.EPOCH,
                actor = PersonalBootstrapActor(
                    UUID.fromString("10000000-0000-0000-0000-000000000001"),
                    "虚构教师",
                ),
                organizations = emptyList(),
                teachingContexts = emptyList(),
            ),
        )
}
