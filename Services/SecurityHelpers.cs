using System.IO;
using System.IO.Compression;

namespace GasparSystemHealth.Services;

internal static class SecurityHelpers
{
    private static readonly string[] AllowedLibreHardwareMonitorHosts =
    [
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com"
    ];

    private static readonly string[] AllowedShellPrefixes =
    [
        "ms-settings:",
        "windowsdefender:"
    ];

    public static string ToSafeUserMessage(Exception ex, string fallbackMessage)
    {
        return ex switch
        {
            OperationCanceledException => "Operazione annullata o scaduta.",
            UnauthorizedAccessException => "Permessi insufficienti per completare l'operazione.",
            IOException => "Errore di accesso a file o cartelle durante l'operazione.",
            InvalidOperationException when !string.IsNullOrWhiteSpace(ex.Message) => ex.Message,
            _ => fallbackMessage
        };
    }

    public static bool IsTrustedLibreHardwareMonitorUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps &&
               AllowedLibreHardwareMonitorHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    public static void ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            destinationRoot += Path.DirectorySeparatorChar;
        }

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Il pacchetto sensori contiene percorsi non validi.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            string? directoryPath = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    public static bool IsAllowedShellTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        return AllowedShellPrefixes.Any(prefix => target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
