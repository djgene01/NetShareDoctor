using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetShareDoctor.Services;

namespace NetShareDoctor;

public partial class App : Application
{
    private const string MutexName = @"Local\NetShareDoctor.SingleInstance";
    public const string ElevatedArg = "--elevated";

    private static Mutex? _instance;

    /// <summary>"1.1.0", from the assembly's informational version without the source revision suffix.</summary>
    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+')[0];

    /// <summary>The 64 px frame of the app icon, for showing it inside windows (an Image would pick the 16 px one).</summary>
    public static BitmapSource IconImage() =>
        BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            .Frames.OrderBy(f => Math.Abs(f.PixelWidth - 64)).First();

    protected override void OnStartup(StartupEventArgs e)
    {
        // net.exe and cmdkey.exe write in the console's OEM code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        // The elevated copy started by "Restart as administrator" waits for the old window to close.
        var wait = e.Args.Contains(ElevatedArg) ? TimeSpan.FromSeconds(5) : TimeSpan.Zero;
        if (!TryClaimInstance(wait))
        {
            ActivateOtherInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _instance?.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static bool TryClaimInstance(TimeSpan wait)
    {
        try
        {
            var m = new Mutex(false, MutexName);
            bool owned;
            try { owned = m.WaitOne(wait); }
            catch (AbandonedMutexException) { owned = true; }
            if (owned) { _instance = m; return true; }
            m.Dispose();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated copy owns the mutex and a normal one can't open it: that copy is running.
            return false;
        }
    }

    private static void ActivateOtherInstance()
    {
        using var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            using (p)
            {
                if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (Native.IsIconic(p.MainWindowHandle)) Native.ShowWindow(p.MainWindowHandle, Native.SW_RESTORE);
                Native.SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Network Share Doctor", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
