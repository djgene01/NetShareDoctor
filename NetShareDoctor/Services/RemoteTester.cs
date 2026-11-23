using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetShareDoctor.Models;

namespace NetShareDoctor.Services;

/// <summary>Tests whether another PC's shares can be reached from this PC.</summary>
public static partial class RemoteTester
{
    /// <summary>Accepts "pc", "\\pc", "\\pc\share" or "smb://pc/share" and returns (host, share).</summary>
    public static (string Host, string Share) Split(string input)
    {
        var t = (input ?? "").Trim().Replace('/', '\\');
        if (t.StartsWith("smb:", StringComparison.OrdinalIgnoreCase)) t = t[4..];
        t = t.Trim('\\');
        var i = t.IndexOf('\\');
        return i < 0 ? (t, "") : (t[..i], t[(i + 1)..].Trim('\\'));
    }

    public static async Task<List<DiagnosticCheck>> TestAsync(string host)
    {
        const string cat = "Connection test";
        var list = new List<DiagnosticCheck>();
        IPAddress? addr;

        if (IPAddress.TryParse(host, out var parsed))
        {
            addr = parsed;
        }
        else
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var all = await Dns.GetHostAddressesAsync(host, cts.Token);
                addr = all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? all.FirstOrDefault();
            }
            catch { addr = null; }

            if (addr == null)
            {
                list.Add(new() { Category = cat, Status = CheckStatus.Problem, Title = $"Can't find a PC named \"{host}\"",
                    Detail = "Check the spelling, or use its IP address instead (run ipconfig on that PC). PC names often don't resolve across some routers or after a VPN connects." });
                return list;
            }
            list.Add(new() { Category = cat, Status = CheckStatus.Ok, Title = $"\"{host}\" was found at {addr}" });
        }

        var local = NetInfo.LocalSubnets();
        if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr)
            && local.Count > 0 && !local.Any(s => Diagnostics.SameNetwork(s.Address, addr, s.Prefix)))
        {
            list.Add(new() { Category = cat, Status = CheckStatus.Warning, Title = $"{addr} is on a different network than this PC",
                Detail = "This PC is on " + string.Join(", ", local.Select(s => $"{s.Address}/{s.Prefix}")) +
                         ". Make sure both PCs use the same Wi-Fi (not a guest network) and that no VPN is connected." });
        }

        var pingOk = false;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(addr, 1500);
            pingOk = reply.Status == IPStatus.Success;
            list.Add(pingOk
                ? new() { Category = cat, Status = CheckStatus.Ok, Title = $"{addr} answers ping ({reply.RoundtripTime} ms)" }
                : new() { Category = cat, Status = CheckStatus.Info, Title = $"{addr} doesn't answer ping",
                    Detail = "Windows blocks ping by default, so this alone isn't a problem if port 445 below is open." });
        }
        catch (Exception ex)
        {
            list.Add(new() { Category = cat, Status = CheckStatus.Info, Title = "Ping failed", Detail = ex.Message });
        }

        var smb = await PortOpenAsync(addr, 445, 3000);
        list.Add(smb
            ? new() { Category = cat, Status = CheckStatus.Ok, Title = "File sharing port 445 is open",
                Detail = "The network path works. If connecting still fails, it's the username, password or share name." }
            : new() { Category = cat, Status = CheckStatus.Problem, Title = "File sharing port 445 is closed or blocked",
                Detail = (pingOk ? "The PC is reachable, but its file sharing isn't. " : "") +
                         "On that PC: run the Diagnose tab, check its firewall and that a folder is shared. Also check the router doesn't isolate Wi-Fi devices (\"AP isolation\" / \"client isolation\")." });

        var nb = await PortOpenAsync(addr, 139, 1500);
        list.Add(new() { Category = cat, Status = CheckStatus.Info, Title = nb ? "Legacy NetBIOS port 139 is open" : "Legacy NetBIOS port 139 is closed",
            Detail = "Only matters for very old devices." });

        return list;
    }

    public static async Task<bool> PortOpenAsync(IPAddress addr, int port, int timeoutMs)
    {
        using var client = new TcpClient(addr.AddressFamily);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await client.ConnectAsync(addr, port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Lists a PC's shares with "net view" (uses the current or saved sign-in).</summary>
    public static async Task<(List<string> Shares, ShellResult Raw)> ListSharesAsync(string host)
    {
        var r = await Shell.RunAsync("net.exe", ["view", $@"\\{host}", "/all"], TimeSpan.FromSeconds(30));
        var shares = new List<string>();
        if (!r.Success) return (shares, r);

        var lines = r.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(l => l.StartsWith("-----"));
        if (start < 0) return (shares, r);
        foreach (var line in lines.Skip(start + 1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var m = ShareLine().Match(line);
            if (m.Success) shares.Add(m.Groups[1].Value.Trim());
        }
        return (shares, r);
    }

    [GeneratedRegex(@"^(.+?)\s{2,}\S")]
    private static partial Regex ShareLine();
}
