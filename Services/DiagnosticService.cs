using System.Net.NetworkInformation;
using System.Net;
using System.Diagnostics;
using GasparSystemHealth.Models;

namespace GasparSystemHealth.Services;

public sealed class DiagnosticService
{
    private readonly SystemProbeService _probeService;
    private readonly SessionLogger _logger;
    private const int StepRenderDelayMs = 140;
    private const int DefaultCommandTimeoutSeconds = 180;

    public DiagnosticService(SystemProbeService probeService, SessionLogger logger)
    {
        _probeService = probeService;
        _logger = logger;
    }

    public async Task RunAsync(
        IProgress<(int percent, string title, string detail)> progress,
        Action<DiagnosticStepResult> onStep,
        CancellationToken cancellationToken)
    {
        _logger.Write("Controllo automatico avviato.");

        await ExecuteStepAsync(8, "Sistema", "Raccolta informazioni di base.", 5, async () =>
        {
            SystemSnapshot snapshot = _probeService.CaptureSnapshot();
            string detail = $"{snapshot.OperatingSystem} | CPU {snapshot.CpuName}";
            return new DiagnosticStepResult { Title = "Sistema", Detail = detail, Status = "OK", Success = true };
        });

        await ExecuteStepAsync(18, "Disco", "Controllo spazio disponibile.", 5, async () =>
        {
            SystemSnapshot snapshot = _probeService.CaptureSnapshot();
            bool ok = snapshot.PrimaryDriveUsedPercent < 90;
            string status = ok ? "OK" : "ATTENZIONE";
            string detail = $"{snapshot.PrimaryDriveLabel} libero {snapshot.PrimaryDriveFreeGb:F1} GB su {snapshot.PrimaryDriveTotalGb:F1} GB";
            return new DiagnosticStepResult { Title = "Disco", Detail = detail, Status = status, Success = ok };
        });

        await ExecuteStepAsync(28, "Rete", "Ping verso rete esterna.", 6, async () =>
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync("1.1.1.1", 1500);
            bool ok = reply.Status == IPStatus.Success;
            string status = ok ? "OK" : "ERRORE";
            string detail = ok ? $"Ping Cloudflare {reply.RoundtripTime} ms" : $"Ping fallito: {reply.Status}";
            return new DiagnosticStepResult { Title = "Rete", Detail = detail, Status = status, Success = ok };
        });

