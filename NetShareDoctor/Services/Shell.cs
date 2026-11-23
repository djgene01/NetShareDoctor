using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace NetShareDoctor.Services;

public sealed record ShellResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;

    public string Text
    {
        get
        {
            var o = Output.TrimEnd();
            var e = Error.Trim();
            if (e.Length == 0) return o;
            return o.Length == 0 ? e : o + Environment.NewLine + e;
        }
    }
}

/// <summary>Runs PowerShell and console tools, optionally elevated through UAC.</summary>
public static class Shell
{
    public static bool IsAdmin { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private const string Prelude =
        "$ProgressPreference='SilentlyContinue'\n[Console]::OutputEncoding=[System.Text.Encoding]::UTF8\n";

    /// <summary>Quotes a value as a PowerShell single-quoted string literal.</summary>
    public static string Q(string? value) => "'" + (value ?? "").Replace("'", "''") + "'";

    private static Encoding OemEncoding =>
        Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

    public static async Task<ShellResult> RunAsync(string file, IEnumerable<string> args,
        TimeSpan? timeout = null, Encoding? encoding = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = encoding ?? OemEncoding,
            StandardErrorEncoding = encoding ?? OemEncoding,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception ex) { return new ShellResult(-1, "", ex.Message); }

        // Close stdin so tools that would prompt (net use asking for a password) fail instead of hanging.
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(90));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { /* already gone */ }
            return new ShellResult(-2, await stdout, "Timed out.");
        }
        return new ShellResult(p.ExitCode, await stdout, await stderr);
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string[] PsArgs(string script) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Encode(script)];

    /// <summary>Runs a script as the current user and returns its formatted output (errors included).</summary>
    public static Task<ShellResult> PowerShellAsync(string script, TimeSpan? timeout = null)
    {
        var full = Prelude + "& {\n" + script + "\n} 2>&1 | Out-String -Width 300";
        return RunAsync("powershell.exe", PsArgs(full), timeout, Encoding.UTF8);
    }

    /// <summary>
    /// Runs a script as administrator. Shows a UAC prompt unless the app already runs elevated.
    /// Any error stops the script and is returned in <see cref="ShellResult.Error"/>.
    /// </summary>
    public static async Task<ShellResult> PowerShellElevatedAsync(string script)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"nsd_{Guid.NewGuid():N}.txt");
        var full = Prelude +
                   "$ErrorActionPreference='Stop'\n$code=0\n" +
                   "try { $o = & {\n" + script + "\n} 2>&1 | Out-String -Width 300 }\n" +
                   "catch { $o = 'ERROR: ' + $_.Exception.Message; $code = 1 }\n" +
                   $"Set-Content -LiteralPath {Q(outFile)} -Value $o -Encoding UTF8\n" +
                   "exit $code";

        int exit;
        if (IsAdmin)
        {
            var r = await RunAsync("powershell.exe", PsArgs(full), TimeSpan.FromMinutes(5), Encoding.UTF8);
            exit = r.ExitCode;
        }
        else
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = string.Join(' ', PsArgs(full)),
            };
            try
            {
                using var p = Process.Start(psi)!;
                await p.WaitForExitAsync();
                exit = p.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new ShellResult(-1, "", "Cancelled: administrator permission was not given.");
            }
        }

        var text = "";
        try
        {
            if (File.Exists(outFile)) text = (await File.ReadAllTextAsync(outFile)).Trim();
            File.Delete(outFile);
        }
        catch { /* best effort */ }

        return exit == 0
            ? new ShellResult(0, text, "")
            : new ShellResult(exit, "", text.Length > 0 ? text : $"Failed (exit code {exit}).");
    }

    /// <summary>Returns the JSON part of a script's output (scripts may print warnings first).</summary>
    public static string? ExtractJson(string output)
    {
        var start = output.IndexOfAny(['{', '[']);
        if (start < 0) return null;
        var close = output[start] == '{' ? '}' : ']';
        var end = output.LastIndexOf(close);
        return end > start ? output[start..(end + 1)] : null;
    }
}
