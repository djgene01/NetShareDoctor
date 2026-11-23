# Network Share Doctor

A Windows desktop app that finds out why PCs can't see or open each other's shared folders, fixes it with one click, and makes connecting to shares painless.

Windows file sharing (SMB) breaks in quiet ways: a network set to Public, a stray firewall block rule, a stopped discovery service, a Windows PIN used as a password, a signing mismatch. Windows' own error messages rarely say which. This app checks all of them and explains the result in plain words.

## Features

**Diagnose this PC.** Answers "can other PCs reach this one?" It checks:

- network type (Public / Private), IP address, router reachability, connected VPNs
- firewall state, the File and Printer Sharing and Network Discovery rules, and any inbound **block rule** that covers your local network (block rules beat allow rules)
- the Server, Workstation and discovery services, and whether port 445 is listening
- SMB 1/2/3, SMB signing, guest-share settings
- your shared folders, plus a self-test that opens one through the network address
- which username other PCs should sign in with (local account or Microsoft account)

Each problem has a fix button. Fixes ask for administrator permission through UAC. "Copy report" puts the full result on the clipboard.

**Connect to other PCs.** Saves connections (PC, share, username, drive letter):

- *Test this PC* checks the name lookup, ping, port 445 and 139, and whether the PC is on the same network
- *Show shares* lists that PC's shared folders; click one to use it
- *Connect*, *Disconnect*, *Open in Explorer*
- *Clear saved logins* fixes "already connected with a different account" (error 1219)
- error numbers (53, 67, 86, 1219, 1272, 1326, …) are explained in plain language

Passwords are never written to the app's files. If you choose, they're stored in Windows Credential Manager.

**Mapped drives.** Every connection with its status. Open it, disconnect it, or save it as a connection.

**My shared folders.** Share a folder (read-only or read-write, for Everyone or one account). It can set the folder's own (NTFS) permissions too, because Windows checks both. You can also stop sharing and copy the network path.

**Command toolbox.** About 30 diagnostic commands in groups, each with a short description: ipconfig, devices on the network, routes, firewall rules, who is connected to me, open files, failed sign-ins, saved credentials, SMB logs, and so on. You can also run your own command, as administrator if needed.

## Download

Get `NetShareDoctor-<version>-win-x64.zip` from the releases, unzip it, and run `NetShareDoctor.exe`. It's self-contained, so no .NET install is needed. Windows 10 or 11, x64.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project NetShareDoctor -c Release
```

Make the self-contained zip:

```powershell
dotnet publish NetShareDoctor -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/NetShareDoctor
Compress-Archive dist/NetShareDoctor/* dist/NetShareDoctor-1.0.0-win-x64.zip -Force
```

## How it works

The app is WPF on .NET 10 with no third-party packages. Checks run Windows PowerShell's built-in cmdlets (`Get-NetConnectionProfile`, `Get-NetFirewallRule`, `Get-SmbServerConfiguration`, …) and `net.exe` / `cmdkey.exe`. Fixes run in a separate elevated PowerShell. You can read the exact command for every fix by hovering its button.

| Folder | Contents |
| --- | --- |
| `NetShareDoctor/Services/Diagnostics.cs` | the checks for this PC and their fixes |
| `NetShareDoctor/Services/RemoteTester.cs` | testing another PC and listing its shares |
| `NetShareDoctor/Services/SmbClient.cs` | connecting, disconnecting and clearing saved sign-ins |
| `NetShareDoctor/Services/ToolCatalog.cs` | the command toolbox list |
| `NetShareDoctor/Services/NetInfo.cs` | plain-language meanings of error numbers |
| `NetShareDoctor/Pages/` | the five tabs |

Saved connections are kept in `%AppData%\NetShareDoctor\connections.json`.

## License

[MIT](LICENSE) © 2026 djgene01
