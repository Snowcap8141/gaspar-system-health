using System.Diagnostics;
using GasparSystemHealth.Models;

namespace GasparSystemHealth.Services;

public static class CommandRunner
{
    public static async Task<ToolExecutionResult> RunAsync(
        string title,
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
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
}
