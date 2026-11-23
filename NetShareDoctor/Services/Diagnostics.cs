using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NetShareDoctor.Models;
using static NetShareDoctor.Services.Json;

namespace NetShareDoctor.Services;

public sealed class DiagnosticsReport
{
    public List<DiagnosticCheck> Checks { get; } = [];
    public string Computer { get; set; } = Environment.MachineName;
    public string? LanAddress { get; set; }
    public List<string> ShareNames { get; } = [];
}

/// <summary>Checks everything that decides whether other PCs can see and open this PC's shares.</summary>
public static class Diagnostics
{
    private const string FactsScript = """
        $ErrorActionPreference='SilentlyContinue'
        $r=[ordered]@{}
        $r.Computer=$env:COMPUTERNAME
        $r.User=$env:USERNAME
        $r.PrincipalSource="$((Get-LocalUser -Name $env:USERNAME).PrincipalSource)"
        $r.Profiles=@(Get-NetConnectionProfile | ForEach-Object { [pscustomobject]@{Name=$_.Name;Alias=$_.InterfaceAlias;Index=$_.InterfaceIndex;Category="$($_.NetworkCategory)"} })
        $r.IPs=@(Get-NetIPConfiguration | Where-Object { $_.IPv4Address -and $_.NetAdapter.Status -eq 'Up' } | ForEach-Object {
            $gw = $_.IPv4DefaultGateway.NextHop | Select-Object -First 1
            [pscustomobject]@{Alias=$_.InterfaceAlias;Desc=$_.InterfaceDescription;IP=($_.IPv4Address.IPAddress | Select-Object -First 1);Gateway="$gw";GatewayOk=$(if($gw){[bool](Test-Connection -ComputerName $gw -Count 1 -Quiet)}else{$false})} })
        $r.Vpn=@(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and ($_.InterfaceDescription -match 'VPN|TAP-|WireGuard|OpenVPN|Fortinet|AnyConnect|ZeroTier|Tailscale|Wintun|PANGP|Juniper|SonicWall' -or $_.Name -match 'VPN') } | ForEach-Object { "$($_.Name) ($($_.InterfaceDescription))" })
        $r.Firewall=@(Get-NetFirewallProfile | ForEach-Object { [pscustomobject]@{Name="$($_.Name)";Enabled=("$($_.Enabled)" -eq 'True')} })
        $fw=@(Get-NetFirewallRule -Direction Inbound -Enabled True)
        $r.Allow=@($fw | Where-Object { "$($_.Action)" -eq 'Allow' -and ($_.Name -like 'FPS-SMB-In-TCP*' -or $_.Name -like 'NETDIS-*') } | ForEach-Object { [pscustomobject]@{Name=$_.Name;Profile="$($_.Profile)"} })
        $r.Block=@($fw | Where-Object { "$($_.Action)" -eq 'Block' } | ForEach-Object {
            $a = $_ | Get-NetFirewallAddressFilter; $p = $_ | Get-NetFirewallPortFilter
            [pscustomobject]@{Name=$_.Name;Display=$_.DisplayName;Desc=$_.Description;Profile="$($_.Profile)";Remote=(@($a.RemoteAddress) -join ',');Protocol="$($p.Protocol)";Port=(@($p.LocalPort) -join ',')} })
        $r.Services=@(Get-Service LanmanServer,LanmanWorkstation,FDResPub,fdPHost | ForEach-Object { [pscustomobject]@{Name=$_.Name;Status="$($_.Status)";Start="$($_.StartType)"} })
        $r.Listen445=[bool](Get-NetTCPConnection -LocalPort 445 -State Listen)
        $s=Get-SmbServerConfiguration
        $r.Server=[pscustomobject]@{Smb1=[bool]$s.EnableSMB1Protocol;Smb2=[bool]$s.EnableSMB2Protocol;Signing=[bool]$s.RequireSecuritySignature}
        $c=Get-SmbClientConfiguration
        $r.Client=[pscustomobject]@{Signing=[bool]$c.RequireSecuritySignature;Guest=[bool]$c.EnableInsecureGuestLogons}
        $r.Shares=@(Get-SmbShare | Where-Object { -not $_.Special } | ForEach-Object { [pscustomobject]@{Name=$_.Name;Path=$_.Path} })
        $r | ConvertTo-Json -Depth 5 -Compress
        """;

