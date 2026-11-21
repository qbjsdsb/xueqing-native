using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Linux.NativeSpike;

internal sealed class LinuxKeyringUnavailableException : InvalidOperationException
{
    public LinuxKeyringUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Linux Secret Service (libsecret / org.freedesktop.secrets) master-key store
/// mirroring WindowsDpapiDatabaseKeyStore's fail-closed contract:
/// - 256-bit random master key stored only in the desktop keyring (never plaintext on disk)
/// - existing database with missing keyring entry fails closed (refuses replacement key)
/// - missing/locked Secret Service fails closed (never falls back to plaintext SQLite)
/// - native secret buffers and managed key bytes are explicitly wiped after use
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxSecretServiceKeyStore
{
    private const int MasterKeyLengthBytes = 32;
    private const string SchemaName = "com.xueqing.app.local-database-key.v1";
    private const string PurposeValue = "local-database-key";
    private const string CollectionDefault = "default";

    private readonly string _databasePath;
    private readonly string _scopeHash;

    public LinuxSecretServiceKeyStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
        _scopeHash = ComputeScopeHash(_databasePath);
    }

    public string ScopeHash => _scopeHash;

    public byte[] LoadOrCreate()
    {
        var existing = TryLookupExisting();
        if (existing is not null)
        {
            return existing;
        }

        if (File.Exists(_databasePath))
        {
            throw new LinuxKeyringUnavailableException(
                "Encrypted local database exists on disk, but its Linux Secret Service key is missing. Refusing to create a replacement key.");
        }

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var masterKey = RandomNumberGenerator.GetBytes(MasterKeyLengthBytes);
        try
        {
            StoreKey(masterKey);
            return masterKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
    }

    public byte[]? TryLookupExisting()
    {
        EnsureSessionBusConfigured();

        IntPtr schema = IntPtr.Zero;
        IntPtr attrs = IntPtr.Zero;
        IntPtr secretPtr = IntPtr.Zero;
        IntPtr errorPtr = IntPtr.Zero;

        try
        {
            schema = CreateSchema();
            attrs = CreateAttributes(_scopeHash);
            secretPtr = SecretPasswordLookupvSync(schema, attrs, IntPtr.Zero, out errorPtr);
            ThrowIfGError(errorPtr, "Failed to query Linux Secret Service for local database key.");

            if (secretPtr == IntPtr.Zero)
            {
                return null;
            }

            return DecodeMasterKey(secretPtr);
        }
        finally
        {
            if (secretPtr != IntPtr.Zero)
            {
                SecretPasswordFree(secretPtr);
            }

            if (attrs != IntPtr.Zero)
            {
                GHashTableUnref(attrs);
            }

            if (schema != IntPtr.Zero)
            {
                SecretSchemaUnref(schema);
            }
        }
    }

    public bool Purge()
    {
        EnsureSessionBusConfigured();

        IntPtr schema = IntPtr.Zero;
        IntPtr attrs = IntPtr.Zero;
        IntPtr errorPtr = IntPtr.Zero;

        try
        {
            schema = CreateSchema();
            attrs = CreateAttributes(_scopeHash);
            var cleared = SecretPasswordClearvSync(schema, attrs, IntPtr.Zero, out errorPtr);
            ThrowIfGError(errorPtr, "Failed to clear Linux Secret Service key.");
            return cleared;
        }
        finally
        {
            if (attrs != IntPtr.Zero)
            {
                GHashTableUnref(attrs);
            }

            if (schema != IntPtr.Zero)
            {
                SecretSchemaUnref(schema);
            }
        }
    }

    private void StoreKey(byte[] masterKey)
    {
        EnsureSessionBusConfigured();

        var encoded = Convert.ToBase64String(masterKey);
        var encodedUtf8 = Encoding.UTF8.GetBytes(encoded + "\0");
        IntPtr schema = IntPtr.Zero;
        IntPtr attrs = IntPtr.Zero;
        IntPtr errorPtr = IntPtr.Zero;
        var pinned = GCHandle.Alloc(encodedUtf8, GCHandleType.Pinned);

        try
        {
            schema = CreateSchema();
            attrs = CreateAttributes(_scopeHash);
            var label = $"Xueqing Local Database Key ({_scopeHash[..12]})";
            var stored = SecretPasswordStorevSync(
                schema,
                attrs,
                CollectionDefault,
                label,
                pinned.AddrOfPinnedObject(),
                IntPtr.Zero,
                out errorPtr);
            ThrowIfGError(errorPtr, "Failed to persist local database key in Linux Secret Service.");

            if (!stored)
            {
                throw new LinuxKeyringUnavailableException(
                    "Linux Secret Service rejected storing the local database master key.");
            }
        }
        finally
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }

            CryptographicOperations.ZeroMemory(encodedUtf8);

            if (attrs != IntPtr.Zero)
            {
                GHashTableUnref(attrs);
            }

            if (schema != IntPtr.Zero)
            {
                SecretSchemaUnref(schema);
            }
        }
    }

    private static byte[] DecodeMasterKey(IntPtr secretUtf8Ptr)
    {
        var secretText = Marshal.PtrToStringUTF8(secretUtf8Ptr);
        if (string.IsNullOrWhiteSpace(secretText))
        {
            throw new LinuxKeyringUnavailableException(
                "Linux Secret Service returned an empty local database key.");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(secretText);
        }
        catch (FormatException)
        {
            throw new LinuxKeyringUnavailableException(
                "Linux Secret Service returned a corrupted local database key payload.");
        }

        if (decoded.Length != MasterKeyLengthBytes)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new LinuxKeyringUnavailableException(
                $"Linux Secret Service returned a key with invalid length ({decoded.Length} bytes).");
        }

        return decoded;
    }

    private static void EnsureSessionBusConfigured()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        {
            throw new LinuxKeyringUnavailableException(
                "DBUS_SESSION_BUS_ADDRESS is not set; Linux Secret Service is unavailable.");
        }
    }

    private static IntPtr CreateSchema()
    {
        var schemaAttrs = CreatePointerHashTable();
        try
        {
            // SecretSchemaAttributeType.STRING == 0
            GHashTableInsert(schemaAttrs, GStrdup("purpose"), IntPtr.Zero);
            GHashTableInsert(schemaAttrs, GStrdup("scope"), IntPtr.Zero);

            // SecretSchemaFlags.NONE == 0
            var schema = SecretSchemaNewv(SchemaName, 0, schemaAttrs);
            if (schema == IntPtr.Zero)
            {
                throw new LinuxKeyringUnavailableException(
                    "Failed to initialize libsecret schema.");
            }

            return schema;
        }
        finally
        {
            GHashTableUnref(schemaAttrs);
        }
    }

    private static IntPtr CreateAttributes(string scopeHash)
    {
        var attrs = CreateStringHashTable();
        GHashTableInsert(attrs, GStrdup("purpose"), GStrdup(PurposeValue));
        GHashTableInsert(attrs, GStrdup("scope"), GStrdup(scopeHash));
        return attrs;
    }

    private static IntPtr CreateStringHashTable()
    {
        var glib = NativeLibrary.Load("libglib-2.0.so.0");
        var strHash = NativeLibrary.GetExport(glib, "g_str_hash");
        var strEqual = NativeLibrary.GetExport(glib, "g_str_equal");
        var gFree = NativeLibrary.GetExport(glib, "g_free");
        return GHashTableNewFull(strHash, strEqual, gFree, gFree);
    }

    private static IntPtr CreatePointerHashTable()
    {
        var glib = NativeLibrary.Load("libglib-2.0.so.0");
        var strHash = NativeLibrary.GetExport(glib, "g_str_hash");
        var strEqual = NativeLibrary.GetExport(glib, "g_str_equal");
        var gFree = NativeLibrary.GetExport(glib, "g_free");
        return GHashTableNewFull(strHash, strEqual, gFree, IntPtr.Zero);
    }

    private static void ThrowIfGError(IntPtr errorPtr, string fallbackMessage)
    {
        if (errorPtr == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var err = Marshal.PtrToStructure<GErrorStruct>(errorPtr);
            var detail = err.Message != IntPtr.Zero
                ? Marshal.PtrToStringUTF8(err.Message)
                : null;
            throw new LinuxKeyringUnavailableException(
                string.IsNullOrWhiteSpace(detail)
                    ? fallbackMessage
                    : $"{fallbackMessage} ({detail})");
        }
        finally
        {
            GErrorFree(errorPtr);
        }
    }

    private static string ComputeScopeHash(string fullPath)
    {
        var bytes = Encoding.UTF8.GetBytes("xueqing-native/linux/local-database-key/v1/" + fullPath);
        try
        {
            var hash = SHA256.HashData(bytes);
            try
            {
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GErrorStruct
    {
        public uint Domain;
        public int Code;
        public IntPtr Message;
    }

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_hash_table_new_full")]
    private static partial IntPtr GHashTableNewFull(
        IntPtr hashFunc,
        IntPtr keyEqualFunc,
        IntPtr keyDestroyFunc,
        IntPtr valueDestroyFunc);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_hash_table_insert")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool GHashTableInsert(IntPtr hashTable, IntPtr key, IntPtr value);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_hash_table_unref")]
    private static partial void GHashTableUnref(IntPtr hashTable);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_strdup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GStrdup(string value);

    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
    private static partial void GErrorFree(IntPtr error);

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_schema_newv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr SecretSchemaNewv(string name, int flags, IntPtr attributeNamesAndTypes);

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_schema_unref")]
    private static partial void SecretSchemaUnref(IntPtr schema);

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_storev_sync", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SecretPasswordStorevSync(
        IntPtr schema,
        IntPtr attributes,
        string collection,
        string label,
        IntPtr passwordUtf8,
        IntPtr cancellable,
        out IntPtr error);

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_lookupv_sync")]
    private static partial IntPtr SecretPasswordLookupvSync(
        IntPtr schema,
        IntPtr attributes,
        IntPtr cancellable,
        out IntPtr error);

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_clearv_sync")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SecretPasswordClearvSync(
        IntPtr schema,
        IntPtr attributes,
        IntPtr cancellable,
        out IntPtr error);

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_password_free")]
    private static partial void SecretPasswordFree(IntPtr password);
}
