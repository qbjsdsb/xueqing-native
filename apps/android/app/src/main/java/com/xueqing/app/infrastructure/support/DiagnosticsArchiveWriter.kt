package com.xueqing.app.infrastructure.support

import com.xueqing.app.application.support.DiagnosticsSnapshot
import java.io.OutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class DiagnosticsArchiveWriter(
    private val json: Json = Json { prettyPrint = true },
) {
    fun write(snapshot: DiagnosticsSnapshot, output: OutputStream) {
        val payload = buildPayload(snapshot).toString().toByteArray(Charsets.UTF_8)
        ZipOutputStream(output).use { zip ->
            zip.putNextEntry(ZipEntry(FILE_NAME))
            zip.write(payload)
            zip.closeEntry()
        }
    }

    internal fun serialize(snapshot: DiagnosticsSnapshot): String =
        json.encodeToString(
            kotlinx.serialization.json.JsonElement.serializer(),
            buildPayload(snapshot),
        )

    private fun buildPayload(snapshot: DiagnosticsSnapshot) = buildJsonObject {
        put("contract", "diagnostics_manifest_v1")
        put("generated_at", snapshot.generatedAt.toString())
        put("platform", buildJsonObject {
            put("kind", "android")
            put("os_version", snapshot.osVersion)
            put("architecture", snapshot.architecture)
            put("package_identity", snapshot.packageIdentity)
        })
        put("app", buildJsonObject {
            put("version", snapshot.appVersion)
            put("source_commit", snapshot.sourceCommit)
            put("client_contract_version", snapshot.clientContractVersion)
            put("local_schema_version", snapshot.localSchemaVersion)
        })
        put("deployment", buildJsonObject {
            put("profile_id", snapshot.deployment.profileId)
            put("environment_id", snapshot.deployment.environmentId)
            put("trust_domain_id", snapshot.deployment.trustDomainId)
            put("provider_id", snapshot.deployment.providerId)
        })
        put("runtime", buildJsonObject {
            put("session_state", snapshot.sessionState.wireValue)
            put("last_sync_category", snapshot.lastSyncCategory.wireValue)
        })
        put("compatibility", buildJsonObject {
            put("state", snapshot.compatibility.state.wireValue)
            if (snapshot.compatibility.reasonCode == null) {
                put("reason_code", kotlinx.serialization.json.JsonNull)
            } else {
                put("reason_code", snapshot.compatibility.reasonCode)
            }
            if (snapshot.compatibility.policyRevision == null) {
                put("policy_revision", kotlinx.serialization.json.JsonNull)
            } else {
                put("policy_revision", snapshot.compatibility.policyRevision)
            }
        })
        put("queues", buildJsonObject {
            put("pending_intents", snapshot.queues.pendingIntents)
            put("outbox_items", snapshot.queues.outboxItems)
            put("attachment_staging", snapshot.queues.attachmentStaging)
        })
        put("recent_error_codes", buildJsonArray {
            snapshot.recentErrorCodes.forEach(::add)
        })
        put("archive_files", buildJsonArray {
            add(buildJsonObject {
                put("name", FILE_NAME)
                put("contract", "diagnostics_manifest_v1")
            })
        })
    }

    private companion object {
        const val FILE_NAME = "diagnostics.json"
    }
}
