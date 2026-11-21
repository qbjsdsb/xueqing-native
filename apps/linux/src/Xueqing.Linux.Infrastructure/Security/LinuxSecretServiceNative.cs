using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Linux.Infrastructure.Security;

[SupportedOSPlatform("linux")]
internal static partial class LinuxSecretServiceNative
{
    private const string CollectionDefault = "default";

    public static void EnsureSessionBusConfigured(Func<string, Exception> exceptionFactory)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        {
            throw exceptionFactory(
                "DBUS_SESSION_BUS_ADDRESS is not set; Linux Secret Service is unavailable.");
        }
    }

    public static string? LookupUtf8Secret(
        string schemaName,
        string purpose,
        string scopeHash,
        Func<string, Exception> exceptionFactory)
    {
        EnsureSessionBusConfigured(exceptionFactory);

        IntPtr schema = IntPtr.Zero;
        IntPtr attrs = IntPtr.Zero;
        IntPtr secretPtr = IntPtr.Zero;
        IntPtr errorPtr = IntPtr.Zero;

        try
        {
            schema = CreateSchema(schemaName, exceptionFactory);
            attrs = CreateAttributes(purpose, scopeHash);
            secretPtr = SecretPasswordLookupvSync(schema, attrs, IntPtr.Zero, out errorPtr);
            ThrowIfGError(errorPtr, "Failed to query Linux Secret Service.", exceptionFactory);

            return secretPtr == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(secretPtr);
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

    public static void StoreUtf8Secret(
        string schemaName,
        string purpose,
        string scopeHash,
        string label,
        string secretText,
        Func<string, Exception> exceptionFactory)
    {
        EnsureSessionBusConfigured(exceptionFactory);

        var utf8 = Encoding.UTF8.GetBytes(secretText + "\0");
        IntPtr schema = IntPtr.Zero;
        IntPtr attrs = IntPtr.Zero;
        IntPtr errorPtr = IntPtr.Zero;
        var pinned = GCHandle.Alloc(utf8, GCHandleType.Pinned);

        try
        {
            schema = CreateSchema(schemaName, exceptionFactory);
            attrs = CreateAttributes(purpose, scopeHash);
            var stored = SecretPasswordStorevSync(
                schema,
                attrs,
                CollectionDefault,
                label,
                pinned.AddrOfPinnedObject(),
                IntPtr.Zero,
                out errorPtr);
            ThrowIfGError(errorPtr, "Failed to store secret in Linux Secret Service.", exceptionFactory);

            if (!stored)
            {
                throw exceptionFactory("Linux Secret Service rejected storing the secret.");
            }
        }
        finally
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }

            CryptographicOperations.ZeroMemory(utf8);

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

    public static bool ClearSecret(
        string schemaName,
        string purpose,
        string scopeHash,
        Func<string, Exception> exceptionFactory)
    {
        EnsureSessionBusConfigured(exceptionFactory);

        IntPtr schema = IntPtr.Zero;
        IntPtr attrs = IntPtr.Zero;
        IntPtr errorPtr = IntPtr.Zero;

        try
        {
            schema = CreateSchema(schemaName, exceptionFactory);
            attrs = CreateAttributes(purpose, scopeHash);
            var cleared = SecretPasswordClearvSync(schema, attrs, IntPtr.Zero, out errorPtr);
            ThrowIfGError(errorPtr, "Failed to clear secret from Linux Secret Service.", exceptionFactory);
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

    public static string ComputeScopeHash(string material)
    {
        var bytes = Encoding.UTF8.GetBytes(material);
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

    private static IntPtr CreateSchema(string schemaName, Func<string, Exception> exceptionFactory)
    {
        var schemaAttrs = CreatePointerHashTable();
        try
        {
            GHashTableInsert(schemaAttrs, GStrdup("purpose"), IntPtr.Zero);
            GHashTableInsert(schemaAttrs, GStrdup("scope"), IntPtr.Zero);
            var schema = SecretSchemaNewv(schemaName, 0, schemaAttrs);
            if (schema == IntPtr.Zero)
            {
                throw exceptionFactory("Failed to initialize libsecret schema.");
            }

            return schema;
        }
        finally
        {
            GHashTableUnref(schemaAttrs);
        }
    }

    private static IntPtr CreateAttributes(string purpose, string scopeHash)
    {
        var attrs = CreateStringHashTable();
        GHashTableInsert(attrs, GStrdup("purpose"), GStrdup(purpose));
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

    private static void ThrowIfGError(
        IntPtr errorPtr,
        string fallbackMessage,
        Func<string, Exception> exceptionFactory)
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
            throw exceptionFactory(
                string.IsNullOrWhiteSpace(detail)
                    ? fallbackMessage
                    : $"{fallbackMessage} ({detail})");
        }
        finally
        {
            GErrorFree(errorPtr);
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
