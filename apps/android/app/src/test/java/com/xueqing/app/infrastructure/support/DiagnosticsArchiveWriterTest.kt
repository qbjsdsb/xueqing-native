package com.xueqing.app.infrastructure.support

import com.xueqing.app.application.support.DiagnosticCompatibility
import com.xueqing.app.application.support.DiagnosticCompatibilityState
import com.xueqing.app.application.support.DiagnosticDeployment
import com.xueqing.app.application.support.DiagnosticQueueCounts
import com.xueqing.app.application.support.DiagnosticSessionState
import com.xueqing.app.application.support.DiagnosticSyncCategory
import com.xueqing.app.application.support.DiagnosticsSnapshot
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.time.Instant
import java.util.zip.ZipInputStream
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class DiagnosticsArchiveWriterTest {
    @Test
    fun `archive contains only allowlisted diagnostics json`() {
        val bytes = ByteArrayOutputStream().also {
            DiagnosticsArchiveWriter().write(snapshot(), it)
        }.toByteArray()

        val zip = ZipInputStream(ByteArrayInputStream(bytes))
        val entry = zip.nextEntry
        assertEquals("diagnostics.json", entry.name)
        val body = zip.readBytes().toString(Charsets.UTF_8)
        val root = Json.parseToJsonElement(body) as JsonObject
        assertEquals("diagnostics_manifest_v1", (root["contract"] as JsonPrimitive).content)
        val runtime = root["runtime"] as JsonObject
        assertEquals("authenticated", (runtime["session_state"] as JsonPrimitive).content)
        assertEquals(null, zip.nextEntry)
    }

    @Test
    fun `unknown queue counts serialize as null`() {
        val body = DiagnosticsArchiveWriter().serialize(
            snapshot(queueCounts = DiagnosticQueueCounts(null, null, null)),
        )
        val root = Json.parseToJsonElement(body) as JsonObject
        val queues = root["queues"] as JsonObject
        assertEquals(JsonNull, queues["pending_intents"])
        assertEquals(JsonNull, queues["outbox_items"])
        assertEquals(JsonNull, queues["attachment_staging"])
    }

    @Test
    fun `private teaching sentinel cannot enter machine-readable error field`() {
        assertThrows(IllegalArgumentException::class.java) {
            snapshot(errorCodes = listOf("虚构学生甲：这段课堂观察绝不能进入诊断包"))
        }
    }

    @Test
    fun `serialized manifest contains no credential or teaching fields`() {
        val body = DiagnosticsArchiveWriter().serialize(snapshot())

        for (forbidden in listOf(
            "student_name",
            "observation_text",
            "access_token",
            "refresh_token",
            "authorization",
            "attachment_bytes",
            "email",
            "password",
        )) {
            assertFalse(body.lowercase().contains(forbidden))
        }
    }

    private fun snapshot(
        errorCodes: List<String> = listOf("XQ_NETWORK_TIMEOUT"),
        queueCounts: DiagnosticQueueCounts = DiagnosticQueueCounts(1, 1, 0),
    ) =
        DiagnosticsSnapshot(
            generatedAt = Instant.parse("2026-09-23T04:00:00Z"),
            osVersion = "17",
            architecture = "arm64-v8a",
            packageIdentity = "com.xueqing.app",
            appVersion = "1.0.0",
            sourceCommit = "62cad1dfce913775776e4cbd60add6ecab1aefc2",
            clientContractVersion = 1,
            localSchemaVersion = 3,
            deployment = DiagnosticDeployment(
                profileId = "prod-singapore-v1",
                environmentId = "production",
                trustDomainId = "xueqing-prod-sg",
                providerId = "supabase",
            ),
            sessionState = DiagnosticSessionState.Authenticated,
            lastSyncCategory = DiagnosticSyncCategory.Recent,
            compatibility = DiagnosticCompatibility(
                DiagnosticCompatibilityState.Supported,
                "XQ_CLIENT_SUPPORTED",
                "v1-initial",
            ),
            queues = queueCounts,
            recentErrorCodes = errorCodes,
        )
}
