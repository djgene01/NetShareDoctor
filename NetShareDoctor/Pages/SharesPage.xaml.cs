using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetShareDoctor.Models;
using NetShareDoctor.Services;
using static NetShareDoctor.Services.Json;

namespace NetShareDoctor.Pages;

public partial class SharesPage : UserControl
{
    private const string ListScript =
        "$x=@(Get-SmbShare | Where-Object { -not $_.Special } | ForEach-Object {\n" +
        "  $acc = (Get-SmbShareAccess -Name $_.Name | ForEach-Object { \"$($_.AccountName) ($($_.AccessControlType) $($_.AccessRight))\" }) -join '; '\n" +
        "  [pscustomobject]@{Name=$_.Name;Path=$_.Path;Description=$_.Description;Access=$acc} })\n" +
        "ConvertTo-Json -InputObject $x -Compress";

    public SharesPage() => InitializeComponent();

    private async void Page_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var r = await Shell.PowerShellAsync(ListScript);
        var json = Shell.ExtractJson(r.Output);
        if (json == null) { StatusText.Text = r.Text; return; }
        var host = NetInfo.LanAddress() ?? Environment.MachineName;
        using var doc = JsonDocument.Parse(json);
        SharesList.ItemsSource = Items(doc.RootElement)
            .Select(e => new ShareRow(S(e, "Name"), S(e, "Path"), S(e, "Description"), S(e, "Access"), $@"\\{host}\{S(e, "Name")}"))
            .ToList();
    }

    private ShareRow? Selected()
    {
        if (SharesList.SelectedItem is ShareRow row) return row;
        StatusText.Text = "Select a shared folder in the list first.";
        return null;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row) return;
        Clipboard.SetText(row.NetworkPath);
        StatusText.Text = $"Copied {row.NetworkPath}";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{row.Path}\"") { UseShellExecute = true });
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row) return;
        if (MessageBox.Show(Window.GetWindow(this)!,
                $"Stop sharing \"{row.Name}\"?\n\nThe folder and its files stay on this PC; other PCs just can't open it any more.",
                "Stop sharing", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var r = await Shell.PowerShellElevatedAsync($"Remove-SmbShare -Name {Shell.Q(row.Name)} -Force");
        StatusText.Text = r.Success ? $"\"{row.Name}\" is no longer shared." : r.Text;
        await RefreshAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder to share" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        PathBox.Text = dlg.FolderName;
        if (NameBox.Text.Length == 0)
            NameBox.Text = Regex.Replace(new DirectoryInfo(dlg.FolderName).Name, @"[\\/:*?""<>|\[\]=;,+]", "");
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        var name = NameBox.Text.Trim();
        var account = AccountBox.Text.Trim();
        if (!Directory.Exists(path)) { StatusText.Text = "That folder doesn't exist."; return; }
        if (name.Length == 0 || Regex.IsMatch(name, @"[\\/:*?""<>|\[\]=;,+]")) { StatusText.Text = "Enter a share name without \\ / : * ? \" < > | [ ] = ; , +"; return; }
        if (account.Length == 0) account = "Everyone";

        var readOnly = AccessBox.SelectedIndex == 1;
        var script =
            $"New-SmbShare -Name {Shell.Q(name)} -Path {Shell.Q(path)} -Description 'Shared with Network Share Doctor' " +
            $"{(readOnly ? "-ReadAccess" : "-FullAccess")} {Shell.Q(account)} | Out-Null";
        if (NtfsBox.IsChecked == true)
        {
            script += $"\n$g = {Shell.Q(account)} + ':(OI)(CI){(readOnly ? "RX" : "M")}'\n" +
                      $"icacls.exe {Shell.Q(path)} /grant $g | Out-Null\n" +
                      "if ($LASTEXITCODE -ne 0) { throw \"The share was created, but setting the folder permissions failed (icacls exit $LASTEXITCODE).\" }";
        }

        AddButton.IsEnabled = false;
        try
        {
            var r = await Shell.PowerShellElevatedAsync(script);
            var host = NetInfo.LanAddress() ?? Environment.MachineName;
            StatusText.Text = r.Success ? $@"Shared. Other PCs open it at \\{host}\{name}" : r.Text;
            if (r.Success) { PathBox.Clear(); NameBox.Clear(); }
            await RefreshAsync();
        }
        finally { AddButton.IsEnabled = true; }
    }
}