    private const string FixFileSharingRules =
        "Get-NetFirewallRule -Name 'FPS-SMB-In-TCP*','FPS-NB_Session-In-TCP*' | Where-Object { \"$($_.Profile)\" -match 'Private|Domain|Any' } | Enable-NetFirewallRule";

    private const string FixDiscoveryRules =
        "Get-NetFirewallRule -Name 'NETDIS-*' | Where-Object { \"$($_.Profile)\" -match 'Private|Domain|Any' } | Enable-NetFirewallRule";

    private const string FixDiscoveryServices =
        "foreach ($n in 'fdPHost','FDResPub') { Set-Service -Name $n -StartupType Automatic }\nRestart-Service -Name fdPHost,FDResPub -Force";

    private const string FixServer =
        "Set-Service -Name LanmanServer -StartupType Automatic\nStart-Service -Name LanmanServer";

    public static async Task<DiagnosticsReport> RunAsync()
    {
        var report = new DiagnosticsReport();
        var result = await Shell.PowerShellAsync(FactsScript, TimeSpan.FromSeconds(90));
        var json = Shell.ExtractJson(result.Output);
        if (json == null)
        {
            report.Checks.Add(new DiagnosticCheck
            {
                Category = "App", Status = CheckStatus.Problem,
                Title = "Could not read the network settings",
                Detail = result.Text,
            });
            return report;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var add = report.Checks.Add;
        report.Computer = S(root, "Computer") is { Length: > 0 } c ? c : Environment.MachineName;

        // ---------- Network ----------
        const string net = "Network";
        var profiles = Arr(root, "Profiles").ToList();
        var ips = Arr(root, "IPs").ToList();
        var lan = ips.FirstOrDefault(i => S(i, "Gateway").Length > 0 && !S(i, "IP").StartsWith("169.254."));
        var lanAlias = lan.ValueKind == JsonValueKind.Object ? S(lan, "Alias") : "";
        report.LanAddress = lan.ValueKind == JsonValueKind.Object ? S(lan, "IP") : null;
        IPAddress.TryParse(lan.ValueKind == JsonValueKind.Object ? S(lan, "Gateway") : "", out var peer);

        var currentCategory = "Private";
        if (profiles.Count == 0)
        {
            add(new() { Category = net, Status = CheckStatus.Problem, Title = "This PC is not connected to a network",
                Detail = "Connect to your Wi-Fi or plug in a network cable first." });
        }
        foreach (var p in profiles)
        {
            var cat = S(p, "Category");
            var alias = S(p, "Alias");
            if (alias == lanAlias || profiles.Count == 1)
                currentCategory = cat == "DomainAuthenticated" ? "Domain" : cat;
            if (cat == "Public")
            {
                add(new()
                {
                    Category = net, Status = CheckStatus.Problem,
                    Title = $"\"{S(p, "Name")}\" ({alias}) is set to Public",
                    Detail = "On a Public network Windows hides this PC and blocks file sharing. Make it Private if this is your home or office network.",
                    FixLabel = "Make Private",
                    FixScript = $"Set-NetConnectionProfile -InterfaceIndex {S(p, "Index")} -NetworkCategory Private",
                });
            }
            else
            {
                add(new() { Category = net, Status = CheckStatus.Ok,
                    Title = $"\"{S(p, "Name")}\" ({alias}) is set to {cat}",
                    Detail = "Sharing is allowed on this type of network." });
            }
        }

        foreach (var ip in ips.Where(i => S(i, "Gateway").Length > 0))
        {
            var addr = S(ip, "IP");
            if (addr.StartsWith("169.254."))
            {
                add(new() { Category = net, Status = CheckStatus.Problem, Title = $"{S(ip, "Alias")} has no address from the router ({addr})",
                    Detail = "The router didn't hand out an address. Reconnect to the network or restart the router.",
                    FixLabel = "Renew address", FixScript = "ipconfig /release; ipconfig /renew" });
                continue;
            }
            var gwOk = B(ip, "GatewayOk");
            add(new()
            {
                Category = net, Status = gwOk ? CheckStatus.Ok : CheckStatus.Warning,
                Title = $"{S(ip, "Alias")}: address {addr}, router {S(ip, "Gateway")}",
                Detail = gwOk ? "The router answers." : "The router didn't answer a ping. Some routers ignore pings; if other things on the network also fail, check the Wi-Fi or cable.",
            });
        }

        foreach (var vpn in Arr(root, "Vpn").Select(v => v.GetString() ?? ""))
        {
            add(new() { Category = net, Status = CheckStatus.Warning, Title = $"A VPN is connected: {vpn}",
                Detail = "VPNs often block or reroute traffic on your local network. If sharing fails, disconnect the VPN and try again." });
        }

        // ---------- Firewall ----------
        const string fwCat = "Firewall";
        var fwProfiles = Arr(root, "Firewall").ToDictionary(f => S(f, "Name"), f => B(f, "Enabled"));
        var fwOn = fwProfiles.TryGetValue(currentCategory, out var on) ? on : fwProfiles.Values.Any(v => v);
        if (fwProfiles.Count > 0 && fwProfiles.Values.All(v => !v))
        {
            add(new() { Category = fwCat, Status = CheckStatus.Warning, Title = "The Windows firewall is turned off",
                Detail = "Sharing works with the firewall on. Turn it back on once you've found the real cause.",
                FixLabel = "Turn firewall on", FixScript = "Set-NetFirewallProfile -All -Enabled True" });
        }
        else
        {
            add(new() { Category = fwCat, Status = CheckStatus.Info, Title = "Firewall: " + string.Join(", ", fwProfiles.Select(kv => $"{kv.Key} {(kv.Value ? "on" : "off")}")),
                Detail = $"This network uses the {currentCategory} firewall profile." });
        }

        var allow = Arr(root, "Allow").ToList();
        bool Allowed(string prefix) => allow.Any(a => S(a, "Name").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && ProfileMatches(S(a, "Profile"), currentCategory));

        add(Allowed("FPS-SMB-In-TCP")
            ? new() { Category = fwCat, Status = CheckStatus.Ok, Title = "The firewall allows file sharing (SMB, port 445)", Detail = $"Allowed on the {currentCategory} profile." }
            : new() { Category = fwCat, Status = fwOn ? CheckStatus.Problem : CheckStatus.Warning,
                Title = "The firewall doesn't allow file sharing on this network",
                Detail = "The \"File and Printer Sharing (SMB-In)\" rule is off for this network type, so other PCs can't open your shares.",
                FixLabel = "Allow file sharing", FixScript = FixFileSharingRules });

        add(Allowed("NETDIS-")
            ? new() { Category = fwCat, Status = CheckStatus.Ok, Title = "The firewall allows Network Discovery", Detail = "This PC can appear in other PCs' Network list." }
            : new() { Category = fwCat, Status = CheckStatus.Warning, Title = "Network Discovery is blocked by the firewall",
                Detail = "This PC won't show in the Network list on other PCs. Opening it directly by address still works.",
                FixLabel = "Allow discovery", FixScript = FixDiscoveryRules });

        var otherBlocks = 0;
        foreach (var b in Arr(root, "Block"))
        {
            var covers = ProfileMatches(S(b, "Profile"), currentCategory)
                         && PortCovers445(S(b, "Port"), S(b, "Protocol"))
                         && RemoteCovers(S(b, "Remote"), peer);
            if (!covers) { otherBlocks++; continue; }

            var desc = S(b, "Desc");
            add(new()
            {
                Category = fwCat, Status = fwOn ? CheckStatus.Problem : CheckStatus.Warning,
                Title = $"Firewall rule \"{S(b, "Display")}\" blocks file sharing",
                Detail = (desc.Length > 0 ? desc + "\n" : "") +
                         $"It blocks incoming connections from your local network{(peer != null ? $" (e.g. {peer})" : "")}. Block rules beat allow rules." +
                         (fwOn ? "" : " The firewall is off right now, but this rule will block again as soon as it's turned back on."),
                FixLabel = "Disable rule",
                FixScript = $"Disable-NetFirewallRule -Name {Shell.Q(S(b, "Name"))}",
            });
        }
        if (otherBlocks > 0)
        {
            add(new() { Category = fwCat, Status = CheckStatus.Info, Title = $"{otherBlocks} other inbound block rule(s) don't affect file sharing",
                Detail = "See \"Inbound block rules\" in the Command toolbox for the full list." });
        }

        // ---------- Services ----------
        const string svc = "Services";
        var services = Arr(root, "Services").ToDictionary(s => S(s, "Name"), s => S(s, "Status"), StringComparer.OrdinalIgnoreCase);
        bool Running(string n) => services.TryGetValue(n, out var st) && st == "Running";

        add(Running("LanmanServer")
            ? new() { Category = svc, Status = CheckStatus.Ok, Title = "The file sharing service (Server) is running" }
            : new() { Category = svc, Status = CheckStatus.Problem, Title = "The file sharing service (Server) is not running",
                Detail = "Without it this PC can't share anything.", FixLabel = "Start service", FixScript = FixServer });

        add(B(root, "Listen445")
            ? new() { Category = svc, Status = CheckStatus.Ok, Title = "This PC is listening for file sharing on port 445" }
            : new() { Category = svc, Status = CheckStatus.Problem, Title = "Nothing is listening on port 445",
                Detail = "The Server service may be stopped or broken. Starting it usually fixes this; otherwise restart the PC.",
                FixLabel = "Start service", FixScript = FixServer });

        add(Running("LanmanWorkstation")
            ? new() { Category = svc, Status = CheckStatus.Ok, Title = "The Workstation service is running", Detail = "Needed to connect to other PCs' shares." }
            : new() { Category = svc, Status = CheckStatus.Problem, Title = "The Workstation service is not running",
                Detail = "This PC can't open other PCs' shares without it.", FixLabel = "Start service",
                FixScript = "Set-Service -Name LanmanWorkstation -StartupType Automatic\nStart-Service -Name LanmanWorkstation" });

        add(Running("FDResPub") && Running("fdPHost")
            ? new() { Category = svc, Status = CheckStatus.Ok, Title = "The discovery services are running", Detail = "This PC announces itself in the Network list." }
            : new() { Category = svc, Status = CheckStatus.Warning, Title = "The discovery services are not both running",
                Detail = $"FDResPub: {services.GetValueOrDefault("FDResPub", "?")}, fdPHost: {services.GetValueOrDefault("fdPHost", "?")}. " +
                         "This PC may not appear in other PCs' Network list. Connecting directly by address still works.",
                FixLabel = "Restart discovery", FixScript = FixDiscoveryServices });

        // ---------- SMB settings ----------
        const string smb = "SMB settings";
        var server = O(root, "Server");
        var client = O(root, "Client");
        if (!B(server, "Smb2"))
            add(new() { Category = smb, Status = CheckStatus.Problem, Title = "SMB 2/3 is turned off on this PC",
                Detail = "Modern file sharing needs it.", FixLabel = "Turn on SMB 2/3",
                FixScript = "Set-SmbServerConfiguration -EnableSMB2Protocol $true -Force" });
        if (B(server, "Smb1"))
            add(new() { Category = smb, Status = CheckStatus.Warning, Title = "The old SMB 1 protocol is turned on",
                Detail = "SMB 1 is insecure. Only keep it if a very old device needs it.", FixLabel = "Turn off SMB 1",
                FixScript = "Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force" });

        add(B(server, "Signing")
            ? new() { Category = smb, Status = CheckStatus.Info, Title = "This PC requires signed connections (SMB signing)",
                Detail = "Windows, macOS and current Linux connect fine. Some TVs, phone apps, media players and old NAS boxes can't, and fail even with the right password.",
                FixLabel = "Allow unsigned", FixScript = "Set-SmbServerConfiguration -RequireSecuritySignature $false -Force" }
            : new() { Category = smb, Status = CheckStatus.Info, Title = "This PC accepts unsigned connections",
                Detail = "Older devices can connect. Requiring signing is safer if you only use modern computers.",
                FixLabel = "Require signing", FixScript = "Set-SmbServerConfiguration -RequireSecuritySignature $true -Force" });

        add(B(client, "Guest")
            ? new() { Category = smb, Status = CheckStatus.Warning, Title = "This PC may connect to password-free guest shares",
                Detail = "That's less secure. Turn it off unless an old NAS or router share needs it.",
                FixLabel = "Block guest shares", FixScript = "Set-SmbClientConfiguration -EnableInsecureGuestLogons $false -Force" }
            : new() { Category = smb, Status = CheckStatus.Info, Title = "Password-free guest shares are blocked when connecting out",
                Detail = "Normal and safer. Only old NAS boxes and router USB shares need guest access (error 1272 means this).",
                FixLabel = "Allow guest shares", FixScript = "Set-SmbClientConfiguration -EnableInsecureGuestLogons $true -RequireSecuritySignature $false -Force" });

        // ---------- Shares & sign-in ----------
        const string sh = "Shared folders";
        var shares = Arr(root, "Shares").ToList();
        report.ShareNames.AddRange(shares.Select(s => S(s, "Name")));
        var hostForPaths = report.LanAddress ?? report.Computer;
        if (shares.Count == 0)
        {
            add(new() { Category = sh, Status = CheckStatus.Warning, Title = "No folders are shared",
                Detail = "Share a folder in the \"My shared folders\" tab." });
        }
        else
        {
            add(new() { Category = sh, Status = CheckStatus.Ok, Title = $"{shares.Count} shared folder(s)",
                Detail = string.Join("\n", shares.Select(s => $@"\\{hostForPaths}\{S(s, "Name")}   →   {S(s, "Path")}")) });

            if (report.LanAddress != null)
            {
                var unc = $@"\\{report.LanAddress}\{S(shares[0], "Name")}";
                var reachable = await ProbeAsync(unc, TimeSpan.FromSeconds(8));
                add(reachable
                    ? new() { Category = sh, Status = CheckStatus.Ok, Title = $"This PC can open {unc} through its network address",
                        Detail = "This PC's side of sharing works. If another PC still can't connect, use \"Connect to other PCs\" on that PC (or check its sign-in and the router)." }
                    : new() { Category = sh, Status = CheckStatus.Problem, Title = $"This PC can't open its own share {unc}",
                        Detail = "Sharing is broken on this PC itself. Fix the problems above first." });
            }
        }

        var user = S(root, "User");
        add(S(root, "PrincipalSource") == "MicrosoftAccount"
            ? new() { Category = "Sign-in", Status = CheckStatus.Info, Title = "Other PCs sign in with your Microsoft account",
                Detail = $"Username: your Microsoft account email (or {report.Computer}\\{user}). Password: the Microsoft account password. A Windows PIN does not work over the network." }
            : new() { Category = "Sign-in", Status = CheckStatus.Info, Title = $"Other PCs sign in as {report.Computer}\\{user}",
                Detail = "Use this account's password. A Windows PIN does not work over the network, and accounts without a password can't sign in." });

        return report;
    }

    private static async Task<bool> ProbeAsync(string unc, TimeSpan timeout)
    {
        var probe = Task.Run(() => Directory.Exists(unc));
        return await Task.WhenAny(probe, Task.Delay(timeout)) == probe && probe.Result;
    }

    internal static bool ProfileMatches(string profile, string category) =>
        profile.Contains("Any", StringComparison.OrdinalIgnoreCase) ||
        profile.Contains(category, StringComparison.OrdinalIgnoreCase);

    internal static bool PortCovers445(string ports, string protocol)
    {
        if (!(protocol is "" or "Any" || protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase))) return false;
        if (ports is "" or "Any") return true;
        foreach (var part in ports.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "445") return true;
            var dash = part.Split('-');
            if (dash.Length == 2 && int.TryParse(dash[0], out var lo) && int.TryParse(dash[1], out var hi) && lo <= 445 && 445 <= hi)
                return true;
        }
        return false;
    }

    /// <summary>Does a firewall remote-address filter include <paramref name="peer"/> (a device on the LAN)?</summary>
    internal static bool RemoteCovers(string remote, IPAddress? peer)
    {
        foreach (var t in remote.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (t.Equals("Any", StringComparison.OrdinalIgnoreCase) || t.StartsWith("LocalSubnet", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Intranet", StringComparison.OrdinalIgnoreCase) || t.Equals("PlayToDevice", StringComparison.OrdinalIgnoreCase))
                return true;
            if (peer == null) continue;

            if (t.Contains('-'))
            {
                var p = t.Split('-');
                if (p.Length == 2 && IPAddress.TryParse(p[0], out var lo) && IPAddress.TryParse(p[1], out var hi)
                    && lo.AddressFamily == peer.AddressFamily && hi.AddressFamily == peer.AddressFamily
                    && Compare(lo, peer) <= 0 && Compare(peer, hi) <= 0)
                    return true;
            }
            else if (t.Contains('/'))
            {
                var p = t.Split('/');
                if (IPAddress.TryParse(p[0], out var network) && network.AddressFamily == AddressFamily.InterNetwork
                    && peer.AddressFamily == AddressFamily.InterNetwork)
                {
                    int bits = int.TryParse(p[1], out var n) ? n
                        : IPAddress.TryParse(p[1], out var mask) ? CountBits(mask) : -1;
                    if (bits >= 0 && SameNetwork(network, peer, bits)) return true;
                }
            }
            else if (IPAddress.TryParse(t, out var single) && single.Equals(peer))
            {
                return true;
            }
        }
        return false;
    }

    private static int Compare(IPAddress a, IPAddress b)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        for (var i = 0; i < x.Length; i++)
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    private static int CountBits(IPAddress mask) =>
        mask.GetAddressBytes().Sum(b => System.Numerics.BitOperations.PopCount(b));

    internal static bool SameNetwork(IPAddress a, IPAddress b, int prefix)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        if (x.Length != y.Length) return false;
        for (var i = 0; i < x.Length && prefix > 0; i++, prefix -= 8)
        {
            var mask = prefix >= 8 ? (byte)0xFF : (byte)(0xFF << (8 - prefix));
            if ((x[i] & mask) != (y[i] & mask)) return false;
        }
        return true;
    }
}

internal static class Json
{
    public static IEnumerable<JsonElement> Arr(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) yield break;
        if (v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray()) yield return x;
        else if (v.ValueKind is JsonValueKind.Object or JsonValueKind.String)
            yield return v;
    }

    public static IEnumerable<JsonElement> Items(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? [e] : [];

    public static string S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    public static bool B(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "True"));

    public static JsonElement O(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
}
