using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Windows.Infrastructure.LocalData;

public static class WindowsLocalScopeIdentity
{
    public static string ComputeActorScopeDirectoryName(
        string environmentId,
        string appUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);

        var environmentHash = HashSegment(environmentId);
        var appUserHash = HashSegment(appUserId);
        return HashSegment(string.Concat(environmentHash, appUserHash));
    }

    public static string ComputeOrganizationScopeDirectoryName(
        string environmentId,
        string appUserId,
        string organizationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);

        var environmentHash = HashSegment(environmentId);
        var appUserHash = HashSegment(appUserId);
        var organizationHash = HashSegment(organizationId);
        return HashSegment(string.Concat(environmentHash, appUserHash, organizationHash));
    }

    private static string HashSegment(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        try
        {
            var hash = SHA256.HashData(utf8);
            try
            {
                return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }
    }
}
