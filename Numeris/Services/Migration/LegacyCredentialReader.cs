using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Numeris.Services.Migration;

internal static class LegacyCredentialReader
{
    private const int CredTypeGeneric = 1;

    public static string? ReadKeyringPassword(string user, string credentialService)
    {
        foreach (var targetName in OrderCredentialTargets(user, credentialService, EnumerateCredentialTargetNames()))
        {
            var password = ReadCredentialTarget(targetName);
            if (!string.IsNullOrWhiteSpace(password))
            {
                return password;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> OrderCredentialTargets(string user, string credentialService, IEnumerable<string> targetNames)
    {
        var currentTargetName = $"{user}.{credentialService}";
        var legacyPrefix = user + ".";
        var ordered = new List<string> { currentTargetName };

        foreach (var targetName in targetNames)
        {
            if (targetName.StartsWith(legacyPrefix, StringComparison.Ordinal)
                && !string.Equals(targetName, currentTargetName, StringComparison.Ordinal))
            {
                ordered.Add(targetName);
            }
        }

        return ordered;
    }

    private static string? ReadCredentialTarget(string targetName)
    {
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

    private static IReadOnlyList<string> EnumerateCredentialTargetNames()
    {
        if (!CredEnumerate(null, 0, out var count, out var credentialsPtr) || credentialsPtr == IntPtr.Zero)
        {
            return Array.Empty<string>();
        }

        try
        {
            var targetNames = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var credentialPtr = Marshal.ReadIntPtr(credentialsPtr, index * IntPtr.Size);
                if (credentialPtr == IntPtr.Zero)
                {
                    continue;
                }

                var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
                if (credential.Type != CredTypeGeneric || credential.TargetName == IntPtr.Zero)
                {
                    continue;
                }

                var targetName = Marshal.PtrToStringUni(credential.TargetName);
                if (!string.IsNullOrWhiteSpace(targetName))
                {
                    targetNames.Add(targetName);
                }
            }

            return targetNames;
        }
        finally
        {
            CredFree(credentialsPtr);
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

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);

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
