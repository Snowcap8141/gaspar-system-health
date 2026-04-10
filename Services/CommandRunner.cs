using System.Diagnostics;
using System.IO;
using GasparSystemHealth.Models;

namespace GasparSystemHealth.Services;

public static class CommandRunner
{
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell.exe",
        "ipconfig.exe",
        "ping.exe",
        "nslookup.exe",
        "sfc.exe",
        "dism.exe",
        "systeminfo.exe",
        "route.exe",
        "chkdsk.exe",
        "explorer.exe",
        "sc.exe"
    };

    public static async Task<ToolExecutionResult> RunAsync(
        string title,
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        string executableName = Path.GetFileName(fileName);
        if (!AllowedExecutables.Contains(executableName))
        {
            throw new InvalidOperationException("Eseguibile non consentito.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            string output = await outputTask;
            string error = await errorTask;

            if (!string.IsNullOrWhiteSpace(error))
            {
                output = string.IsNullOrWhiteSpace(output) ? error : output + Environment.NewLine + error;
            }

            string finalOutput = output.Trim();

            return new ToolExecutionResult
            {
                Title = title,
                CommandLine = $"{fileName} {arguments}".Trim(),
                Output = string.IsNullOrWhiteSpace(finalOutput) ? "Nessun output disponibile." : finalOutput,
                ExitCode = process.ExitCode,
                Success = process.ExitCode == 0
            };
        }
        catch (OperationCanceledException)
        {
            TryTerminateProcess(process);
            throw;
        }
        catch
        {
            TryTerminateProcess(process);
            throw;
        }
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // Best effort cleanup of timed-out processes.
        }
    }
}
