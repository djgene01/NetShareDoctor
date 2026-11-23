using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using NetShareDoctor.Models;
using NetShareDoctor.Services;

namespace NetShareDoctor.Pages;

public partial class ToolsPage : UserControl
{
    private bool _running;

    public ToolsPage()
    {
        InitializeComponent();
        var view = new ListCollectionView(ToolCatalog.All.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ToolCommand.Category)));
        ToolList.ItemsSource = view;
    }

    private ToolCommand? Selected => ToolList.SelectedItem as ToolCommand;

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } t) return;
        ToolTitle.Text = t.Label;
        ToolDescription.Text = t.Description;
        ToolScript.Text = t.Script;
        RunButton.IsEnabled = !_running;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } t) await RunAsync(t.Label, t.Script, t.Admin);
    }

    private async void RunCustom_Click(object sender, RoutedEventArgs e) => await RunCustomAsync();

    private async void CustomBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunCustomAsync();
    }

    private Task RunCustomAsync()
    {
        var cmd = CustomBox.Text.Trim();
        return cmd.Length == 0 ? Task.CompletedTask : RunAsync(cmd, cmd, CustomAdmin.IsChecked == true);
    }

    private async Task RunAsync(string label, string script, bool admin)
    {
        if (_running) return;
        _running = true;
        RunButton.IsEnabled = false;
        OutputBox.Text = $"PS> {script}\n\nRunning…";
        try
        {
            var r = admin ? await Shell.PowerShellElevatedAsync(script) : await Shell.PowerShellAsync(script);
            var text = r.Text.Length > 0 ? r.Text : "(no output)";
            var hint = NetInfo.Explain(text);
            OutputBox.Text = $"PS> {script}\n\n{text}" + (hint.Length > 0 ? $"\n\nWhat this means: {hint}" : "");
        }
        finally
        {
            _running = false;
            RunButton.IsEnabled = Selected != null;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (OutputBox.Text.Length > 0) Clipboard.SetText(OutputBox.Text);
    }
}
