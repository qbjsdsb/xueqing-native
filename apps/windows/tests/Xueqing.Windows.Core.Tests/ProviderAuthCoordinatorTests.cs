using System.Runtime.Versioning;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xueqing.Windows.Infrastructure.Auth;
using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Core.Tests;

[TestClass]
public sealed class ProviderAuthCoordinatorTests
{
    [TestMethod]
    public async Task Refresh_persists_rotated_refresh_token_before_exposing_access_token()
    {
        var source = CreateSource();
        var vault = new FakeVault("old-refresh");
        var transport = new FakeTransport
        {
            RefreshResult = Success("new-access", "new-refresh"),
        };
        var coordinator = new ProviderAuthCoordinator(source, vault, transport);

        var outcome = await coordinator.RefreshAsync();

        Assert.AreEqual(ProviderRefreshOutcome.Usable, outcome);
        Assert.AreEqual("new-refresh", vault.Value);
        Assert.AreEqual("new-access", await source.GetCurrentAccessTokenAsync());
        CollectionAssert.AreEqual(
            new[] { "load", "store:new-refresh" },
            vault.Events);
    }

    [TestMethod]
    public async Task Vault_failure_during_rotation_keeps_new_access_token_unusable()
    {
        var source = CreateSource();
        var vault = new FakeVault("old-refresh") { FailStore = true };
        var transport = new FakeTransport
        {
            RefreshResult = Success("new-access", "new-refresh"),
        };
        var coordinator = new ProviderAuthCoordinator(source, vault, transport);

        await Assert.ThrowsExactlyAsync<RefreshTokenVaultUnavailableException>(
            () => coordinator.RefreshAsync());

        Assert.AreEqual(ProviderSessionStatus.RefreshRequired, source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());
        Assert.AreEqual("old-refresh", vault.Value);
    }

    [TestMethod]
    public async Task Result_unknown_preserves_previous_refresh_token_for_safe_retry()
    {
        var source = CreateSource();
        var vault = new FakeVault("old-refresh");
        var transport = new FakeTransport
        {
            RefreshResult = new(ProviderRefreshDisposition.ResultUnknown),
        };
        var coordinator = new ProviderAuthCoordinator(source, vault, transport);

        var outcome = await coordinator.RefreshAsync();

        Assert.AreEqual(ProviderRefreshOutcome.RefreshRequired, outcome);
        Assert.AreEqual("old-refresh", vault.Value);
        Assert.AreEqual(ProviderSessionStatus.RefreshRequired, source.Snapshot.Status);
    }

    [TestMethod]
    public async Task Rejected_refresh_clears_vault_and_invalidates_session()
    {
        var source = CreateSource();
        var vault = new FakeVault("old-refresh");
        var transport = new FakeTransport
        {
            RefreshResult = new(ProviderRefreshDisposition.Rejected),
        };
        var coordinator = new ProviderAuthCoordinator(source, vault, transport);

        var outcome = await coordinator.RefreshAsync();

        Assert.AreEqual(ProviderRefreshOutcome.Invalid, outcome);
        Assert.IsNull(vault.Value);
        Assert.AreEqual(ProviderSessionStatus.Invalid, source.Snapshot.Status);
    }

    [TestMethod]
    public async Task Remote_signout_failure_still_clears_local_session_but_is_not_claimed_confirmed()
    {
        var source = CreateSource();
        source.Establish("access", DateTimeOffset.UtcNow.AddMinutes(10));
        var vault = new FakeVault("refresh");
        var transport = new FakeTransport { FailSignOut = true };
        var coordinator = new ProviderAuthCoordinator(source, vault, transport);

        var outcome = await coordinator.SignOutAsync();

        Assert.AreEqual(
            ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed,
            outcome);
        Assert.IsNull(vault.Value);
        Assert.AreEqual(ProviderSessionStatus.SignedOut, source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());
    }

    [TestMethod]
    public async Task Signin_does_not_expose_session_when_refresh_token_cannot_be_persisted()
    {
        var source = CreateSource();
        var vault = new FakeVault(null) { FailStore = true };
        var transport = new FakeTransport
        {
            SignInResult = new(
                "signed-in-access",
                "signed-in-refresh",
                DateTimeOffset.UtcNow.AddMinutes(10)),
        };
        var coordinator = new ProviderAuthCoordinator(source, vault, transport);

        await Assert.ThrowsExactlyAsync<RefreshTokenVaultUnavailableException>(
            () => coordinator.SignInWithPasswordAsync(
                "fictional@example.invalid",
                "fictional-password"));

        Assert.AreEqual(ProviderSessionStatus.SignedOut, source.Snapshot.Status);
        Assert.IsNull(await source.GetCurrentAccessTokenAsync());
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task Dpapi_refresh_token_vault_roundtrips_without_plaintext_and_is_scope_bound()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "xueqing-refresh-token-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var vault = new WindowsDpapiRefreshTokenVault(
                directory,
                "prod-sg",
                "xueqing-prod-sg");
            const string Token = "fictional-refresh-token-value";
            await vault.StoreAsync(Token);

            Assert.AreEqual(Token, await vault.LoadAsync());
            var persisted = await File.ReadAllBytesAsync(vault.TokenPath);
            var needle = Encoding.UTF8.GetBytes(Token);
            Assert.IsFalse(ContainsSequence(persisted, needle));

            var otherScope = new WindowsDpapiRefreshTokenVault(
                directory,
                "another-environment",
                "another-trust-domain");
            Assert.IsNull(await otherScope.LoadAsync());

            persisted[^1] ^= 0x01;
            await File.WriteAllBytesAsync(vault.TokenPath, persisted);
            await Assert.ThrowsExactlyAsync<RefreshTokenVaultUnavailableException>(
                async () => { _ = await vault.LoadAsync(); });

            await vault.ClearAsync();
            Assert.IsFalse(File.Exists(vault.TokenPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProviderSessionTokenSource CreateSource() =>
        new("prod-sg", "xueqing-prod-sg");

    private static ProviderRefreshResult Success(
        string accessToken,
        string refreshToken) =>
        new(
            ProviderRefreshDisposition.Success,
            new ProviderAuthTokens(
                accessToken,
                refreshToken,
                DateTimeOffset.UtcNow.AddMinutes(10)));

    private static bool ContainsSequence(
        ReadOnlySpan<byte> haystack,
        ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty)
        {
            return true;
        }

        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.Slice(index, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class FakeVault(string? initial) : IRefreshTokenVault
    {
        public string? Value { get; private set; } = initial;

        public bool FailStore { get; init; }

        public List<string> Events { get; } = [];

        public ValueTask<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("load");
            return ValueTask.FromResult(Value);
        }

        public ValueTask StoreAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"store:{refreshToken}");
            if (FailStore)
            {
                throw new RefreshTokenVaultUnavailableException("fictional vault failure");
            }

            Value = refreshToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("clear");
            Value = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTransport : IProviderAuthTransport
    {
        public ProviderAuthTokens? SignInResult { get; init; }

        public ProviderRefreshResult RefreshResult { get; init; } =
            new(ProviderRefreshDisposition.RetryableFailure);

        public bool FailSignOut { get; init; }

        public Task<ProviderAuthTokens> SignInWithPasswordAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                SignInResult
                ?? throw new InvalidOperationException("No fictional sign-in result configured."));

        public Task<ProviderRefreshResult> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RefreshResult);

        public Task SignOutLocalAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            if (FailSignOut)
            {
                throw new IOException("fictional remote sign-out failure");
            }

            return Task.CompletedTask;
        }
    }
}
