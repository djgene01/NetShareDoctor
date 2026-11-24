using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using NetShareDoctor.Services;

namespace NetShareDoctor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        HeaderIcon.Source = App.IconImage();
        VersionText.Text = "v" + App.Version;
        var ip = NetInfo.LanAddress();
        SubtitleText.Text = $"{Environment.MachineName}" + (ip != null ? $"  ·  {ip}" : "  ·  not on a network") +
                            $"  ·  signed in as {Environment.UserName}";
        if (Shell.IsAdmin)
        {
            AdminText.Text = "Running as administrator";
            ElevateButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            AdminText.Text = "Fixes ask for administrator permission";
        }
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, App.ElevatedArg) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception) { /* UAC declined */ }
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();
}
