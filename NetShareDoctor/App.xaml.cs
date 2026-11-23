using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace NetShareDoctor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // net.exe and cmdkey.exe write in the console's OEM code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Network Share Doctor", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
