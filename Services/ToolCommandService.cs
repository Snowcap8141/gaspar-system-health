using System.Text;
using GasparSystemHealth.Models;

namespace GasparSystemHealth.Services;

public sealed class ToolCommandService
{
    public Task<ToolExecutionResult> GetIpConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("Configurazione IP", "ipconfig.exe", "/all", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> GetNetworkAdaptersAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-NetAdapter -ErrorAction SilentlyContinue) {
            Get-NetAdapter |
                Sort-Object Name |
                Format-Table -Auto Name, Status, LinkSpeed, MacAddress, InterfaceDescription |
                Out-String -Width 220
        } else {
            ipconfig /all | Out-String
        }
        """;

        return RunPowerShellAsync("Schede di rete", script, cancellationToken);
    }

    public Task<ToolExecutionResult> RunPingTestAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("Ping test", "ping.exe", "1.1.1.1 -n 2", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> RunDnsTestAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("Test DNS", "nslookup.exe", "www.microsoft.com", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> GetFirewallStatusAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-NetFirewallProfile -ErrorAction SilentlyContinue) {
            Get-NetFirewallProfile |
                Format-Table -Auto Name, Enabled, DefaultInboundAction, DefaultOutboundAction |
                Out-String -Width 220
        } else {
            netsh advfirewall show allprofiles | Out-String
        }
        """;

        return RunPowerShellAsync("Stato firewall", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetFirewallRulesAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) {
            Get-NetFirewallRule |
                Select-Object -First 40 DisplayName, Enabled, Direction, Action |
                Format-Table -Auto |
                Out-String -Width 220
        } else {
            netsh advfirewall firewall show rule name=all | Out-String
        }
        """;

        return RunPowerShellAsync("Regole firewall", script, cancellationToken);
    }

    public Task<ToolExecutionResult> CheckWindowsUpdatesAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search("IsInstalled=0 and Type='Software'")
        "Aggiornamenti disponibili: $($result.Updates.Count)"
        ""
        for ($i = 0; $i -lt [Math]::Min($result.Updates.Count, 20); $i++) {
            $u = $result.Updates.Item($i)
            "- $($u.Title)"
        }
        """;

        return RunPowerShellAsync("Controllo aggiornamenti", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetWindowsUpdateHistoryAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $count = $searcher.GetTotalHistoryCount()
        $items = $searcher.QueryHistory(0, [Math]::Min($count, 20))
        foreach ($item in $items) {
            "{0:yyyy-MM-dd HH:mm} | {1}" -f $item.Date, $item.Title
        }
        """;

        return RunPowerShellAsync("Storico aggiornamenti", script, cancellationToken);
    }

    public Task<ToolExecutionResult> OpenWindowsUpdateAsync(CancellationToken cancellationToken = default)
    {
        return LaunchShellTargetAsync("Apri Windows Update", "ms-settings:windowsupdate");
    }

    public Task<ToolExecutionResult> GetAntivirusDefinitionsStatusAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue) {
            $status = Get-MpComputerStatus
            $age = if ($status.AntivirusSignatureLastUpdated) {
                [math]::Round(((Get-Date) - $status.AntivirusSignatureLastUpdated).TotalHours, 1)
            } else {
                -1
            }

            @(
                "Antivirus abilitato: $($status.AntivirusEnabled)"
                "Antispyware abilitato: $($status.AntispywareEnabled)"
                "Definizioni aggiornate: $($status.AntivirusSignatureLastUpdated)"
                "Ore dall'ultimo aggiornamento: $age"
                "Versione firme AV: $($status.AntivirusSignatureVersion)"
                "Versione firme AS: $($status.AntispywareSignatureVersion)"
                "Motore AV: $($status.AMEngineVersion)"
                "Protezione realtime: $($status.RealTimeProtectionEnabled)"
            ) -join [Environment]::NewLine
        } else {
            "Microsoft Defender non disponibile su questo sistema."
        }
        """;

        return RunPowerShellAsync("Definizioni antivirus", script, cancellationToken);
    }

