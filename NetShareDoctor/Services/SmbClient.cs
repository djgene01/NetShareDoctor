using System.ComponentModel;
using System.Text;
using NetShareDoctor.Models;

namespace NetShareDoctor.Services;

/// <summary>
/// Connects, disconnects and forgets shares through the Windows networking API (the same one net use calls).
/// Passwords are passed in memory only, so they never appear on a command line or in a log.
/// </summary>
public static class SmbClient
{
    /// <summary>
    /// Connects a saved connection. With SavePassword the password is stored in Windows Credential Manager.
    /// An empty password means "use the saved or current sign-in".
    /// </summary>
    public static Task<(bool Ok, string Log)> ConnectAsync(SavedConnection c, string password) => Task.Run(() =>
    {
        var log = new StringBuilder();
        var unc = c.UncPath;

        if (c.SavePassword && password.Length > 0 && c.UserName.Length > 0)
        {
            var saved = Native.WriteCredential(c.Host, c.UserName, password);
            log.AppendLine($"> save sign-in for {c.Host} as {c.UserName} in Credential Manager");
            log.AppendLine(saved == Native.NO_ERROR ? "Saved." : Describe(saved));
        }

        var res = new Native.NETRESOURCE
        {
            dwType = Native.RESOURCETYPE_DISK,
            lpLocalName = c.HasDrive ? c.DriveLetter : null,
            lpRemoteName = unc,
        };
        var flags = c.Reconnect && c.HasDrive ? Native.CONNECT_UPDATE_PROFILE : Native.CONNECT_TEMPORARY;
        string? user = password.Length > 0 && c.UserName.Length > 0 ? c.UserName : null;
        string? pass = password.Length > 0 ? password : null;

        log.AppendLine($"> connect {(c.HasDrive ? c.DriveLetter + " to " : "")}{unc}" +
                       (user != null ? $" as {user}" : pass == null ? " with the saved or current sign-in" : "") +
                       (flags == Native.CONNECT_UPDATE_PROFILE ? " (reconnect at sign-in)" : ""));
        var code = Native.WNetAddConnection2W(ref res, pass, user, flags);
        log.AppendLine(Describe(code));

        if (code != Native.NO_ERROR)
        {
            var hint = NetInfo.Explain(code);
            if (hint.Length > 0) log.AppendLine().AppendLine("What this means: " + hint);
            else if (password.Length == 0)
                log.AppendLine().AppendLine("Tip: enter the password (it can be saved in Windows Credential Manager so you don't need it next time).");
        }
        return (code == Native.NO_ERROR, log.ToString().TrimEnd());
    });

    public static Task<(bool Ok, string Log)> DisconnectAsync(SavedConnection c) =>
        DisconnectAsync(c.HasDrive ? c.DriveLetter : c.UncPath);

    /// <summary>Drops a drive letter or UNC connection, and forgets it for the next sign-in.</summary>
    public static Task<(bool Ok, string Log)> DisconnectAsync(string target) => Task.Run(() =>
    {
        var code = Native.WNetCancelConnection2W(target, Native.CONNECT_UPDATE_PROFILE, true);
        var text = $"> disconnect {target}\n{Describe(code)}";
        var hint = code == Native.NO_ERROR ? "" : NetInfo.Explain(code);
        return (code == Native.NO_ERROR, hint.Length > 0 ? text + "\n" + hint : text);
    });

    /// <summary>Deletes the saved Credential Manager sign-in for a host. Returns a log line.</summary>
    public static string ForgetPassword(string host)
    {
        var code = Native.DeleteCredential(host);
        return code switch
        {
            Native.NO_ERROR => $"Deleted the saved password for {host}.",
            Native.ERROR_NOT_FOUND => $"No saved password for {host}.",
            _ => $"Couldn't delete the saved password for {host}: {Describe(code)}",
        };
    }

    /// <summary>Drops every connection to a host and deletes its saved credential, so you can sign in fresh.</summary>
    public static async Task<string> ClearLoginsAsync(string host)
    {
        var log = new StringBuilder();
        var list = await Shell.RunAsync("net.exe", ["use"]);
        var prefix = $@"\\{host}\";
        var targets = new List<string>();
        foreach (var line in list.Output.Split('\n'))
        {
            var unc = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.StartsWith(@"\\"));
            if (unc == null) continue;
            if (!unc.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !unc.Equals($@"\\{host}", StringComparison.OrdinalIgnoreCase)) continue;
            var drive = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.Length == 2 && t[1] == ':');
            targets.Add(drive ?? unc);
        }
        if (targets.Count == 0) log.AppendLine($"No open connections to {host}.");
        foreach (var t in targets)
            log.AppendLine((await DisconnectAsync(t)).Log);

        log.AppendLine(ForgetPassword(host));
        log.AppendLine("Saved sign-ins cleared. Windows may keep an open session for a minute; if the next connect still uses the old account, sign out of Windows or restart.");
        return log.ToString().TrimEnd();
    }

    /// <summary>"The command completed successfully." or "Error 86: The specified network password is not correct."</summary>
    private static string Describe(int code)
    {
        if (code == Native.NO_ERROR) return "The command completed successfully.";
        if (code == Native.ERROR_EXTENDED_ERROR)
        {
            var ext = Native.LastNetworkError();
            if (ext.Length > 0) return $"Error {code}: {ext}";
        }
        return $"Error {code}: {new Win32Exception(code).Message}";
    }

    public static IEnumerable<string> FreeDriveLetters(string? keep)
    {
        var used = System.IO.DriveInfo.GetDrives().Select(d => d.Name[..2].ToUpperInvariant()).ToHashSet();
        yield return "None";
        for (var ch = 'Z'; ch >= 'D'; ch--)
        {
            var l = ch + ":";
            if (!used.Contains(l) || string.Equals(l, keep, StringComparison.OrdinalIgnoreCase)) yield return l;
        }
    }
}
