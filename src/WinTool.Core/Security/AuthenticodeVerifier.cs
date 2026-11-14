using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WinTool.Core.Diagnostics;

namespace WinTool.Core.Security;

/// <summary>
/// True only for a file with a valid Authenticode signature whose signer is
/// Microsoft Corporation. Used before running Windows Setup and the Office
/// Deployment Tool. Moved from the Windows Upgrade Tool's UpgradeEngine.cs.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsTrustedMicrosoftFile(string filePath, out string signer)
    {
        signer = "Unknown";

        var fileInfo = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = filePath,
            FileHandle = IntPtr.Zero,
            KnownSubject = IntPtr.Zero
        };
        var fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        var dataPointer = IntPtr.Zero;
        var actionPointer = IntPtr.Zero;
        var fileInfoWritten = false;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            fileInfoWritten = true;
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                PolicyCallbackData = IntPtr.Zero,
                SipClientData = IntPtr.Zero,
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfoPointer = fileInfoPointer,
                StateAction = 1, // Retain the verified signer's chain for the complementary root policy.
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProviderFlags = 0x1000 | 0x10, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE.
                UiContext = 0
            };
            dataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);
            actionPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ActionGenericVerifyV2, actionPointer, false);
            if (WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer) != 0) return false;
            data = Marshal.PtrToStructure<WinTrustData>(dataPointer);
            var provider = WTHelperProvDataFromStateData(data.StateData);
            if (provider == IntPtr.Zero) return false;
            var signerPointer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signerPointer == IntPtr.Zero) return false;
            var verified = Marshal.PtrToStructure<ProviderSigner>(signerPointer);
            if (verified.Error != 0 || verified.CertificateCount == 0 || verified.Certificates == IntPtr.Zero || verified.Chain == IntPtr.Zero) return false;
            var context = Marshal.PtrToStructure<ProviderCertificate>(verified.Certificates).Certificate;
            if (context == IntPtr.Zero) return false;
            using var certificate = new X509Certificate2(context);
            signer = certificate.Subject;
            if (!HasMicrosoftOrganization(certificate)) return false;
            // WinVerifyTrust built this exact signer's chain with cache-only retrieval. Do not
            // rebuild a different chain, enable test roots, or replace Authenticode validation.
            // Real sample: officedeploymenttool_20430-20146.exe (16.0.20430.20146)
            // passed flags=0 on 2026-11-10; the application-root fallback was not needed.
            // Windows 10/11 Setup variants and disconnected-VM evidence remain pending:
            // see docs/testing/MICROSOFT-FILES.md. Do not infer them from ODT or curl.
            return CheckMicrosoftRoot(flags => VerifyRoot(verified.Chain, flags));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException || ex is ArgumentException)
        {
            return false;
        }
        finally
        {
            if (dataPointer != IntPtr.Zero && actionPointer != IntPtr.Zero)
            {
                var data = Marshal.PtrToStructure<WinTrustData>(dataPointer);
                if (data.StateData != IntPtr.Zero)
                {
                    data.StateAction = 2;
                    Marshal.StructureToPtr(data, dataPointer, false);
                    WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer);
                }
            }
            if (actionPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(actionPointer);
            if (dataPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(dataPointer);
            if (fileInfoWritten) Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeCoTaskMem(fileInfoPointer);
        }
    }

    internal static bool HasMicrosoftOrganization(X509Certificate2 certificate)
    {
        var oid = Marshal.StringToHGlobalAnsi("2.5.4.10"); // Organization, not the display name or whole subject.
        try
        {
            var length = CertGetNameString(certificate.Handle, 3, 0, oid, null, 0);
            if (length <= 1) return false;
            var name = new StringBuilder((int)length);
            return CertGetNameString(certificate.Handle, 3, 0, oid, name, length) == length &&
                string.Equals(name.ToString(), "Microsoft Corporation", StringComparison.Ordinal);
        }
        finally { Marshal.FreeHGlobal(oid); }
    }

    internal readonly struct RootPolicyResult
    {
        internal RootPolicyResult(bool evaluated, uint error) { Evaluated = evaluated; Error = error; }
        internal bool Evaluated { get; }
        internal uint Error { get; }
        internal bool Succeeded => Evaluated && Error == 0;
    }

    internal static bool CheckMicrosoftRoot(Func<uint, RootPolicyResult> evaluate)
    {
        // The policies intentionally accept production roots only. Never use ENABLE_TEST_ROOT (0x10000).
        foreach (var flags in new uint[] { 0, 0x20000 })
        {
            var result = evaluate(flags);
            if (result.Succeeded)
            {
                AppLog.Current.Info("Signature", "Microsoft root policy accepted: flags=0x" + flags.ToString("X") + ".");
                return true;
            }
            AppLog.Current.Warn("Signature", "Microsoft root policy failed: flags=0x" + flags.ToString("X") +
                ", evaluated=" + result.Evaluated + ", dwError=0x" + result.Error.ToString("X8") + ".");
        }
        return false;
    }

    internal static RootPolicyResult VerifyRoot(IntPtr chain, uint flags)
    {
        var parameters = new ChainPolicyParameters { Size = (uint)Marshal.SizeOf<ChainPolicyParameters>(), Flags = flags };
        var status = new ChainPolicyStatus { Size = (uint)Marshal.SizeOf<ChainPolicyStatus>() };
        var evaluated = CertVerifyCertificateChainPolicy(new IntPtr(7), chain, ref parameters, ref status);
        return new RootPolicyResult(evaluated, status.Error);
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, EntryPoint = "CertGetNameStringW", ExactSpelling = true)]
    private static extern uint CertGetNameString(IntPtr certificate, uint type, uint flags, IntPtr typeParameter, StringBuilder name, uint length);

    [DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertVerifyCertificateChainPolicy(IntPtr policy, IntPtr chain, ref ChainPolicyParameters parameters, ref ChainPolicyStatus status);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderSigner
    {
        public uint Size;
        public System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;
        public uint CertificateCount;
        public IntPtr Certificates;
        public uint SignerType;
        public IntPtr SignerInfo;
        public uint Error;
        public uint CounterSignerCount;
        public IntPtr CounterSigners;
        public IntPtr Chain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate { public uint Size; public IntPtr Certificate; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChainPolicyParameters { public uint Size; public uint Flags; public IntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChainPolicyStatus { public uint Size; public uint Error; public int ChainIndex; public int ElementIndex; public IntPtr Extra; }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, IntPtr actionId, IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfoPointer;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }
}