        await ExecuteStepAsync(38, "DNS", "Verifica risoluzione DNS.", 6, async () =>
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync("www.microsoft.com", cancellationToken);
            bool ok = addresses.Length > 0;
            string detail = ok ? $"Risoluzione DNS attiva: {addresses[0]}" : "Nessun indirizzo DNS restituito";
            return new DiagnosticStepResult
            {
                Title = "DNS",
                Detail = detail,
                Status = ok ? "OK" : "ERRORE",
                Success = ok
            };
        });

        await ExecuteStepAsync(50, "Integrita", "Verifica file di sistema (SFC).", DefaultCommandTimeoutSeconds, async () =>
        {
            return await RunCommandDiagnosticStepAsync(
                "Integrita",
                "sfc.exe",
                "/verifyonly",
                successDetails: ["Windows Resource Protection did not find any integrity violations"],
                warningDetails: ["Windows Resource Protection found integrity violations"],
                fallbackSuccessMessage: "Verifica SFC completata senza errori segnalati",
                fallbackWarningMessage: "Verifica SFC completata con elementi da controllare",
                timeoutSeconds: DefaultCommandTimeoutSeconds,
                cancellationToken: cancellationToken);
        });

        await ExecuteStepAsync(62, "Componenti", "Verifica integrita component store (DISM).", DefaultCommandTimeoutSeconds, async () =>
        {
            return await RunCommandDiagnosticStepAsync(
                "Componenti",
                "DISM.exe",
                "/Online /Cleanup-Image /ScanHealth",
                successDetails: ["No component store corruption detected", "Nessun danneggiamento del component store rilevato"],
                warningDetails: ["The component store is repairable", "Il component store e ripristinabile"],
                fallbackSuccessMessage: "Verifica DISM completata senza errori",
                fallbackWarningMessage: "Verifica DISM completata con avvisi",
                timeoutSeconds: DefaultCommandTimeoutSeconds,
                cancellationToken: cancellationToken);
        });

        await ExecuteStepAsync(72, "Firewall", "Verifica servizio firewall.", 5, async () =>
        {
            bool ok = ServiceControllerStatus("mpssvc");
            return new DiagnosticStepResult
            {
                Title = "Firewall",
                Detail = ok ? "Servizio firewall attivo." : "Servizio firewall non attivo.",
                Status = ok ? "OK" : "ATTENZIONE",
                Success = ok
            };
        });

        await ExecuteStepAsync(80, "Sicurezza", "Verifica Windows Defender.", 5, async () =>
        {
            bool ok = ServiceControllerStatus("WinDefend");
            return new DiagnosticStepResult
            {
                Title = "Sicurezza",
                Detail = ok ? "Windows Defender attivo." : "Windows Defender non attivo o sostituito.",
                Status = ok ? "OK" : "ATTENZIONE",
                Success = ok
            };
        });

        await ExecuteStepAsync(84, "Definizioni AV", "Controllo aggiornamento firme antivirus.", 8, async () =>
        {
            return await CheckAntivirusDefinitionsAsync(cancellationToken);
        });

        await ExecuteStepAsync(88, "Servizi", "Verifica servizi critici Windows.", 5, async () =>
        {
            string[] criticalServices = ["EventLog", "Schedule", "W32Time"];
            string[] missing = criticalServices.Where(service => !ServiceControllerStatus(service)).ToArray();
            bool ok = missing.Length == 0;
            string detail = ok
                ? "Servizi critici attivi"
                : $"Servizi non attivi: {string.Join(", ", missing)}";
            return new DiagnosticStepResult
            {
                Title = "Servizi",
                Detail = detail,
                Status = ok ? "OK" : "ATTENZIONE",
                Success = ok
            };
        });

        await ExecuteStepAsync(92, "Eventi", "Analisi errori recenti di sistema.", 7, async () =>
        {
            int count = CountRecentCriticalEvents();
            bool ok = count == 0;
            string detail = ok ? "Nessun errore critico recente" : $"{count} eventi critici recenti rilevati";
            return new DiagnosticStepResult
            {
                Title = "Eventi",
                Detail = detail,
                Status = ok ? "OK" : "ATTENZIONE",
                Success = ok
            };
        });

        await ExecuteStepAsync(96, "Aggiornamenti", "Ricerca aggiornamenti disponibili.", 45, async () =>
        {
            UpdateCheckResult result = await CheckWindowsUpdatesAsync(cancellationToken);
            bool ok = result.AvailableCount == 0 && result.ServiceReady;
            string detail = !result.ServiceReady
                ? "Servizi aggiornamento da verificare"
                : result.AvailableCount == 0
                    ? "Nessun aggiornamento disponibile"
                    : $"{result.AvailableCount} aggiornamenti disponibili";
            return new DiagnosticStepResult
            {
                Title = "Aggiornamenti",
                Detail = detail,
                Status = ok ? "OK" : (result.ServiceReady ? "ATTENZIONE" : "ERRORE"),
                Success = ok
            };
        });

        await ExecuteStepAsync(99, "Sensori", "Lettura temperature e sorgente.", 6, async () =>
        {
            SystemSnapshot snapshot = _probeService.CaptureSnapshot();
            bool ok = snapshot.Temperatures.CpuCelsius.HasValue || snapshot.Temperatures.GpuCelsius.HasValue;
            string detail = $"CPU {(snapshot.Temperatures.CpuCelsius?.ToString("F1") ?? "N/A")} C | GPU {(snapshot.Temperatures.GpuCelsius?.ToString("F1") ?? "N/A")} C | {snapshot.Temperatures.Source}";
            return new DiagnosticStepResult { Title = "Sensori", Detail = detail, Status = ok ? "OK" : "ATTENZIONE", Success = ok };
        });

        progress.Report((100, "Completato", "Controllo completo terminato."));
        await Task.Delay(StepRenderDelayMs, cancellationToken);
        _logger.Write("Controllo automatico completato.");

        async Task ExecuteStepAsync(
            int percent,
            string title,
            string detail,
            int timeoutSeconds,
            Func<Task<DiagnosticStepResult>> action)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report((percent, title, detail));
            _logger.Write($"Step diagnostica: {title} - {detail}");
            await Task.Delay(StepRenderDelayMs, cancellationToken);
            DiagnosticStepResult result = await RunStepSafeAsync(title, action, timeoutSeconds, cancellationToken);
            onStep(result);
            _logger.Write($"Esito {title}: {result.Status} - {result.Detail}");
        }
    }

    private static int CountRecentCriticalEvents()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"@(Get-WinEvent -FilterHashtable @{LogName='System'; Level=1,2; StartTime=(Get-Date).AddDays(-1)} -MaxEvents 20).Count\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return 0;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);

            return int.TryParse(output.Trim(), out int count) ? count : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<DiagnosticStepResult> RunStepSafeAsync(
        string title,
        Func<Task<DiagnosticStepResult>> action,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<DiagnosticStepResult> actionTask = action();
            Task delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeoutCts.Token);
            Task completed = await Task.WhenAny(actionTask, delayTask);

            if (completed == delayTask)
            {
                return new DiagnosticStepResult
                {
                    Title = title,
                    Detail = $"Tempo massimo superato ({timeoutSeconds}s)",
                    Status = "ERRORE",
                    Success = false
                };
            }

            timeoutCts.Cancel();
            return await actionTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DiagnosticStepResult
            {
                Title = title,
                Detail = ex.Message,
                Status = "ERRORE",
                Success = false
            };
        }
    }

    private static async Task<DiagnosticStepResult> RunCommandDiagnosticStepAsync(
        string title,
        string fileName,
        string arguments,
        IEnumerable<string> successDetails,
        IEnumerable<string> warningDetails,
        string fallbackSuccessMessage,
        string fallbackWarningMessage,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        ToolExecutionResult result = await CommandRunner.RunAsync(
            title,
            fileName,
            arguments,
            cancellationToken: timeoutCts.Token);

        string output = result.Output ?? string.Empty;
        bool warning = warningDetails.Any(detail => output.Contains(detail, StringComparison.OrdinalIgnoreCase));
        bool successByText = successDetails.Any(detail => output.Contains(detail, StringComparison.OrdinalIgnoreCase));
        bool success = result.Success && !warning;

        string detail = successByText
            ? fallbackSuccessMessage
            : warning
                ? fallbackWarningMessage
                : result.Success
                    ? fallbackSuccessMessage
                    : $"Comando terminato con codice {result.ExitCode}";

        return new DiagnosticStepResult
        {
            Title = title,
            Detail = detail,
            Status = success ? "OK" : (warning ? "ATTENZIONE" : "ERRORE"),
            Success = success
        };
    }

    private static async Task<UpdateCheckResult> CheckWindowsUpdatesAsync(CancellationToken cancellationToken)
    {
        bool serviceReady = ServiceExists("wuauserv") && ServiceExists("BITS");
        if (!serviceReady)
        {
            return new UpdateCheckResult(false, 0);
        }

        ToolExecutionResult result = await CommandRunner.RunAsync(
            "Controllo aggiornamenti",
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"$session = New-Object -ComObject Microsoft.Update.Session; $searcher = $session.CreateUpdateSearcher(); $r = $searcher.Search(\\\"IsInstalled=0 and Type='Software'\\\"); Write-Output $r.Updates.Count\"",
            cancellationToken: cancellationToken);

        if (int.TryParse(result.Output.Trim(), out int count))
        {
            return new UpdateCheckResult(true, count);
        }

        return new UpdateCheckResult(true, 0);
    }

    private static async Task<DiagnosticStepResult> CheckAntivirusDefinitionsAsync(CancellationToken cancellationToken)
    {
        ToolExecutionResult result = await CommandRunner.RunAsync(
            "Definizioni antivirus",
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue) { $s = Get-MpComputerStatus; $h = if ($s.AntivirusSignatureLastUpdated) { [math]::Round(((Get-Date)-$s.AntivirusSignatureLastUpdated).TotalHours,1) } else { 9999 }; Write-Output \\\"$h|$($s.AntivirusSignatureVersion)|$($s.RealTimeProtectionEnabled)\\\" } else { Write-Output 'NA|NA|False' }\"",
            cancellationToken: cancellationToken);

        string[] parts = result.Output.Trim().Split('|');
        if (parts.Length < 3 || parts[0] == "NA")
        {
            return new DiagnosticStepResult
            {
                Title = "Definizioni AV",
                Detail = "Microsoft Defender non disponibile",
                Status = "ATTENZIONE",
                Success = false
            };
        }

        _ = double.TryParse(parts[0], out double ageHours);
        string version = parts[1];
        bool realtime = bool.TryParse(parts[2], out bool parsedRealtime) && parsedRealtime;
        bool fresh = ageHours <= 48;
        bool ok = fresh && realtime;

        string detail = fresh
            ? $"Firme aggiornate ({ageHours:F1}h) - v{version}"
            : $"Firme datate ({ageHours:F1}h) - v{version}";

        return new DiagnosticStepResult
        {
            Title = "Definizioni AV",
            Detail = realtime ? detail : $"{detail} | realtime disattivato",
            Status = ok ? "OK" : "ATTENZIONE",
            Success = ok
        };
    }

    private readonly record struct UpdateCheckResult(bool ServiceReady, int AvailableCount);

    public QuickStatusState EvaluateSystem(SystemSnapshot snapshot)
    {
        bool ok = !string.IsNullOrWhiteSpace(snapshot.OperatingSystem)
            && !snapshot.CpuName.Contains("non disponibile", StringComparison.OrdinalIgnoreCase);

        return new QuickStatusState
        {
            Level = ok ? QuickStatusLevel.Good : QuickStatusLevel.Warning,
            Message = ok ? "Sistema e CPU rilevati correttamente" : "Sistema rilevato solo in parte"
        };
    }

    public QuickStatusState EvaluateDisk(SystemSnapshot snapshot)
    {
        if (snapshot.PrimaryDriveUsedPercent >= 90)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Error,
                Message = $"Disco quasi pieno: {snapshot.PrimaryDriveFreeGb:F1} GB liberi"
            };
        }

        if (snapshot.PrimaryDriveUsedPercent >= 75)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Warning,
                Message = $"Disco in attenzione: {snapshot.PrimaryDriveFreeGb:F1} GB liberi"
            };
        }

        return new QuickStatusState
        {
            Level = QuickStatusLevel.Good,
            Message = $"Disco sotto controllo: {snapshot.PrimaryDriveFreeGb:F1} GB liberi"
        };
    }

    public async Task<QuickStatusState> EvaluateNetworkAsync(CancellationToken cancellationToken = default)
    {
        bool pingOk = false;
        bool dnsOk = false;

        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync("1.1.1.1", 1500);
            pingOk = reply.Status == IPStatus.Success;
        }
        catch
        {
            pingOk = false;
        }

        try
        {
            _ = await Dns.GetHostAddressesAsync("www.microsoft.com", cancellationToken);
            dnsOk = true;
        }
        catch
        {
            dnsOk = false;
        }

        if (pingOk && dnsOk)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Good,
                Message = "Rete OK: ping e DNS funzionano"
            };
        }

        if (!pingOk && !dnsOk)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Error,
                Message = "Rete KO: ping e DNS falliti"
            };
        }

        return new QuickStatusState
        {
            Level = QuickStatusLevel.Error,
            Message = pingOk ? "DNS non funzionante" : "Ping non funzionante"
        };
    }

    public QuickStatusState EvaluateSecurity()
    {
        bool firewallOk = ServiceControllerStatus("mpssvc");
        bool defenderOk = ServiceControllerStatus("WinDefend");

        if (firewallOk && defenderOk)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Good,
                Message = "Firewall e protezione base attivi"
            };
        }

        if (!firewallOk)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Error,
                Message = "Firewall non attivo"
            };
        }

        return new QuickStatusState
        {
            Level = QuickStatusLevel.Warning,
            Message = "Protezione Windows non attiva"
        };
    }

    public QuickStatusState EvaluateSensors(SystemSnapshot snapshot)
    {
        bool hasCpu = snapshot.Temperatures.CpuCelsius.HasValue;
        bool hasGpu = snapshot.Temperatures.GpuCelsius.HasValue;

        if (hasCpu && hasGpu)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Good,
                Message = "Sensori CPU e GPU disponibili"
            };
        }

        if (hasCpu || hasGpu)
        {
            return new QuickStatusState
            {
                Level = QuickStatusLevel.Warning,
                Message = "Sensori disponibili in parte"
            };
        }

        return new QuickStatusState
        {
            Level = QuickStatusLevel.Error,
            Message = "Sensori temperatura non disponibili"
        };
    }

    private static bool ServiceControllerStatus(string serviceName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
