using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GasparSystemHealth.Models;
using GasparSystemHealth.Services;

namespace GasparSystemHealth;

public partial class MainWindow : Window
{
    private readonly SessionLogger _logger;
    private readonly SystemProbeService _probeService;
    private readonly DiagnosticService _diagnosticService;
    private readonly ToolCommandService _toolService;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _diagnosticCts;
    private CancellationTokenSource? _sensorBootstrapCts;
    private bool _toolOperationRunning;
    private string? _activeOperationName;

    public MainWindow()
    {
        InitializeComponent();

        string appRoot = AppContext.BaseDirectory;
        _logger = new SessionLogger();
        _probeService = new SystemProbeService(appRoot);
        _diagnosticService = new DiagnosticService(_probeService, _logger);
        _toolService = new ToolCommandService();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_diagnosticCts is null)
            {
                await RefreshDashboardAsync();
            }
        };

        Loaded += OnLoaded;
        Closing += OnClosing;

        RunDiagnosticButton.Click += async (_, _) => await RunDiagnosticAsync();
        RefreshButton.Click += async (_, _) => await RefreshDashboardAsync();
        OpenLogButton.Click += (_, _) => OpenLogFolder();
        MenuSystemButton.Click += (_, _) => ShowModule("system");
        MenuStorageButton.Click += (_, _) => ShowModule("storage");
        MenuNetworkButton.Click += (_, _) => ShowModule("network");
        MenuFirewallButton.Click += (_, _) => ShowModule("firewall");
        MenuUpdateButton.Click += (_, _) => ShowModule("update");
        MenuIntegrityButton.Click += (_, _) => ShowModule("integrity");
        MenuLogButton.Click += (_, _) => ShowModule("log");
        SystemInfoButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetSystemInfoAsync);
        TopProcessesButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetTopProcessesAsync);
        CriticalEventsButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetCriticalEventsAsync);
        ServicesButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetServicesSummaryAsync);
        PendingRebootButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetPendingRebootStatusAsync);
        DriversButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetDriverSummaryAsync);
        StartupAppsButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetStartupAppsAsync);
        ScheduledTasksButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.GetScheduledTasksAsync);
        DiskVolumesButton.Click += async (_, _) => await RunToolAsync(StorageOutputTextBox, _toolService.GetDiskVolumesAsync);
        PhysicalDisksButton.Click += async (_, _) => await RunToolAsync(StorageOutputTextBox, _toolService.GetPhysicalDisksAsync);
        ChkDskButton.Click += async (_, _) => await RunToolAsync(StorageOutputTextBox, _toolService.GetChkDskInfoAsync);
        SmartStatusButton.Click += async (_, _) => await RunToolAsync(StorageOutputTextBox, _toolService.GetSmartStatusAsync);
        StorageHealthButton.Click += async (_, _) => await RunToolAsync(StorageOutputTextBox, _toolService.GetStorageHealthAsync);
        IpConfigButton.Click += async (_, _) => await RunToolAsync(NetworkOutputTextBox, _toolService.GetIpConfigurationAsync);
        NetworkAdaptersButton.Click += async (_, _) => await RunToolAsync(NetworkOutputTextBox, _toolService.GetNetworkAdaptersAsync);
        PingButton.Click += async (_, _) => await RunToolAsync(NetworkOutputTextBox, _toolService.RunPingTestAsync);
        DnsButton.Click += async (_, _) => await RunToolAsync(NetworkOutputTextBox, _toolService.RunDnsTestAsync);
        RouteTableButton.Click += async (_, _) => await RunToolAsync(NetworkOutputTextBox, _toolService.GetRouteTableAsync);
        FlushDnsButton.Click += async (_, _) => await RunToolAsync(NetworkOutputTextBox, _toolService.FlushDnsCacheAsync);
        FirewallStatusButton.Click += async (_, _) => await RunToolAsync(FirewallOutputTextBox, _toolService.GetFirewallStatusAsync);
        FirewallRulesButton.Click += async (_, _) => await RunToolAsync(FirewallOutputTextBox, _toolService.GetFirewallRulesAsync);
        WindowsUpdateCheckButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.CheckWindowsUpdatesAsync);
        WindowsUpdateHistoryButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.GetWindowsUpdateHistoryAsync);
        AntivirusDefinitionsButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.GetAntivirusDefinitionsStatusAsync);
        AntivirusUpdateButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.UpdateAntivirusDefinitionsAsync);
        DefenderQuickScanButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.RunDefenderQuickScanAsync);
        OpenWindowsUpdateButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.OpenWindowsUpdateAsync);
        SfcScanButton.Click += async (_, _) => await RunToolAsync(IntegrityOutputTextBox, _toolService.RunSfcScanAsync);
        SfcVerifyButton.Click += async (_, _) => await RunToolAsync(IntegrityOutputTextBox, _toolService.RunSfcVerifyAsync);
        DismCheckButton.Click += async (_, _) => await RunToolAsync(IntegrityOutputTextBox, _toolService.RunDismCheckHealthAsync);
        DismScanButton.Click += async (_, _) => await RunToolAsync(IntegrityOutputTextBox, _toolService.RunDismScanHealthAsync);
        DismRestoreButton.Click += async (_, _) => await RunToolAsync(IntegrityOutputTextBox, _toolService.RunDismRestoreHealthAsync);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetRunModeBadge();
        LogPathText.Text = _logger.SessionLogPath;
        SetToolStatus("IN ATTESA", "Nessun comando in esecuzione.", "-", Brushes.SlateGray, Brushes.Gainsboro);
        ShowModule("system");
        AppendLog("Applicazione avviata.");
        _clockTimer.Start();
        _refreshTimer.Start();
        await RefreshDashboardAsync();
        _ = EnsureSensorSupportAsync();
    }

    private async Task RefreshDashboardAsync()
    {
        try
        {
            SystemSnapshot snapshot = await Task.Run(_probeService.CaptureSnapshot);
            ApplySnapshot(snapshot);
            await RefreshQuickStatusesAsync(snapshot);
            FooterStatusText.Text = $"Ultimo aggiornamento {snapshot.SnapshotTime:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AppendLog($"Errore dashboard: {ex.Message}");
            FooterStatusText.Text = "Errore durante l'aggiornamento dashboard.";
        }
    }

    private async Task EnsureSensorSupportAsync()
    {
        if (_probeService.SensorsInstalled)
        {
            return;
        }

        _sensorBootstrapCts?.Dispose();
        _sensorBootstrapCts = new CancellationTokenSource();

        try
        {
            SensorSourceText.Text = "Installazione sensori";
            SensorNoteText.Text = "Download automatico LibreHardwareMonitor in corso...";
            AppendLog("Bootstrap sensori: download automatico avviato.");

            BootstrapResult result = await _probeService.EnsureSensorSupportAsync(_sensorBootstrapCts.Token);
            if (result.Success)
            {
                AppendLog(result.Message);
                await RefreshDashboardAsync();
            }
            else
            {
                SensorSourceText.Text = "LibreHardwareMonitor";
                SensorNoteText.Text = $"Installazione automatica non riuscita: {result.Message}";
                AppendLog($"Bootstrap sensori fallito: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            SensorSourceText.Text = "LibreHardwareMonitor";
            SensorNoteText.Text = $"Bootstrap sensori fallito: {ex.Message}";
            AppendLog($"Bootstrap sensori eccezione: {ex.Message}");
        }
        finally
        {
            _sensorBootstrapCts?.Dispose();
            _sensorBootstrapCts = null;
        }
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        ComputerNameText.Text = snapshot.ComputerName;
        OperatingSystemText.Text = snapshot.OperatingSystem;
        CpuNameText.Text = snapshot.CpuName;
        GpuNameText.Text = snapshot.GpuName;

        CpuUsageBar.Value = snapshot.CpuUsagePercent;
        CpuUsageText.Text = $"{snapshot.CpuUsagePercent:F1}% utilizzo";

        MemoryUsageBar.Value = snapshot.MemoryUsedPercent;
        MemoryText.Text = $"{snapshot.MemoryUsedGb:F1} / {snapshot.MemoryTotalGb:F1} GB";
        UptimeText.Text = $"Online da {FormatUptime(snapshot.Uptime)}";

        DiskUsageBar.Value = snapshot.PrimaryDriveUsedPercent;
        DiskText.Text = $"{snapshot.PrimaryDriveLabel} {snapshot.PrimaryDriveTotalGb:F1} GB";
        DiskFreeText.Text = $"{snapshot.PrimaryDriveFreeGb:F1} GB liberi";

        CpuTempText.Text = snapshot.Temperatures.CpuCelsius.HasValue ? $"{snapshot.Temperatures.CpuCelsius:F1} C" : "N/A";
        GpuTempText.Text = snapshot.Temperatures.GpuCelsius.HasValue ? $"{snapshot.Temperatures.GpuCelsius:F1} C" : "N/A";
        SensorSourceText.Text = snapshot.Temperatures.Source;
        SensorNoteText.Text = snapshot.Temperatures.Note;
    }

    private async Task RefreshQuickStatusesAsync(SystemSnapshot snapshot)
    {
        ApplyQuickStatus(SystemStatusDot, SystemStatusText, _diagnosticService.EvaluateSystem(snapshot));
        ApplyQuickStatus(DiskStatusDot, DiskStatusText, _diagnosticService.EvaluateDisk(snapshot));
        ApplyQuickStatus(SensorStatusDot, SensorStatusText, _diagnosticService.EvaluateSensors(snapshot));
        ApplyQuickStatus(SecurityStatusDot, SecurityStatusText, _diagnosticService.EvaluateSecurity());
        ApplyQuickStatus(NetworkStatusDot, NetworkStatusText, await _diagnosticService.EvaluateNetworkAsync());
    }

    private async Task RunDiagnosticAsync()
    {
        if (_diagnosticCts is not null)
        {
            return;
        }

        _diagnosticCts = new CancellationTokenSource();
        RunDiagnosticButton.IsEnabled = false;
        DiagnosticProgressBar.Value = 0;
        DiagnosticPercentText.Text = "0%";
        DiagnosticStatusText.Text = "Controllo in corso";
        DiagnosticDetailText.Text = "Avvio procedura completa...";
        AppendLog("Controllo completo avviato dall'utente.");

        try
        {
            var progress = new Progress<(int percent, string title, string detail)>(value =>
            {
                DiagnosticProgressBar.Value = value.percent;
                DiagnosticPercentText.Text = $"{value.percent}%";
                DiagnosticStatusText.Text = value.title;
                DiagnosticDetailText.Text = value.detail;
                FooterStatusText.Text = $"Diagnostica: {value.title}";
            });

            await _diagnosticService.RunAsync(progress, ApplyDiagnosticResult, _diagnosticCts.Token);
            DiagnosticStatusText.Text = "Controllo completato";
            DiagnosticDetailText.Text = "Tutte le fasi del controllo completo sono terminate.";
            AppendLog("Controllo completo completato.");
        }
        catch (OperationCanceledException)
        {
            DiagnosticStatusText.Text = "Controllo annullato";
            DiagnosticDetailText.Text = "La procedura e stata annullata.";
            AppendLog("Controllo completo annullato.");
        }
        catch (Exception ex)
        {
            DiagnosticStatusText.Text = "Errore";
            DiagnosticDetailText.Text = ex.Message;
            AppendLog($"Errore controllo completo: {ex.Message}");
        }
        finally
        {
            _diagnosticCts.Dispose();
            _diagnosticCts = null;
            RunDiagnosticButton.IsEnabled = true;
            await RefreshDashboardAsync();
        }
    }

    private void ApplyDiagnosticResult(DiagnosticStepResult result)
    {
        Brush brush = result.Success ? Brushes.LimeGreen : Brushes.Goldenrod;
        string text = $"{result.Title}: {result.Status} - {result.Detail}";

        switch (result.Title)
        {
            case "Sistema":
                SetDot(SystemStatusDot, SystemStatusText, brush, text);
                break;
            case "Disco":
                SetDot(DiskStatusDot, DiskStatusText, brush, text);
                break;
            case "Rete":
            case "Firewall":
                SetDot(NetworkStatusDot, NetworkStatusText, brush, text);
                break;
            case "Sicurezza":
            case "Definizioni AV":
                SetDot(SecurityStatusDot, SecurityStatusText, brush, text);
                break;
            case "Sensori":
                SetDot(SensorStatusDot, SensorStatusText, brush, text);
                break;
        }

        AppendLog($"{result.Title}: {result.Status} - {result.Detail}");
    }

    private void AppendLog(string line)
    {
        _logger.Write(line);
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private async Task RunToolAsync(
        TextBox targetTextBox,
        Func<CancellationToken, Task<ToolExecutionResult>> action)
    {
        if (_diagnosticCts is not null || _toolOperationRunning)
        {
            return;
        }

        _toolOperationRunning = true;
        _activeOperationName = "Comando in esecuzione";
        FooterStatusText.Text = "Comando in corso...";
        targetTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] Avvio comando..." + Environment.NewLine;
        SetToolStatus("IN CORSO", "Esecuzione comando di sistema...", "-", Brushes.DodgerBlue, Brushes.White);
        AppendLog("Comando avviato.");
        SetButtonsEnabled(false);

        try
        {
            using var cts = new CancellationTokenSource();
            ToolExecutionResult result = await action(cts.Token);
            _activeOperationName = result.Title;
            targetTextBox.Text =
                $"Titolo: {result.Title}{Environment.NewLine}" +
                $"Comando: {result.CommandLine}{Environment.NewLine}" +
                $"Exit code: {result.ExitCode}{Environment.NewLine}" +
                $"Esito: {(result.Success ? "OK" : "ERRORE")}{Environment.NewLine}{Environment.NewLine}" +
                result.Output;

            SetToolStatus(
                result.Success ? "OK" : "ERRORE",
                $"{result.Title} {(result.Success ? "eseguito correttamente" : "terminato con problemi")}.",
                result.CommandLine,
                result.Success ? Brushes.LimeGreen : Brushes.OrangeRed,
                Brushes.White);

            AppendLog($"{result.Title} completato. Exit code {result.ExitCode}.");
            FooterStatusText.Text = $"{result.Title} completato.";
        }
        catch (Exception ex)
        {
            targetTextBox.Text = ex.Message;
            SetToolStatus("ERRORE", "Comando terminato con eccezione.", ex.Message, Brushes.OrangeRed, Brushes.White);
            AppendLog($"Comando fallito: {ex.Message}");
            FooterStatusText.Text = "Comando terminato con errore.";
        }
        finally
        {
            _toolOperationRunning = false;
            _activeOperationName = null;
            SetButtonsEnabled(true);
        }
    }

    private void SetButtonsEnabled(bool isEnabled)
    {
        RunDiagnosticButton.IsEnabled = isEnabled && _diagnosticCts is null;
        RefreshButton.IsEnabled = isEnabled && _diagnosticCts is null;
        SystemInfoButton.IsEnabled = isEnabled;
        TopProcessesButton.IsEnabled = isEnabled;
        CriticalEventsButton.IsEnabled = isEnabled;
        ServicesButton.IsEnabled = isEnabled;
        PendingRebootButton.IsEnabled = isEnabled;
        DriversButton.IsEnabled = isEnabled;
        StartupAppsButton.IsEnabled = isEnabled;
        ScheduledTasksButton.IsEnabled = isEnabled;
        DiskVolumesButton.IsEnabled = isEnabled;
        PhysicalDisksButton.IsEnabled = isEnabled;
        ChkDskButton.IsEnabled = isEnabled;
        SmartStatusButton.IsEnabled = isEnabled;
        StorageHealthButton.IsEnabled = isEnabled;
        IpConfigButton.IsEnabled = isEnabled;
        NetworkAdaptersButton.IsEnabled = isEnabled;
        PingButton.IsEnabled = isEnabled;
        DnsButton.IsEnabled = isEnabled;
        RouteTableButton.IsEnabled = isEnabled;
        FlushDnsButton.IsEnabled = isEnabled;
        FirewallStatusButton.IsEnabled = isEnabled;
        FirewallRulesButton.IsEnabled = isEnabled;
        WindowsUpdateCheckButton.IsEnabled = isEnabled;
        WindowsUpdateHistoryButton.IsEnabled = isEnabled;
        AntivirusDefinitionsButton.IsEnabled = isEnabled;
        AntivirusUpdateButton.IsEnabled = isEnabled;
        DefenderQuickScanButton.IsEnabled = isEnabled;
        OpenWindowsUpdateButton.IsEnabled = isEnabled;
        SfcScanButton.IsEnabled = isEnabled;
        SfcVerifyButton.IsEnabled = isEnabled;
        DismCheckButton.IsEnabled = isEnabled;
        DismScanButton.IsEnabled = isEnabled;
        DismRestoreButton.IsEnabled = isEnabled;
    }

    private void ShowModule(string module)
    {
        SystemPanel.Visibility = module == "system" ? Visibility.Visible : Visibility.Collapsed;
        StoragePanel.Visibility = module == "storage" ? Visibility.Visible : Visibility.Collapsed;
        NetworkPanel.Visibility = module == "network" ? Visibility.Visible : Visibility.Collapsed;
        FirewallPanel.Visibility = module == "firewall" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = module == "update" ? Visibility.Visible : Visibility.Collapsed;
        IntegrityPanel.Visibility = module == "integrity" ? Visibility.Visible : Visibility.Collapsed;
        LogPanel.Visibility = module == "log" ? Visibility.Visible : Visibility.Collapsed;

        PaintMenuButton(MenuSystemButton, module == "system");
        PaintMenuButton(MenuStorageButton, module == "storage");
        PaintMenuButton(MenuNetworkButton, module == "network");
        PaintMenuButton(MenuFirewallButton, module == "firewall");
        PaintMenuButton(MenuUpdateButton, module == "update");
        PaintMenuButton(MenuIntegrityButton, module == "integrity");
        PaintMenuButton(MenuLogButton, module == "log");
    }

    private static void PaintMenuButton(Button button, bool active)
    {
        button.Background = active
            ? new SolidColorBrush(Color.FromRgb(25, 194, 255))
            : new SolidColorBrush(Color.FromRgb(20, 24, 35));
        button.BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(25, 194, 255))
            : new SolidColorBrush(Color.FromRgb(42, 45, 57));
        button.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(208, 213, 221));
    }

    private void SetToolStatus(string badge, string status, string command, Brush badgeBrush, Brush textBrush)
    {
        ToolStatusBadge.Background = badgeBrush;
        ToolStatusBadgeText.Text = badge;
        ToolStatusBadgeText.Foreground = textBrush;
        ToolStatusText.Text = status;
        ToolCommandText.Text = $"Comando: {command}";
    }

    private void OpenLogFolder()
    {
        string folder = Path.GetDirectoryName(_logger.SessionLogPath) ?? AppContext.BaseDirectory;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = folder,
            UseShellExecute = true
        });
    }

    private void SetRunModeBadge()
    {
        bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        RunModeText.Text = isAdmin ? "AMMINISTRATORE" : "LIMITATO";
        RunModeText.Foreground = isAdmin ? Brushes.LimeGreen : Brushes.Goldenrod;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}g {uptime.Hours}h {uptime.Minutes}m";
        }

        return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
    }

    private static void SetDot(System.Windows.Shapes.Ellipse dot, System.Windows.Controls.TextBlock text, Brush brush, string message)
    {
        dot.Fill = brush;
        text.Text = message;
    }

    private static void ApplyQuickStatus(System.Windows.Shapes.Ellipse dot, TextBlock text, QuickStatusState state)
    {
        Brush brush = state.Level switch
        {
            QuickStatusLevel.Good => Brushes.LimeGreen,
            QuickStatusLevel.Warning => Brushes.Goldenrod,
            QuickStatusLevel.Error => Brushes.OrangeRed,
            _ => Brushes.SlateGray
        };

        SetDot(dot, text, brush, state.Message);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_diagnosticCts is not null)
        {
            e.Cancel = true;
            MessageBox.Show(
                "Attendere la fine del controllo completo prima di chiudere.",
                "Controllo in corso",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_toolOperationRunning)
        {
            e.Cancel = true;
            MessageBox.Show(
                $"Attendere la fine dell'operazione in corso: {_activeOperationName}.",
                "Operazione in corso",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _clockTimer.Stop();
        _refreshTimer.Stop();
        _sensorBootstrapCts?.Cancel();
        _sensorBootstrapCts?.Dispose();
        _probeService.Dispose();
    }
}
