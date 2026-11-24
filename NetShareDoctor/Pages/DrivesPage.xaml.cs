using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NetShareDoctor.Models;
using NetShareDoctor.Services;
using static NetShareDoctor.Services.Json;

namespace NetShareDoctor.Pages;

public partial class DrivesPage : UserControl
{
    private const string ListScript =
        "$x=@(Get-SmbMapping | ForEach-Object { [pscustomobject]@{Local=\"$($_.LocalPath)\";Remote=\"$($_.RemotePath)\";Status=\"$($_.Status)\"} })\n" +
        "ConvertTo-Json -InputObject $x -Compress";

    public DrivesPage() => InitializeComponent();

    private async void Page_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusText.Text = "Loading…";
        var r = await Shell.PowerShellAsync(ListScript);
        var json = Shell.ExtractJson(r.Output);
        if (json == null) { StatusText.Text = r.Text; return; }
        using var doc = JsonDocument.Parse(json);
        var rows = Items(doc.RootElement)
            .Select(e => new DriveRow(S(e, "Local") is { Length: > 0 } l ? l : "(none)", S(e, "Remote"), Friendly(S(e, "Status"))))
            .ToList();
        DrivesList.ItemsSource = rows;
        StatusText.Text = rows.Count == 0 ? "Not connected to any shares." : $"{rows.Count} connection(s).";
    }

    private static string Friendly(string status) => status switch
    {
        "OK" or "0" => "Connected",
        "Unavailable" or "6" => "Unavailable (PC offline?)",
        "Disconnected" or "2" => "Disconnected",
        "Reconnecting" or "5" => "Reconnecting",
        _ => status,
    };

    private DriveRow? Selected()
    {
        if (DrivesList.SelectedItem is DriveRow row) return row;
        StatusText.Text = "Select a connection first.";
        return null;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row) return;
        var target = row.Local != "(none)" ? row.Local + "\\" : row.Remote;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row) return;
        var target = row.Local != "(none)" ? row.Local : row.Remote;
        var (_, log) = await SmbClient.DisconnectAsync(target);
        await RefreshAsync();
        StatusText.Text = log;
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row) return;
        var (host, share) = RemoteTester.Split(row.Remote);
        var items = ConnectionStore.Load();
        if (items.Any(c => c.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && c.Share.Equals(share, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = $@"\\{host}\{share} is already a saved connection in ""Connect to other PCs"".";
            return;
        }
        items.Add(new SavedConnection
        {
            Name = $"{share} on {host}", Host = host, Share = share,
            DriveLetter = row.Local != "(none)" ? row.Local : "None",
        });
        try
        {
            ConnectionStore.Save(items);
            StatusText.Text = $"Saved. It's in the list in \"Connect to other PCs\".";
        }
        catch (Exception ex) { StatusText.Text = "Couldn't save the connection: " + ex.Message; }
    }
}
