using System.Windows;
using System.Windows.Controls;
using NetShareDoctor.Models;
using NetShareDoctor.Services;

namespace NetShareDoctor.Pages;

public partial class DiagnosePage : UserControl
{
    private List<DiagnosticCheck> _checks = [];
    private bool _loaded;

    public DiagnosePage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await RunAsync();
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        RunButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        SummaryText.Text = "Checking network, firewall, services and shares… (takes a few seconds)";
        try
        {
            var report = await Diagnostics.RunAsync();
            foreach (var c in report.Checks.Where(c => c.FixScript != null))
                c.FixCommand = new RelayCommand(() => FixAsync(c));
            _checks = report.Checks;
            ApplyFilter();

            var problems = _checks.Count(c => c.Status == CheckStatus.Problem);
            var warnings = _checks.Count(c => c.Status == CheckStatus.Warning);
            SummaryText.Text = problems > 0
                ? $"Found {problems} problem(s) that stop sharing" + (warnings > 0 ? $" and {warnings} thing(s) to check." : ".") + " Use the button on each one to fix it."
                : warnings > 0
                    ? $"No blocking problems. {warnings} thing(s) worth checking below."
                    : "Everything looks right. If another PC still can't connect, test from that PC with \"Connect to other PCs\".";

            if (report.LanAddress != null)
            {
                var lines = new List<string> { $@"Other PCs connect to:  \\{report.LanAddress}   or   \\{report.Computer}" };
                lines.AddRange(report.ShareNames.Select(s => $@"                       \\{report.LanAddress}\{s}"));
                AddressText.Text = string.Join("\n", lines);
                AddressText.Visibility = Visibility.Visible;
            }
            else
            {
                AddressText.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            RunButton.IsEnabled = true;
            CopyButton.IsEnabled = _checks.Count > 0;
        }
    }

    private async Task FixAsync(DiagnosticCheck check)
    {
        var r = await Shell.PowerShellElevatedAsync(check.FixScript!);
        if (!r.Success)
        {
            MessageBox.Show(Window.GetWindow(this)!, r.Text, $"{check.FixLabel} didn't work", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RunAsync();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter() =>
        ChecksList.ItemsSource = ProblemsOnly.IsChecked == true ? _checks.Where(c => c.NeedsAttention).ToList() : _checks;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = $"Network Share Doctor report – {Environment.MachineName} – {DateTime.Now:yyyy-MM-dd HH:mm}\n\n" +
                   string.Join("\n", _checks.Select(c => c.ToString()));
        Clipboard.SetText(text);
        SummaryText.Text = "Report copied to the clipboard.";
    }
}
