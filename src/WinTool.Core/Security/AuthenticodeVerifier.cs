using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

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
                StateAction = 1,
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProviderFlags = 0,
                UiContext = 0
            };
            dataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(data, dataPointer, false);
            actionPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
            Marshal.StructureToPtr(ActionGenericVerifyV2, actionPointer, false);
            if (WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer) != 0) return false;
            data = Marshal.PtrToStructure<WinTrustData>(dataPointer);
            // Read the verified signer from native state instead of the obsolete signed-file loader.
            var provider = WTHelperProvDataFromStateData(data.StateData);
            if (provider == IntPtr.Zero) return false;
            var signerPointer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signerPointer == IntPtr.Zero) return false;
            var verified = Marshal.PtrToStructure<ProviderSigner>(signerPointer);
            if (verified.Error != 0 || verified.CertificateCount == 0 || verified.Certificates == IntPtr.Zero) return false;
            var context = Marshal.PtrToStructure<ProviderCertificate>(verified.Certificates).Certificate;
            if (context == IntPtr.Zero) return false;
            using var certificate = new X509Certificate2(context);
            signer = certificate.Subject;
            return certificate.Subject.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
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
