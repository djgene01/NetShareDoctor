using NetShareDoctor.Models;

namespace NetShareDoctor.Services;

/// <summary>Every diagnostic command, grouped, with a plain description.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolCommand> All { get; } =
    [
        new("Network", "IP configuration", "Addresses, gateway, DNS and DHCP for every adapter (ipconfig /all).", "ipconfig /all"),
        new("Network", "Network profiles", "Whether each network is Public, Private or Domain.", "Get-NetConnectionProfile | Format-List Name,InterfaceAlias,NetworkCategory,IPv4Connectivity"),
        new("Network", "Network adapters", "Adapters that exist, whether they're up, and their speed.", "Get-NetAdapter | Sort-Object Status -Descending | Format-Table Name,InterfaceDescription,Status,LinkSpeed,MacAddress -AutoSize"),
        new("Network", "Devices seen on the network", "Other devices this PC has talked to recently (ARP table).", "Get-NetNeighbor -AddressFamily IPv4 | Where-Object { $_.State -notin 'Unreachable','Permanent' } | Sort-Object { [version]$_.IPAddress } | Format-Table IPAddress,LinkLayerAddress,State,InterfaceAlias -AutoSize"),
        new("Network", "Routing table", "Where traffic goes. A VPN often shows up here.", "route print -4"),
        new("Network", "Flush DNS cache", "Forget cached name lookups, e.g. after a PC changed its address.", "ipconfig /flushdns"),
        new("Network", "NetBIOS names", "The names this PC announces on the network.", "nbtstat -n"),
        new("Network", "Renew IP address", "Ask the router for a new address. The network drops for a few seconds.", "ipconfig /release | Out-Null; ipconfig /renew", true),

        new("Firewall", "Firewall profiles", "Whether the firewall is on for each network type.", "Get-NetFirewallProfile | Format-Table Name,Enabled,DefaultInboundAction,DefaultOutboundAction -AutoSize"),
        new("Firewall", "Inbound block rules", "Rules that block incoming traffic. These beat allow rules.", "Get-NetFirewallRule -Direction Inbound -Action Block -Enabled True | ForEach-Object { $a = $_ | Get-NetFirewallAddressFilter; $p = $_ | Get-NetFirewallPortFilter; [pscustomobject]@{Rule=$_.DisplayName; Profile=$_.Profile; Remote=(@($a.RemoteAddress) -join ','); Protocol=$p.Protocol; Port=(@($p.LocalPort) -join ',')} } | Format-List"),
        new("Firewall", "File sharing rules", "The File and Printer Sharing rules and where they're on.", "Get-NetFirewallRule -Name 'FPS-*' | Sort-Object Direction,DisplayName | Format-Table DisplayName,Profile,Enabled,Direction -AutoSize"),
        new("Firewall", "Network discovery rules", "The rules that let this PC appear in the Network list.", "Get-NetFirewallRule -Name 'NETDIS-*' | Sort-Object Direction,DisplayName | Format-Table DisplayName,Profile,Enabled,Direction -AutoSize"),
        new("Firewall", "Turn firewall on", "Turn the firewall back on for all network types.", "Set-NetFirewallProfile -All -Enabled True; Get-NetFirewallProfile | Format-Table Name,Enabled", true),

        new("Sharing (this PC)", "Shared folders", "Every folder this PC shares, including hidden admin shares.", "Get-SmbShare | Format-Table Name,Path,Description -AutoSize"),
        new("Sharing (this PC)", "Share permissions", "Who may use each shared folder.", "Get-SmbShare | Where-Object { -not $_.Special } | Get-SmbShareAccess | Format-Table Name,AccountName,AccessControlType,AccessRight -AutoSize"),
        new("Sharing (this PC)", "Who is connected to me", "PCs that have this PC's shares open right now.", "Get-SmbSession | Format-Table ClientComputerName,ClientUserName,NumOpens,Dialect -AutoSize", true),
        new("Sharing (this PC)", "Open files", "Files other PCs have open on this PC.", "Get-SmbOpenFile | Format-Table ClientComputerName,ClientUserName,Path -AutoSize", true),
        new("Sharing (this PC)", "SMB server settings", "Protocol versions, signing and encryption for this PC's shares.", "Get-SmbServerConfiguration | Format-List EnableSMB1Protocol,EnableSMB2Protocol,RequireSecuritySignature,EnableSecuritySignature,EncryptData,RejectUnencryptedAccess,AnnounceServer"),
        new("Sharing (this PC)", "Listening ports 445 and 139", "Whether this PC is accepting file sharing connections.", "Get-NetTCPConnection -LocalPort 445,139 -State Listen | Format-Table LocalAddress,LocalPort,OwningProcess -AutoSize"),
        new("Sharing (this PC)", "Sharing services", "Services that file sharing and discovery depend on.", "Get-Service LanmanServer,LanmanWorkstation,FDResPub,fdPHost,SSDPSRV,upnphost | Format-Table Name,DisplayName,Status,StartType -AutoSize"),
        new("Sharing (this PC)", "Restart discovery services", "Fixes this PC vanishing from other PCs' Network list.", "foreach ($n in 'fdPHost','FDResPub') { Set-Service -Name $n -StartupType Automatic }; Restart-Service fdPHost,FDResPub -Force; Get-Service fdPHost,FDResPub | Format-Table Name,Status,StartType", true),
        new("Sharing (this PC)", "Restart file sharing service", "Restarts the Server service. Connected PCs are dropped.", "Restart-Service LanmanServer -Force; Get-Service LanmanServer | Format-Table Name,Status", true),
        new("Sharing (this PC)", "Local user accounts", "Accounts other PCs can sign in with.", "Get-LocalUser | Format-Table Name,Enabled,PrincipalSource,PasswordRequired,LastLogon -AutoSize"),
        new("Sharing (this PC)", "Failed sign-ins to this PC", "Recent wrong-password attempts, with the account and source address.", "Get-WinEvent -FilterHashtable @{LogName='Security';Id=4625} -MaxEvents 25 | ForEach-Object { [pscustomobject]@{Time=$_.TimeCreated; Account=$_.Properties[5].Value; Domain=$_.Properties[6].Value; From=$_.Properties[19].Value; Reason=('0x{0:X}' -f $_.Properties[9].Value)} } | Format-Table -AutoSize", true),
        new("Sharing (this PC)", "SMB server security log", "Recent SMB errors such as signing or access problems.", "Get-WinEvent -LogName Microsoft-Windows-SMBServer/Security -MaxEvents 20 | Format-List TimeCreated,Id,Message", true),

        new("Connecting out", "My connections", "Shares this PC is connected to right now.", "Get-SmbConnection | Format-Table ServerName,ShareName,UserName,Dialect,NumOpens -AutoSize"),
        new("Connecting out", "Mapped drives (net use)", "Mapped drives and remembered connections.", "net use"),
        new("Connecting out", "Saved Windows credentials", "Passwords Windows remembers for other PCs (Credential Manager).", "cmdkey /list"),
        new("Connecting out", "SMB client settings", "Signing and guest settings used when connecting to other PCs.", "Get-SmbClientConfiguration | Format-List RequireSecuritySignature,EnableSecuritySignature,EnableInsecureGuestLogons,RequireEncryption"),
        new("Connecting out", "SMB client errors", "Recent errors when connecting to other PCs.", "Get-WinEvent -LogName Microsoft-Windows-SMBClient/Connectivity -MaxEvents 20 | Format-List TimeCreated,Id,Message"),
        new("Connecting out", "Disconnect everything", "Drop every mapped drive and share connection.", "net use * /delete /y"),
    ];
}
