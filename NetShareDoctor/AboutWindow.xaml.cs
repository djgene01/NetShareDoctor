using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace NetShareDoctor;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppIcon.Source = App.IconImage();
        VersionText.Text = $"Version {App.Version}";
        var copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        CopyrightText.Text = $"{copyright}. Free and open source under the MIT License.";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
