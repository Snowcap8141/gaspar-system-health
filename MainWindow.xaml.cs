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
    private bool _refreshInProgress;
    private string? _activeOperationName;
    private readonly Dictionary<string, int> _diagnosticCategorySeverity = new();
    private readonly List<string> _diagnosticSummaryLines = new();
    private int _diagnosticOkCount;
    private int _diagnosticWarningCount;
    private int _diagnosticErrorCount;

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
            if (_diagnosticCts is null && !_toolOperationRunning && _sensorBootstrapCts is null)
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
        SystemSettingsButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.OpenSystemSettingsAsync);
        NetworkSettingsButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.OpenNetworkSettingsAsync);
        WindowsSecurityButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.OpenWindowsSecurityAsync);
        InstalledAppsButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.OpenInstalledAppsSettingsAsync);
        QuickWindowsUpdateButton.Click += async (_, _) => await RunToolAsync(SystemOutputTextBox, _toolService.OpenWindowsUpdateAsync);
        FullSystemSpecsButton.Click += async (_, _) => await ShowFullSystemSpecsAsync();
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
        InstallWindowsUpdateButton.Click += async (_, _) => await RunToolAsync(WindowsUpdateOutputTextBox, _toolService.InstallWindowsUpdatesAsync);
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
        if (_refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            SystemSnapshot snapshot = await Task.Run(_probeService.CaptureSnapshot);
            ApplySnapshot(snapshot);
            await RefreshQuickStatusesAsync(snapshot);
            FooterStatusText.Text = $"Ultimo aggiornamento {snapshot.SnapshotTime:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AppendLog($"Errore dashboard: {ex.GetType().Name} - {ex.Message}");
            FooterStatusText.Text = "Errore durante l'aggiornamento dashboard.";
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async Task EnsureSensorSupportAsync()
    {
        if (_probeService.SensorsInstalled)
        {
            return;
        }

        _sensorBootstrapCts?.Dispose();
        _sensorBootstrapCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

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
            SensorNoteText.Text = SecurityHelpers.ToSafeUserMessage(ex, "Bootstrap sensori non riuscito.");
            AppendLog($"Bootstrap sensori eccezione: {ex.GetType().Name} - {ex.Message}");
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
        ApplyQuickStatus(RebootStatusDot, RebootStatusText, _diagnosticService.EvaluateReboot());
        ApplyQuickStatus(NetworkStatusDot, NetworkStatusText, await _diagnosticService.EvaluateNetworkAsync());
    }

    private async Task RunDiagnosticAsync()
    {
        if (_diagnosticCts is not null)
        {
            return;
        }

        _diagnosticCts = new CancellationTokenSource();
        ResetDiagnosticCategoryStates();
        ResetDiagnosticSummaryUi();
        _refreshTimer.Stop();
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
            UpdateDiagnosticSummaryText();
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
            DiagnosticDetailText.Text = SecurityHelpers.ToSafeUserMessage(ex, "Controllo completo interrotto.");
            AppendLog($"Errore controllo completo: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            _diagnosticCts.Dispose();
            _diagnosticCts = null;
            RunDiagnosticButton.IsEnabled = true;
            _refreshTimer.Start();
            await RefreshDashboardAsync();
        }
    }

    private void ApplyDiagnosticResult(DiagnosticStepResult result)
    {
        RegisterDiagnosticSummary(result);

        string? category = MapDiagnosticCategory(result.Title);
        if (category is null)
        {
            AppendLog($"{result.Title}: {result.Status} - {result.Detail}");
            return;
        }

        int severity = GetSeverityRank(result.Status);
        if (_diagnosticCategorySeverity.TryGetValue(category, out int currentSeverity) && severity < currentSeverity)
        {
            AppendLog($"{result.Title}: {result.Status} - {result.Detail}");
            return;
        }

        _diagnosticCategorySeverity[category] = severity;
        Brush brush = GetSeverityBrush(result.Status);
        string text = BuildQuickStatusMessage(category, result);

        switch (category)
        {
            case "system":
                SetDot(SystemStatusDot, SystemStatusText, brush, text);
                break;
            case "disk":
                SetDot(DiskStatusDot, DiskStatusText, brush, text);
                break;
            case "network":
                SetDot(NetworkStatusDot, NetworkStatusText, brush, text);
                break;
            case "security":
                SetDot(SecurityStatusDot, SecurityStatusText, brush, text);
                break;
            case "reboot":
                SetDot(RebootStatusDot, RebootStatusText, brush, text);
                break;
            case "sensor":
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
        _refreshTimer.Stop();
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
            string userCommand = BuildUserCommandLabel(result.CommandLine);
            targetTextBox.Text =
                $"Titolo: {result.Title}{Environment.NewLine}" +
                $"Comando: {userCommand}{Environment.NewLine}" +
                $"Exit code: {result.ExitCode}{Environment.NewLine}" +
                $"Esito: {(result.Success ? "OK" : "ERRORE")}{Environment.NewLine}{Environment.NewLine}" +
                result.Output;

            SetToolStatus(
                result.Success ? "OK" : "ERRORE",
                $"{result.Title} {(result.Success ? "eseguito correttamente" : "terminato con problemi")}.",
                userCommand,
                result.Success ? Brushes.LimeGreen : Brushes.OrangeRed,
                Brushes.White);

            AppendLog($"{result.Title} completato. Exit code {result.ExitCode}.");
            FooterStatusText.Text = $"{result.Title} completato.";
        }
        catch (Exception ex)
        {
            string userMessage = SecurityHelpers.ToSafeUserMessage(ex, "Comando terminato con errore.");
            targetTextBox.Text = userMessage;
            SetToolStatus("ERRORE", "Comando terminato con eccezione.", userMessage, Brushes.OrangeRed, Brushes.White);
            AppendLog($"Comando fallito: {ex.GetType().Name} - {ex.Message}");
            FooterStatusText.Text = "Comando terminato con errore.";
        }
        finally
        {
            _toolOperationRunning = false;
            _activeOperationName = null;
            SetButtonsEnabled(true);
            _refreshTimer.Start();
        }
    }

    private async Task ShowFullSystemSpecsAsync()
    {
        if (_diagnosticCts is not null || _toolOperationRunning)
        {
            return;
        }

        _toolOperationRunning = true;
        _activeOperationName = "Specifiche complete";
        _refreshTimer.Stop();
        FooterStatusText.Text = "Raccolta specifiche complete...";
        SystemOutputTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] Raccolta specifiche complete..." + Environment.NewLine;
        SetToolStatus("IN CORSO", "Raccolta specifiche di sistema...", "Specifiche complete", Brushes.DodgerBlue, Brushes.White);
        AppendLog("Raccolta specifiche complete avviata.");
        SetButtonsEnabled(false);

        try
        {
            string report = await Task.Run(_probeService.BuildDetailedReport);
            SystemOutputTextBox.Text = report;
            SetToolStatus("OK", "Specifiche complete generate.", "Specifiche complete", Brushes.LimeGreen, Brushes.White);
            AppendLog("Specifiche complete generate.");
            FooterStatusText.Text = "Specifiche complete aggiornate.";
        }
        catch (Exception ex)
        {
            string userMessage = SecurityHelpers.ToSafeUserMessage(ex, "Raccolta specifiche non riuscita.");
            SystemOutputTextBox.Text = userMessage;
            SetToolStatus("ERRORE", "Errore durante la raccolta specifiche.", userMessage, Brushes.OrangeRed, Brushes.White);
            AppendLog($"Specifiche complete fallite: {ex.GetType().Name} - {ex.Message}");
            FooterStatusText.Text = "Errore durante la raccolta specifiche.";
        }
        finally
        {
            _toolOperationRunning = false;
            _activeOperationName = null;
            SetButtonsEnabled(true);
            _refreshTimer.Start();
        }
    }

    private void SetButtonsEnabled(bool isEnabled)
    {
        RunDiagnosticButton.IsEnabled = isEnabled && _diagnosticCts is null;
        RefreshButton.IsEnabled = isEnabled && _diagnosticCts is null;
        SystemSettingsButton.IsEnabled = isEnabled;
        NetworkSettingsButton.IsEnabled = isEnabled;
        WindowsSecurityButton.IsEnabled = isEnabled;
        InstalledAppsButton.IsEnabled = isEnabled;
        QuickWindowsUpdateButton.IsEnabled = isEnabled;
        FullSystemSpecsButton.IsEnabled = isEnabled;
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
        InstallWindowsUpdateButton.IsEnabled = isEnabled;
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

    private void ResetDiagnosticCategoryStates()
    {
        _diagnosticCategorySeverity.Clear();
    }

    private void ResetDiagnosticSummaryUi()
    {
        _diagnosticSummaryLines.Clear();
        _diagnosticOkCount = 0;
        _diagnosticWarningCount = 0;
        _diagnosticErrorCount = 0;
        DiagnosticOkCountText.Text = "OK 0";
        DiagnosticWarningCountText.Text = "ATT 0";
        DiagnosticErrorCountText.Text = "ERR 0";
        DiagnosticSummaryTextBox.Text = "Controllo in preparazione...";
    }

    private void RegisterDiagnosticSummary(DiagnosticStepResult result)
    {
        switch (result.Status)
        {
            case "OK":
                _diagnosticOkCount++;
                break;
            case "ATTENZIONE":
                _diagnosticWarningCount++;
                break;
            default:
                _diagnosticErrorCount++;
                break;
        }

        DiagnosticOkCountText.Text = $"OK {_diagnosticOkCount}";
        DiagnosticWarningCountText.Text = $"ATT {_diagnosticWarningCount}";
        DiagnosticErrorCountText.Text = $"ERR {_diagnosticErrorCount}";

        string icon = result.Status switch
        {
            "OK" => "[OK]",
            "ATTENZIONE" => "[!]",
            _ => "[X]"
        };

        _diagnosticSummaryLines.Insert(0, $"{icon} {result.Title}: {BuildShortDiagnosticDetail(result)}");
        if (_diagnosticSummaryLines.Count > 6)
        {
            _diagnosticSummaryLines.RemoveAt(_diagnosticSummaryLines.Count - 1);
        }

        UpdateDiagnosticSummaryText();
    }

    private void UpdateDiagnosticSummaryText()
    {
        DiagnosticSummaryTextBox.Text = _diagnosticSummaryLines.Count == 0
            ? "Nessun controllo eseguito."
            : string.Join(Environment.NewLine, _diagnosticSummaryLines);
        DiagnosticSummaryTextBox.ScrollToHome();
    }

    private static string BuildShortDiagnosticDetail(DiagnosticStepResult result)
    {
        string detail = result.Detail?.Trim() ?? string.Empty;
        if (detail.Length <= 72)
        {
            return detail;
        }

        return detail[..69] + "...";
    }

    private static string BuildUserCommandLabel(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return "-";
        }

        string compact = commandLine.Trim();

        if (compact.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
            compact.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase))
        {
            return "PowerShell comando interno";
        }

        if (compact.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            return compact switch
            {
                var s when s.Contains("windowsupdate", StringComparison.OrdinalIgnoreCase) => "Apri Windows Update",
                var s when s.Contains("appsfeatures", StringComparison.OrdinalIgnoreCase) => "Apri app installate",
                var s when s.Contains("network", StringComparison.OrdinalIgnoreCase) => "Apri impostazioni rete",
                var s when s.Contains("about", StringComparison.OrdinalIgnoreCase) => "Apri impostazioni sistema",
                _ => "Apri impostazioni Windows"
            };
        }

        if (compact.StartsWith("windowsdefender:", StringComparison.OrdinalIgnoreCase))
        {
            return "Apri Sicurezza Windows";
        }

        compact = compact
            .Replace("chkdsk.exe", "chkdsk", StringComparison.OrdinalIgnoreCase)
            .Replace("sfc.exe", "sfc", StringComparison.OrdinalIgnoreCase)
            .Replace("DISM.exe", "DISM", StringComparison.OrdinalIgnoreCase)
            .Replace("ipconfig.exe", "ipconfig", StringComparison.OrdinalIgnoreCase)
            .Replace("route.exe", "route", StringComparison.OrdinalIgnoreCase)
            .Replace("ping.exe", "ping", StringComparison.OrdinalIgnoreCase)
            .Replace("nslookup.exe", "nslookup", StringComparison.OrdinalIgnoreCase)
            .Replace("powershell.exe", "PowerShell", StringComparison.OrdinalIgnoreCase);

        if (compact.Length <= 52)
        {
            return compact;
        }

        return compact[..49] + "...";
    }

    private static string? MapDiagnosticCategory(string title) => title switch
    {
        "Sistema" or "Integrita" or "Componenti" or "Servizi" or "Eventi" or "Aggiornamenti" => "system",
        "Disco" => "disk",
        "Rete" or "DNS" or "Firewall" => "network",
        "Sicurezza" or "Definizioni AV" => "security",
        "Riavvio" => "reboot",
        "Sensori" => "sensor",
        _ => null
    };

    private static int GetSeverityRank(string status) => status switch
    {
        "ERRORE" => 2,
        "ATTENZIONE" => 1,
        _ => 0
    };

    private static Brush GetSeverityBrush(string status) => status switch
    {
        "ERRORE" => Brushes.OrangeRed,
        "ATTENZIONE" => Brushes.Goldenrod,
        _ => Brushes.LimeGreen
    };

    private static string BuildQuickStatusMessage(string category, DiagnosticStepResult result) => category switch
    {
        "system" => result.Status switch
        {
            "OK" => "Integrita sistema sotto controllo",
            "ATTENZIONE" => $"Sistema da verificare: {result.Title}",
            _ => $"Problema sistema: {result.Title}"
        },
        "disk" => result.Status switch
        {
            "OK" => "Disco principale sotto controllo",
            "ATTENZIONE" => "Disco con avvisi da controllare",
            _ => "Disco con problemi rilevati"
        },
        "network" => result.Status switch
        {
            "OK" => result.Title == "Firewall" ? "Firewall e rete operativi" : "Rete operativa",
            "ATTENZIONE" => $"Rete da verificare: {result.Title}",
            _ => $"Problema rete: {result.Title}"
        },
        "security" => result.Status switch
        {
            "OK" => result.Title == "Definizioni AV" ? "Protezione e firme aggiornate" : "Protezione Windows attiva",
            "ATTENZIONE" => $"Sicurezza da verificare: {result.Title}",
            _ => $"Problema sicurezza: {result.Title}"
        },
        "reboot" => result.Status switch
        {
            "OK" => "Nessun riavvio richiesto",
            "ATTENZIONE" => "Riavvio richiesto dal sistema",
            _ => "Stato riavvio da verificare"
        },
        "sensor" => result.Status switch
        {
            "OK" => "Sensori temperatura disponibili",
            "ATTENZIONE" => "Sensori disponibili in parte",
            _ => "Sensori non disponibili"
        },
        _ => $"{result.Title}: {result.Status}"
    };

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
