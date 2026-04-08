using System.Security.Principal;
using System.Windows;

namespace GasparSystemHealth;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
}
