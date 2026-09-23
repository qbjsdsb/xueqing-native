using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ProviderSessionTokenSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task New_session_is_signed_out_and_returns_no_token()
    {
        var clock = new ManualTimeProvider(Now);
        var source = Create(clock);

        Assert.AreEqual(ProviderSessionStatus.SignedOut, source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());
    }

    [TestMethod]
    public async Task Established_session_returns_token_only_before_expiry()
    {
        var clock = new ManualTimeProvider(Now);
        var source = Create(clock);
        source.Establish("fictional-access-token", Now.AddMinutes(5));

        Assert.AreEqual(
            "fictional-access-token",
            await source.GetCurrentAccessTokenAsync());
        Assert.AreEqual(ProviderSessionStatus.Usable, source.Snapshot.Status);
        Assert.AreEqual(Now.AddMinutes(5), source.Snapshot.AccessTokenExpiresAt);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.IsNull(await source.GetCurrentAccessTokenAsync());
        Assert.AreEqual(
            ProviderSessionStatus.RefreshRequired,
            source.Snapshot.Status);
        Assert.IsNull(source.Snapshot.AccessTokenExpiresAt);
    }

    [TestMethod]
    public async Task Refresh_invalid_and_sign_out_states_fail_closed()
    {
        var clock = new ManualTimeProvider(Now);
        var source = Create(clock);

        source.Establish("token-one", Now.AddMinutes(5));
        source.MarkRefreshRequired();
        Assert.AreEqual(
            ProviderSessionStatus.RefreshRequired,
            source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());

        source.Establish("token-two", Now.AddMinutes(5));
        source.Invalidate();
        Assert.AreEqual(ProviderSessionStatus.Invalid, source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());

        source.Establish("token-three", Now.AddMinutes(5));
        source.SignOut();
        Assert.AreEqual(ProviderSessionStatus.SignedOut, source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());
    }

    [TestMethod]
    public void Expired_or_malformed_session_input_is_rejected()
    {
        var clock = new ManualTimeProvider(Now);
        var source = Create(clock);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => source.Establish("token", Now));
        Assert.ThrowsExactly<ArgumentException>(
            () => source.Establish("token with spaces", Now.AddMinutes(5)));
    }

    [TestMethod]
    public async Task Scope_is_fixed_and_new_establishment_replaces_only_memory_token()
    {
        var clock = new ManualTimeProvider(Now);
        var source = Create(clock);

        source.Establish("first-token", Now.AddMinutes(5));
        source.Establish("second-token", Now.AddMinutes(10));

        Assert.AreEqual("prod-sg", source.Snapshot.EnvironmentId);
        Assert.AreEqual("xueqing-prod-sg", source.Snapshot.TrustDomainId);
        Assert.AreEqual(
            "second-token",
            await source.GetCurrentAccessTokenAsync());
        Assert.AreEqual(Now.AddMinutes(10), source.Snapshot.AccessTokenExpiresAt);
    }

    private static ProviderSessionTokenSource Create(TimeProvider clock) =>
        new("prod-sg", "xueqing-prod-sg", clock);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
