using System.Runtime.InteropServices;
using System.Text;

namespace NetShareDoctor.Services;

/// <summary>Win32 calls for connecting shares and storing credentials without putting passwords on a command line.</summary>
internal static class Native
{
    public const int NO_ERROR = 0;
    public const int ERROR_EXTENDED_ERROR = 1208;
    public const int ERROR_NOT_FOUND = 1168;

    public const int RESOURCETYPE_DISK = 1;
    public const int CONNECT_UPDATE_PROFILE = 0x1;
    public const int CONNECT_TEMPORARY = 0x4;

    public const int CRED_TYPE_DOMAIN_PASSWORD = 2;
    public const int CRED_PERSIST_LOCAL_MACHINE = 2;

    public const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    public static extern int WNetAddConnection2W(ref NETRESOURCE netResource, string? password, string? userName, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    public static extern int WNetCancelConnection2W(string name, int flags, bool force);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetLastErrorW(out int error, StringBuilder errorBuf, int errorBufSize, StringBuilder nameBuf, int nameBufSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    /// <summary>The provider's own message for ERROR_EXTENDED_ERROR.</summary>
    public static string LastNetworkError()
    {
        var error = new StringBuilder(1024);
        var provider = new StringBuilder(256);
        return WNetGetLastErrorW(out var code, error, error.Capacity, provider, provider.Capacity) == NO_ERROR
            ? $"{provider}: {error.ToString().Trim()} (code {code})"
            : "";
    }

    /// <summary>Saves a Windows (domain password) credential for <paramref name="target"/>, like cmdkey /add.</summary>
    public static int WriteCredential(string target, string userName, string password)
    {
        var blob = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_DOMAIN_PASSWORD,
                TargetName = target,
                UserName = userName,
                CredentialBlob = blob,
                CredentialBlobSize = password.Length * 2,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            return CredWriteW(ref cred, 0) ? NO_ERROR : Marshal.GetLastWin32Error();
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    /// <summary>Deletes a saved Windows credential, like cmdkey /delete. Returns ERROR_NOT_FOUND if there was none.</summary>
    public static int DeleteCredential(string target) =>
        CredDeleteW(target, CRED_TYPE_DOMAIN_PASSWORD, 0) ? NO_ERROR : Marshal.GetLastWin32Error();
}
