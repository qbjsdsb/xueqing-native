package com.xueqing.app.infrastructure.deployment

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class DeploymentProfileParserTest {
    @Test
    fun `valid profile parses complete production scope`() {
        val profile = DeploymentProfileParser.parse(valid)

        assertEquals("xueqing-prod-sg-v1", profile.profileId)
        assertEquals("production-sg-v1", profile.environmentId)
        assertEquals("xueqing-native-prod-sg", profile.trustDomainId)
        assertEquals("ap-southeast-1", profile.requiredEdgeRegion)
        assertEquals(
            "0123456789abcdef0123456789abcdef01234567",
            profile.source.commit,
        )
        assertEquals(
            listOf(
                "auth",
                "database-rpc",
                "edge-functions",
                "private-storage",
            ),
            profile.capabilities,
        )
    }

    @Test
    fun `unknown field fails closed`() {
        assertThrows(IllegalArgumentException::class.java) {
            DeploymentProfileParser.parse(
                valid.replace(
                    "\"contract\": \"deployment_profile_v1\"",
                    "\"contract\": \"deployment_profile_v1\", \"unexpected\": true",
                ),
            )
        }
    }

    @Test
    fun `wrong commit region and insecure origin fail closed`() {
        listOf(
            valid.replace(
                "0123456789abcdef0123456789abcdef01234567",
                "not-a-sha",
            ),
            valid.replace("ap-southeast-1", "singapore"),
            valid.replace(
                "https://example.supabase.co",
                "http://localhost:54321",
            ),
        ).forEach { invalid ->
            assertThrows(IllegalArgumentException::class.java) {
                DeploymentProfileParser.parse(invalid)
            }
        }
    }

    @Test
    fun `capabilities must be sorted unique and supported`() {
        val invalid = valid.replace(
            "\"database-rpc\"",
            "\"auth\"",
        )
        assert(invalid != valid)
        assertThrows(IllegalArgumentException::class.java) {
            DeploymentProfileParser.parse(invalid)
        }
    }

    private val valid = """
        {
          "contract": "deployment_profile_v1",
          "profile_id": "xueqing-prod-sg-v1",
          "environment_id": "production-sg-v1",
          "trust_domain_id": "xueqing-native-prod-sg",
          "provider_id": "supabase",
          "project_origin": "https://example.supabase.co",
          "publishable_key": "sb_publishable_fictional_public_0001",
          "required_edge_region": "ap-southeast-1",
          "capabilities": [
            "auth",
            "database-rpc",
            "edge-functions",
            "private-storage"
          ],
          "source": {
            "repository": "qbjsdsb/xueqing-native",
            "commit": "0123456789abcdef0123456789abcdef01234567"
          }
        }
    """.trimIndent()
}
