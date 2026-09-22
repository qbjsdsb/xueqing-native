using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Infrastructure.Deployment;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class DeploymentProfileParserTests
{
    private const string Valid = """
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
        """;

    [TestMethod]
    public void Valid_profile_parses_complete_production_scope()
    {
        var profile = DeploymentProfileParser.Parse(Valid);

        Assert.AreEqual("xueqing-prod-sg-v1", profile.ProfileId);
        Assert.AreEqual("production-sg-v1", profile.EnvironmentId);
        Assert.AreEqual("xueqing-native-prod-sg", profile.TrustDomainId);
        Assert.AreEqual("ap-southeast-1", profile.RequiredEdgeRegion);
        Assert.AreEqual(
            "0123456789abcdef0123456789abcdef01234567",
            profile.Source.Commit);
        CollectionAssert.AreEqual(
            new[]
            {
                "auth",
                "database-rpc",
                "edge-functions",
                "private-storage",
            },
            profile.Capabilities.ToArray());
    }

    [TestMethod]
    public void Unknown_fields_fail_closed()
    {
        var invalid = Valid.Replace(
            "\"contract\": \"deployment_profile_v1\"",
            "\"contract\": \"deployment_profile_v1\", \"unexpected\": true",
            StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(
            () => DeploymentProfileParser.Parse(invalid));
    }

    [TestMethod]
    public void Wrong_commit_region_and_insecure_origin_fail_closed()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => DeploymentProfileParser.Parse(
                Valid.Replace(
                    "0123456789abcdef0123456789abcdef01234567",
                    "not-a-sha",
                    StringComparison.Ordinal)));

        Assert.ThrowsExactly<InvalidDataException>(
            () => DeploymentProfileParser.Parse(
                Valid.Replace(
                    "ap-southeast-1",
                    "singapore",
                    StringComparison.Ordinal)));

        Assert.ThrowsExactly<InvalidDataException>(
            () => DeploymentProfileParser.Parse(
                Valid.Replace(
                    "https://example.supabase.co",
                    "http://localhost:54321",
                    StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Capabilities_must_be_sorted_unique_and_supported()
    {
        var invalid = Valid.Replace(
            "\"auth\",\n    \"database-rpc\",",
            "\"database-rpc\",\n    \"auth\",",
            StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(
            () => DeploymentProfileParser.Parse(invalid));
    }
}
