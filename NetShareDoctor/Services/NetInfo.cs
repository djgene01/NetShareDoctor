using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetShareDoctor.Services;

public static partial class NetInfo
{
    public sealed record Subnet(IPAddress Address, int Prefix);

    /// <summary>IPv4 subnets of adapters that are up and have a default gateway (the real LAN).</summary>
    public static List<Subnet> LocalSubnets()
    {
        var result = new List<Subnet>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                continue;
            foreach (var u in props.UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork && !u.Address.ToString().StartsWith("169.254."))
                    result.Add(new Subnet(u.Address, u.PrefixLength));
        }
        return result;
    }

    public static string? LanAddress() => LocalSubnets().FirstOrDefault()?.Address.ToString();

    /// <summary>Plain-language meaning of the error numbers net.exe and Explorer show.</summary>
    public static string Explain(string netOutput)
    {
        var m = ErrorNumber().Match(netOutput);
        return m.Success && long.TryParse(m.Groups[1].Value, out var code) ? Explain(code) : "";
    }

    /// <summary>Plain-language meaning of a Windows error code (negative values are read as unsigned NTSTATUS/HRESULT).</summary>
    public static string Explain(long code) => (code < 0 ? (uint)code : code) switch
    {
        5 => "Access denied: the sign-in worked, but this account isn't allowed into that share, or the folder's own permissions block it.",
        53 => "Network path not found: the PC can't be reached or isn't sharing. Check the name or IP, that file sharing is on there, and the router. Try the IP address instead of the name.",
        64 => "The connection dropped. Usually Wi-Fi, a VPN or a firewall in between.",
        67 => "That share name doesn't exist. Use \"Show shares\" to see the real names.",
        85 => "That drive letter is already in use. Pick another one.",
        86 or 1326 => "Wrong username or password. Use an account on the other PC (PCNAME\\user, or its Microsoft account email) with the account password. A Windows PIN doesn't work.",
        1219 => "You're already connected to this PC with a different account, and Windows only allows one. Press \"Clear saved logins\", then connect again.",
        1272 or 1240 => "The other side only offers password-free guest access, which Windows blocks. Sign in with a real account, or allow guest shares in the Diagnose tab (less secure).",
        1327 => "Account restriction: usually the account has no password. Set a password on that account.",
        1331 => "That account is disabled on the other PC.",
        1385 => "That account isn't allowed to log on over the network (a local policy, or an account without a password).",
        1909 => "That account is locked after too many wrong passwords. Wait or unlock it on the other PC.",
        2202 => "The username isn't valid. Use PCNAME\\user or an email address.",
        2221 => "That user doesn't exist on the other PC.",
        1203 or 1222 => "No network provider accepted the path: the Workstation service or network is down.",
        2250 => "That connection doesn't exist (it was already disconnected).",
        3227320323 or 3221225581 => "SMB signing mismatch: one side requires signed connections and the other can't do them. Allow unsigned connections in the Diagnose tab on the PC that requires them.",
        _ => "",
    };

    [GeneratedRegex(@"(?:error|Fehler|erreur|errore|fout|erro)\D{0,20}?(\d{1,10})", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorNumber();
}
