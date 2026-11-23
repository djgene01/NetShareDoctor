using System.Text;
using NetShareDoctor.Models;

namespace NetShareDoctor.Services;

/// <summary>Connects, disconnects and forgets shares with net.exe and cmdkey.exe.</summary>
public static class SmbClient
{
    /// <summary>
    /// Connects a saved connection. The password goes straight to the tools as an argument and is
    /// never logged or written to disk; with SavePassword it is stored in Windows Credential Manager.
    /// </summary>
    public static async Task<(bool Ok, string Log)> ConnectAsync(SavedConnection c, string password)
    {
        var log = new StringBuilder();
        var unc = c.UncPath;

        if (c.SavePassword && password.Length > 0 && c.UserName.Length > 0)
        {
            var key = await Shell.RunAsync("cmdkey.exe", [$"/add:{c.Host}", $"/user:{c.UserName}", $"/pass:{password}"]);
            log.AppendLine($"> cmdkey /add:{c.Host} /user:{c.UserName} /pass:********");
            log.AppendLine(key.Text);
        }

        var args = new List<string> { "use" };
        if (c.HasDrive) args.Add(c.DriveLetter);
        args.Add(unc);
        if (password.Length > 0)
        {
            args.Add(password);
            if (c.UserName.Length > 0) args.Add($"/user:{c.UserName}");
        }
        args.Add(c.Reconnect ? "/persistent:yes" : "/persistent:no");

        log.AppendLine("> net " + string.Join(' ', args.Select(a => a == password && password.Length > 0 ? "********" : a)));
        var r = await Shell.RunAsync("net.exe", args, TimeSpan.FromSeconds(45));
        log.AppendLine(r.Text);

        if (!r.Success)
        {
            var hint = NetInfo.Explain(r.Text);
            if (hint.Length > 0) log.AppendLine().AppendLine("What this means: " + hint);
            else if (password.Length == 0)
                log.AppendLine().AppendLine("Tip: enter the password (it can be saved in Windows Credential Manager so you don't need it next time).");
        }
        return (r.Success, log.ToString().TrimEnd());
    }

    public static async Task<(bool Ok, string Log)> DisconnectAsync(SavedConnection c)
    {
        var target = c.HasDrive ? c.DriveLetter : c.UncPath;
        var r = await Shell.RunAsync("net.exe", ["use", target, "/delete", "/y"]);
        return (r.Success, $"> net use {target} /delete /y\n{r.Text}");
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
        {
            var r = await Shell.RunAsync("net.exe", ["use", t, "/delete", "/y"]);
            log.AppendLine($"> net use {t} /delete /y").AppendLine(r.Text);
        }

        foreach (var key in new[] { host, $"Domain:target={host}" })
        {
            var r = await Shell.RunAsync("cmdkey.exe", [$"/delete:{key}"]);
            if (r.Success) log.AppendLine($"> cmdkey /delete:{key}").AppendLine(r.Text);
        }
        log.AppendLine("Saved sign-ins cleared. Windows may keep an open session for a minute; if the next connect still uses the old account, sign out of Windows or restart.");
        return log.ToString().TrimEnd();
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
