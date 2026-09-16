using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Windows.LocalDataSecurity;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiCurrentUserProtector
{
    private const string EntropyPrefix = "xueqing-native/dpapi/current-user/v1/";

    public byte[] Protect(ReadOnlySpan<byte> secret, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var plaintext = secret.ToArray();
        var entropy = BuildEntropy(purpose);
        try
        {
            return ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var ciphertext = protectedData.ToArray();
        var entropy = BuildEntropy(purpose);
        try
        {
            return ProtectedData.Unprotect(ciphertext, entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private static byte[] BuildEntropy(string purpose)
        => SHA256.HashData(Encoding.UTF8.GetBytes(EntropyPrefix + purpose));
}
