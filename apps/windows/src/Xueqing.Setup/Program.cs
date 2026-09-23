using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windows.Management.Deployment;

namespace Xueqing.Setup;

internal static class Program
{
    private const string PayloadResourceName = "Xueqing.Native.msix";
    private const string LaunchUri = "xueqing://today";

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
        var noLaunch = args.Contains("--no-launch", StringComparer.OrdinalIgnoreCase);

        try
        {
            var metadata = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? string.Empty, StringComparer.Ordinal);

            var expectedHash = RequiredMetadata(metadata, "XueqingPayloadSha256").ToLowerInvariant();
            var expectedPublisher = RequiredMetadata(metadata, "XueqingPublisher");
            var expectedPackageName = RequiredMetadata(metadata, "XueqingPackageName");

            if (expectedHash.Length != 64 || expectedHash.Any(ch => !Uri.IsHexDigit(ch)))
            {
                throw new InvalidDataException("安装程序内置的 MSIX 哈希无效。");
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "XueqingSetup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var packagePath = Path.Combine(tempDirectory, "Xueqing.Native.msix");

            try
            {
                await ExtractPayloadAsync(packagePath);
                VerifyHash(packagePath, expectedHash);
                VerifyAuthenticodeTrust(packagePath);
                VerifyPublisher(packagePath, expectedPublisher);

                var packageManager = new PackageManager();
                var deployment = await packageManager.AddPackageAsync(
                    new Uri(packagePath),
                    dependencyPackageUris: null,
                    DeploymentOptions.ForceApplicationShutdown);

                if (!deployment.IsRegistered)
                {
                    var detail = string.IsNullOrWhiteSpace(deployment.ErrorText)
                        ? deployment.ExtendedErrorCode?.Message ?? "未知部署错误"
                        : deployment.ErrorText;
                    throw new InvalidOperationException($"MSIX 安装未完成：{detail}");
                }

                var installed = packageManager.FindPackagesForUser(string.Empty)
                    .FirstOrDefault(package => string.Equals(package.Id.Name, expectedPackageName, StringComparison.Ordinal));
                if (installed is null)
                {
                    throw new InvalidOperationException("安装完成后未找到预期的 Xueqing.Native 包身份。");
                }

                if (!noLaunch)
                {
                    Process.Start(new ProcessStartInfo(LaunchUri) { UseShellExecute = true });
                }

                if (!quiet)
                {
                    MessageBox.Show(
                        "学情已安装或升级完成。",
                        "学情安装程序",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return 0;
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }
        catch (Exception exception)
        {
            if (!quiet)
            {
                MessageBox.Show(
                    exception.Message,
                    "学情安装失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            else
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static string RequiredMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"安装程序缺少发布元数据：{key}");
        }

        return value;
    }

    private static async Task ExtractPayloadAsync(string packagePath)
    {
        await using var source = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidDataException("安装程序不包含 Xueqing MSIX 负载。");
        await using var destination = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            useAsync: true);
        await source.CopyToAsync(destination);
        await destination.FlushAsync();
    }

    private static void VerifyHash(string packagePath, string expectedHash)
    {
        using var stream = File.OpenRead(packagePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expectedHash)))
        {
            throw new CryptographicException("内嵌 MSIX 的 SHA-256 与发布记录不一致。");
        }
    }

    private static void VerifyPublisher(string packagePath, string expectedPublisher)
    {
        using var signer = X509CertificateLoader.LoadCertificateFromFile(packagePath);
        if (!string.Equals(signer.Subject, expectedPublisher, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                $"MSIX 签名发布者不匹配。期望：{expectedPublisher}；实际：{signer.Subject}");
        }
    }

    private static void VerifyAuthenticodeTrust(string packagePath)
    {
        var filePathPointer = Marshal.StringToCoTaskMemUni(packagePath);
        var fileInfoPointer = IntPtr.Zero;
        var trustDataPointer = IntPtr.Zero;

        try
        {
            var fileInfo = new WinTrustFileInfo(filePathPointer);
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var trustData = new WinTrustData(fileInfoPointer, WinTrustStateAction.Verify);
            trustDataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, false);

            var status = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer);
            if (status != 0)
            {
                throw new CryptographicException($"MSIX Authenticode 信任验证失败：0x{status:X8}");
            }

            trustData = Marshal.PtrToStructure<WinTrustData>(trustDataPointer);
            trustData.StateAction = WinTrustStateAction.Close;
            Marshal.StructureToPtr(trustData, trustDataPointer, false);
            _ = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustDataPointer);
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(trustDataPointer);
            }
            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }
            Marshal.FreeCoTaskMem(filePathPointer);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Installation already completed; temp cleanup is best-effort only.
        }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(IntPtr filePath)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = filePath;
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }
    }

    private enum WinTrustDataUIChoice : uint
    {
        None = 2,
    }

    private enum WinTrustDataRevocationChecks : uint
    {
        None = 0,
    }

    private enum WinTrustDataChoice : uint
    {
        File = 1,
    }

    private enum WinTrustStateAction : uint
    {
        Ignore = 0,
        Verify = 1,
        Close = 2,
    }

    [Flags]
    private enum WinTrustProviderFlags : uint
    {
        Safer = 0x100,
    }

    private enum WinTrustDataUIContext : uint
    {
        Execute = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public WinTrustDataUIChoice UIChoice;
        public WinTrustDataRevocationChecks RevocationChecks;
        public WinTrustDataChoice UnionChoice;
        public IntPtr FileInfo;
        public WinTrustStateAction StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public WinTrustProviderFlags ProviderFlags;
        public WinTrustDataUIContext UIContext;
        public IntPtr SignatureSettings;

        public WinTrustData(IntPtr fileInfo, WinTrustStateAction stateAction)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UIChoice = WinTrustDataUIChoice.None;
            RevocationChecks = WinTrustDataRevocationChecks.None;
            UnionChoice = WinTrustDataChoice.File;
            FileInfo = fileInfo;
            StateAction = stateAction;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = WinTrustProviderFlags.Safer;
            UIContext = WinTrustDataUIContext.Execute;
            SignatureSettings = IntPtr.Zero;
        }
    }
}
