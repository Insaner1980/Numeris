using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Numeris.Services.Migration;

internal static class LegacyCredentialReader
{
    private const int CredTypeGeneric = 1;

    public static string? ReadKeyringPassword(string user, string credentialService)
    {
        var targetName = $"{user}.{credentialService}";
        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return DecodeCredentialBlob(bytes);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private static string? DecodeCredentialBlob(byte[] bytes)
    {
        var utf8 = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        if (!utf8.Contains('\0', StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(utf8) ? null : utf8;
        }

        if (bytes.Length % 2 != 0)
        {
            return null;
        }

        var utf16 = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(utf16) ? null : utf16;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string targetName, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
