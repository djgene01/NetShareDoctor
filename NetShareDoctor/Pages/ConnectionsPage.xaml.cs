using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NetShareDoctor.Models;
using NetShareDoctor.Services;

namespace NetShareDoctor.Pages;

public partial class ConnectionsPage : UserControl
{
    private readonly ObservableCollection<SavedConnection> _items;

    public ConnectionsPage()
    {
        InitializeComponent();
        _items = new ObservableCollection<SavedConnection>(ConnectionStore.Load());
        SavedList.ItemsSource = _items;
        StoreText.Text = "Saved in " + ConnectionStore.FilePath + ". Passwords are kept only in Windows Credential Manager.";
        if (_items.Count == 0) NewItem();
        else SavedList.SelectedIndex = 0;
    }

    private SavedConnection? Current => SavedList.SelectedItem as SavedConnection;

    private void NewItem()
    {
        var c = new SavedConnection();
        _items.Add(c);
        SavedList.SelectedItem = c;
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewItem();

    private void SavedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var c = Current;
        FormCard.DataContext = c;
        FormCard.IsEnabled = c != null;
        PasswordBox.Clear();
        SharesCard.Visibility = Visibility.Collapsed;
        ResultsList.ItemsSource = null;
        if (c != null)
        {
            var keep = c.DriveLetter;
            DriveBox.ItemsSource = SmbClient.FreeDriveLetters(keep).ToList();
            c.DriveLetter = keep;
            DriveBox.SelectedItem = keep;
        }
    }

    /// <summary>Lets people paste \\pc\share (or smb://pc/share) into the PC box.</summary>
    private void HostBox_LostFocus(object sender, RoutedEventArgs e) => Normalize();

    private bool Normalize()
    {
        var c = Current;
        if (c == null) return false;
        var (host, share) = RemoteTester.Split(c.Host);
        c.Host = host;
        if (share.Length > 0) c.Share = share;
        c.Share = c.Share.Trim().Trim('\\', '/');
        if (c.Name is "New connection" or "" && host.Length > 0)
            c.Name = c.Share.Length > 0 ? $"{c.Share} on {host}" : host;
        if (host.Length == 0)
        {
            Log("Enter the other PC's name or IP address first.");
            return false;
        }
        return true;
    }

    private void Log(string text)
    {
        LogBox.AppendText((LogBox.Text.Length > 0 ? "\n\n" : "") + $"[{DateTime.Now:HH:mm:ss}] " + text);
        LogBox.ScrollToEnd();
    }

    private void Persist()
    {
        try { ConnectionStore.Save(_items); }
        catch (Exception ex) { Log("Couldn't save connections: " + ex.Message); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        Persist();
        Log($"Saved \"{Current!.Name}\".");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var c = Current;
        if (c == null) return;
        if (MessageBox.Show(Window.GetWindow(this)!, $"Delete the saved connection \"{c.Name}\"?\nThis doesn't disconnect it or touch the other PC.",
                "Delete connection", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _items.Remove(c);
        Persist();
        if (_items.Count == 0) NewItem(); else SavedList.SelectedIndex = 0;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        var c = Current!;
        if (c.Share.Length == 0)
        {
            Log("Pick a shared folder first. Press \"Show shares\" to list them.");
            return;
        }
        ConnectButton.IsEnabled = false;
        try
        {
            Log($"Connecting to {c.UncPath}…");
            var (ok, log) = await SmbClient.ConnectAsync(c, PasswordBox.Password);
            Log(log);
            if (ok)
            {
                PasswordBox.Clear();
                Persist();
                Log("Connected." + (c.HasDrive ? $" It's drive {c.DriveLetter} in Explorer." : ""));
            }
        }
        finally { ConnectButton.IsEnabled = true; }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        var (_, log) = await SmbClient.DisconnectAsync(Current!);
        Log(log);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        var c = Current!;
        var target = c.HasDrive && System.IO.Directory.Exists(c.DriveLetter + "\\") ? c.DriveLetter + "\\" : c.UncPath;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
        Log($"Opened {target} in Explorer. If it asks for a password or shows an error, press Connect here instead to see why.");
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        TestButton.IsEnabled = false;
        try
        {
            ResultsList.ItemsSource = null;
            Log($"Testing {Current!.Host}…");
            var results = await RemoteTester.TestAsync(Current!.Host);
            ResultsList.ItemsSource = results;
            Log(string.Join("\n", results.Select(r => r.ToString())));
        }
        finally { TestButton.IsEnabled = true; }
    }

    private async void ShowShares_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        var host = Current!.Host;
        Log($"Listing shares on {host}…");
        var (shares, raw) = await RemoteTester.ListSharesAsync(host);
        SharesWrap.Children.Clear();
        if (!raw.Success)
        {
            SharesCard.Visibility = Visibility.Collapsed;
            var hint = NetInfo.Explain(raw.Text);
            Log(raw.Text + (hint.Length > 0 ? "\n\nWhat this means: " + hint : "") +
                "\n\nIf it's a sign-in problem, fill in Username and Password and press Connect (with the share name) instead.");
            return;
        }
        foreach (var s in shares.Where(s => !s.EndsWith('$')))
        {
            var b = new Button { Content = s, Margin = new Thickness(0, 0, 8, 8), FontFamily = (System.Windows.Media.FontFamily)FindResource("Mono") };
            b.Click += (_, _) => { Current!.Share = s; Normalize(); };
            SharesWrap.Children.Add(b);
        }
        SharesCard.Visibility = Visibility.Visible;
        Log(shares.Count == 0 ? $"{host} doesn't share any folders." : $"{host} shares: " + string.Join(", ", shares));
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        Log(await SmbClient.ClearLoginsAsync(Current!.Host));
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();
}