    public Task<ToolExecutionResult> UpdateAntivirusDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Update-MpSignature -ErrorAction SilentlyContinue) {
            Update-MpSignature
            ''
            Get-MpComputerStatus |
                Select-Object AntivirusSignatureLastUpdated, AntivirusSignatureVersion, AMServiceEnabled, RealTimeProtectionEnabled |
                Format-List |
                Out-String -Width 220
        } else {
            'Aggiornamento definizioni non disponibile su questo sistema.'
        }
        """;

        return RunPowerShellAsync("Aggiorna definizioni AV", script, cancellationToken);
    }

    public Task<ToolExecutionResult> RunDefenderQuickScanAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Start-MpScan -ErrorAction SilentlyContinue) {
            Start-MpScan -ScanType QuickScan
            'Scansione rapida Defender avviata.'
        } else {
            'Scansione Defender non disponibile su questo sistema.'
        }
        """;

        return RunPowerShellAsync("Scansione rapida Defender", script, cancellationToken);
    }

    public Task<ToolExecutionResult> RunSfcScanAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("SFC Scan Now", "sfc.exe", "/scannow", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> RunSfcVerifyAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("SFC Verify Only", "sfc.exe", "/verifyonly", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> RunDismCheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("DISM CheckHealth", "DISM.exe", "/Online /Cleanup-Image /CheckHealth", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> RunDismScanHealthAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("DISM ScanHealth", "DISM.exe", "/Online /Cleanup-Image /ScanHealth", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> RunDismRestoreHealthAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("DISM RestoreHealth", "DISM.exe", "/Online /Cleanup-Image /RestoreHealth", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("Informazioni sistema", "systeminfo.exe", string.Empty, cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> GetTopProcessesAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        Get-Process |
            Sort-Object CPU -Descending |
            Select-Object -First 20 ProcessName, Id, CPU, WorkingSet |
            Format-Table -Auto |
            Out-String -Width 220
        """;

        return RunPowerShellAsync("Processi principali", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetCriticalEventsAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        Get-WinEvent -FilterHashtable @{LogName='System'; Level=1,2; StartTime=(Get-Date).AddDays(-2)} -MaxEvents 20 |
            Select-Object TimeCreated, Id, ProviderName, LevelDisplayName, Message |
            Format-List |
            Out-String -Width 220
        """;

        return RunPowerShellAsync("Eventi critici", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetServicesSummaryAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        Get-Service |
            Sort-Object Status, DisplayName |
            Select-Object -First 40 Status, Name, DisplayName |
            Format-Table -Auto |
            Out-String -Width 220
        """;

        return RunPowerShellAsync("Servizi", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetPendingRebootStatusAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        $pending = @()

        if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') {
            $pending += 'CBS RebootPending'
        }

        if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') {
            $pending += 'Windows Update RebootRequired'
        }

        $session = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -ErrorAction SilentlyContinue
        if ($session.PendingFileRenameOperations) {
            $pending += 'PendingFileRenameOperations'
        }

        if ($pending.Count -eq 0) {
            'Nessun riavvio richiesto.'
        } else {
            "Riavvio richiesto per: $($pending -join ', ')"
        }
        """;

        return RunPowerShellAsync("Riavvio richiesto", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetDriverSummaryAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        Get-CimInstance Win32_PnPSignedDriver |
            Sort-Object DeviceName |
            Select-Object -First 40 DeviceName, DriverVersion, DriverProviderName, DriverDate |
            Format-Table -Auto |
            Out-String -Width 220
        """;

        return RunPowerShellAsync("Driver", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetStartupAppsAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        Get-CimInstance Win32_StartupCommand |
            Select-Object Name, Command, Location, User |
            Format-Table -Auto |
            Out-String -Width 220
        """;

        return RunPowerShellAsync("App di avvio", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetScheduledTasksAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue) {
            Get-ScheduledTask |
                Select-Object -First 40 TaskName, TaskPath, State |
                Format-Table -Auto |
                Out-String -Width 220
        } else {
            schtasks /query /fo table | Out-String
        }
        """;

        return RunPowerShellAsync("Attivita pianificate", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetDiskVolumesAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-Volume -ErrorAction SilentlyContinue) {
            Get-Volume |
                Select-Object DriveLetter, FileSystemLabel, FileSystem, SizeRemaining, Size, HealthStatus |
                Format-Table -Auto |
                Out-String -Width 220
        } else {
            Get-PSDrive -PSProvider FileSystem | Format-Table -Auto Name, Used, Free, Root | Out-String -Width 220
        }
        """;

        return RunPowerShellAsync("Volumi disco", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetPhysicalDisksAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-PhysicalDisk -ErrorAction SilentlyContinue) {
            Get-PhysicalDisk |
                Select-Object FriendlyName, MediaType, HealthStatus, OperationalStatus, Size |
                Format-Table -Auto |
                Out-String -Width 220
        } else {
            Get-Disk | Format-Table -Auto Number, FriendlyName, PartitionStyle, HealthStatus, Size | Out-String -Width 220
        }
        """;

        return RunPowerShellAsync("Dischi fisici", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetChkDskInfoAsync(CancellationToken cancellationToken = default)
    {
        return GetChkDskScanAsync(cancellationToken);
    }

    public Task<ToolExecutionResult> GetSmartStatusAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        Get-CimInstance -Namespace root\wmi -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction SilentlyContinue |
            Select-Object InstanceName, PredictFailure, Reason |
            Format-Table -Auto |
            Out-String -Width 220
        """;

        return RunPowerShellAsync("SMART", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetStorageHealthAsync(CancellationToken cancellationToken = default)
    {
        string script = """
        if (Get-Command Get-PhysicalDisk -ErrorAction SilentlyContinue) {
            Get-PhysicalDisk |
                Select-Object FriendlyName, HealthStatus, OperationalStatus, MediaType, Size |
                Format-Table -Auto |
                Out-String -Width 220
        } else {
            Get-Disk |
                Select-Object Number, FriendlyName, HealthStatus, OperationalStatus, Size |
                Format-Table -Auto |
                Out-String -Width 220
        }
        """;

        return RunPowerShellAsync("Salute storage", script, cancellationToken);
    }

    public Task<ToolExecutionResult> GetRouteTableAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("Tabella di routing", "route.exe", "print", cancellationToken: cancellationToken);
    }

    public Task<ToolExecutionResult> FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        return CommandRunner.RunAsync("Flush DNS", "ipconfig.exe", "/flushdns", cancellationToken: cancellationToken);
    }

    private static Task<ToolExecutionResult> RunPowerShellAsync(string title, string script, CancellationToken cancellationToken)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return CommandRunner.RunAsync(
            title,
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            cancellationToken: cancellationToken);
    }

    private static Task<ToolExecutionResult> LaunchShellTargetAsync(string title, string target)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        };

        System.Diagnostics.Process.Start(startInfo);

        return Task.FromResult(new ToolExecutionResult
        {
            Title = title,
            CommandLine = target,
            Output = "Strumento aperto correttamente.",
            ExitCode = 0,
            Success = true
        });
    }

    private static async Task<ToolExecutionResult> GetChkDskScanAsync(CancellationToken cancellationToken)
    {
        ToolExecutionResult result = await CommandRunner.RunAsync(
            "CHKDSK C",
            "chkdsk.exe",
            "C: /scan",
            cancellationToken: cancellationToken);

        bool issuesFound = result.Output.Contains("Sono stati rilevati problemi", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("problems were found", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("non corretta", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("errors found", StringComparison.OrdinalIgnoreCase);

        string summary = issuesFound
            ? "Controllo CHKDSK completato: sono stati rilevati problemi da correggere."
            : "Controllo CHKDSK completato senza problemi evidenti.";

        return new ToolExecutionResult
        {
            Title = result.Title,
            CommandLine = result.CommandLine,
            ExitCode = result.ExitCode,
            Success = result.ExitCode is 0 or 1 or 2 or 3,
            Output = $"{summary}{Environment.NewLine}{Environment.NewLine}{result.Output}"
        };
    }
}
