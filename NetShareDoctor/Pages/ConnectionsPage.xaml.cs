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
    private bool _persisting;

    public ConnectionsPage()
    {
        InitializeComponent();
        _items = new ObservableCollection<SavedConnection>(ConnectionStore.Load());
        SavedList.ItemsSource = _items;
        StoreText.Text = "Saved in " + ConnectionStore.FilePath + ". Passwords are kept only in Windows Credential Manager.";
        if (_items.Count == 0) NewItem();
        else SavedList.SelectedIndex = 0;
        ConnectionStore.Changed += () => Dispatcher.Invoke(Reload);
    }

    private SavedConnection? Current => SavedList.SelectedItem as SavedConnection;

    /// <summary>
    /// Picks up connections saved elsewhere (the Mapped drives tab). Items already in the list keep their
    /// unsaved edits, and new unsaved items stay; only connections this page hasn't seen are added.
    /// </summary>
    private void Reload()
    {
        if (_persisting) return;
        var selectedId = Current?.Id;
        var known = _items.Select(i => i.Id).ToHashSet();
        foreach (var c in ConnectionStore.Load().Where(c => !known.Contains(c.Id)))
            _items.Add(c);
        SavedList.SelectedItem = _items.FirstOrDefault(i => i.Id == selectedId) ?? _items.FirstOrDefault();
    }

    /// <summary>Disables the action buttons while a connect, test or listing runs.</summary>
    private async Task BusyAsync(Func<Task> work)
    {
        ActionsPanel.IsEnabled = false;
        try { await work(); }
        finally { ActionsPanel.IsEnabled = true; }
    }

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
        _persisting = true;
        try { ConnectionStore.Save(_items); }
        catch (Exception ex) { Log("Couldn't save connections: " + ex.Message); }
        finally { _persisting = false; }
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
        var owner = Window.GetWindow(this)!;
        var question = $"Delete the saved connection \"{c.Name}\"?\nThis doesn't disconnect it or touch the other PC.";
        if (c.Host.Length == 0)
        {
            if (MessageBox.Show(owner, question, "Delete connection", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        }
        else
        {
            var answer = MessageBox.Show(owner,
                question + "\n\nDelete saved password too?\n" +
                $"  Yes: also delete the password saved for {c.Host} in Windows Credential Manager (other connections to {c.Host} use it too).\n" +
                "  No: delete only the connection.",
                "Delete connection", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.Yes) Log(SmbClient.ForgetPassword(c.Host));
        }
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
        await BusyAsync(async () =>
        {
            Log($"Connecting to {c.UncPath}…");
            var (ok, log) = await SmbClient.ConnectAsync(c, PasswordBox.Password);
            Log(log);
            if (ok)
            {
                PasswordBox.Clear();
                Persist();
                Log("Connected." + (c.HasDrive ? $" It's drive {c.DriveLetter} in Explorer." : ""));
                if (Shell.IsAdmin && c.HasDrive)
                    Log("Note: this app runs as administrator, so the drive belongs to the administrator session and Explorer may not show it. " +
                        "Connect from a normal (not elevated) window to see it in Explorer.");
            }
        });
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        var c = Current!;
        await BusyAsync(async () => Log((await SmbClient.DisconnectAsync(c)).Log));
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
        var host = Current!.Host;
        await BusyAsync(async () =>
        {
            ResultsList.ItemsSource = null;
            Log($"Testing {host}…");
            var results = await RemoteTester.TestAsync(host);
            ResultsList.ItemsSource = results;
            Log(string.Join("\n", results.Select(r => r.ToString())));
        });
    }

    private async void ShowShares_Click(object sender, RoutedEventArgs e)
    {
        if (!Normalize()) return;
        var host = Current!.Host;
        Log($"Listing shares on {host}…");
        List<string> shares = [];
        ShellResult raw = null!;
        await BusyAsync(async () => (shares, raw) = await RemoteTester.ListSharesAsync(host));
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
        var host = Current!.Host;
        await BusyAsync(async () => Log(await SmbClient.ClearLoginsAsync(host)));
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();
}
