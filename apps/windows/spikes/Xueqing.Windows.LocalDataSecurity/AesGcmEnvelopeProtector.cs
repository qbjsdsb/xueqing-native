using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Windows.LocalDataSecurity;

public sealed class AesGcmEnvelopeProtector
{
    private const byte EnvelopeVersion = 1;
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int HeaderSizeBytes = 1 + NonceSizeBytes + TagSizeBytes;

    public byte[] Protect(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        string purpose,
        string recordId)
    {
        ValidateKeyAndContext(key, purpose, recordId);

        var envelope = new byte[HeaderSizeBytes + plaintext.Length];
        envelope[0] = EnvelopeVersion;
        var nonce = envelope.AsSpan(1, NonceSizeBytes);
        var tag = envelope.AsSpan(1 + NonceSizeBytes, TagSizeBytes);
        var ciphertext = envelope.AsSpan(HeaderSizeBytes);
        RandomNumberGenerator.Fill(nonce);

        var aad = BuildAssociatedData(purpose, recordId);
        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public byte[] Unprotect(
        ReadOnlySpan<byte> envelope,
        ReadOnlySpan<byte> key,
        string purpose,
        string recordId)
    {
        ValidateKeyAndContext(key, purpose, recordId);

        if (envelope.Length < HeaderSizeBytes || envelope[0] != EnvelopeVersion)
        {
            throw new CryptographicException("Unsupported or malformed Xueqing encrypted envelope.");
        }

        var nonce = envelope.Slice(1, NonceSizeBytes);
        var tag = envelope.Slice(1 + NonceSizeBytes, TagSizeBytes);
        var ciphertext = envelope[HeaderSizeBytes..];
        var plaintext = new byte[ciphertext.Length];
        var aad = BuildAssociatedData(purpose, recordId);

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    private static byte[] BuildAssociatedData(string purpose, string recordId)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var recordBytes = Encoding.UTF8.GetBytes(recordId);
        var prefixBytes = "xueqing-native/local-data/v1"u8;
        var result = new byte[
            sizeof(int) + prefixBytes.Length
            + sizeof(int) + purposeBytes.Length
            + sizeof(int) + recordBytes.Length];

        var offset = 0;
        WritePart(prefixBytes, result, ref offset);
        WritePart(purposeBytes, result, ref offset);
        WritePart(recordBytes, result, ref offset);
        CryptographicOperations.ZeroMemory(purposeBytes);
        CryptographicOperations.ZeroMemory(recordBytes);
        return result;
    }

    private static void WritePart(ReadOnlySpan<byte> value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, sizeof(int)), value.Length);
        offset += sizeof(int);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static void ValidateKeyAndContext(ReadOnlySpan<byte> key, string purpose, string recordId)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException("Xueqing local-data AES-GCM keys must be exactly 256 bits.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
    }
}
