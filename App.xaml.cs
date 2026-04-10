using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace GasparSystemHealth;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

        if (!isAdmin)
        {
            MessageBox.Show(
                "L'app richiede privilegi amministratore. Se il sistema non puo concederli, l'avvio verra interrotto.",
                "Avvio amministratore richiesto",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteEmergencyLog("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            "Si e verificato un errore imprevisto. L'evento e stato registrato nel log di emergenza.",
            "Errore applicazione",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteEmergencyLog("UnhandledException", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteEmergencyLog("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void WriteEmergencyLog(string source, Exception ex)
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GasparSystemHealth",
                "Logs");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "emergency.log");
            string text = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .Append(source).Append(": ")
                .Append(ex.GetType().Name).Append(" - ")
                .Append(ex.Message).AppendLine()
                .ToString();
            File.AppendAllText(path, text, Encoding.UTF8);
        }
        catch
        {
            // Last-resort logging must stay silent if even emergency logging fails.
        }
    }
}
