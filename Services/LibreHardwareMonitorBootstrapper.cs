using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GasparSystemHealth.Services;

public sealed class LibreHardwareMonitorBootstrapper
{
    private const string LatestReleaseApi = "https://api.github.com/repos/LibreHardwareMonitor/LibreHardwareMonitor/releases/latest";
    private readonly string _appRoot;
    private readonly string _packageRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LibreHardwareMonitorBootstrapper(string appRoot)
    {
        _appRoot = appRoot;
        _packageRoot = Path.Combine(appRoot, "LibreHardwareMonitor");
    }

    public bool IsInstalled => File.Exists(Path.Combine(_packageRoot, "LibreHardwareMonitorLib.dll"));

    public async Task<BootstrapResult> EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            return BootstrapResult.AlreadyInstalled(_packageRoot);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsInstalled)
            {
                return BootstrapResult.AlreadyInstalled(_packageRoot);
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GasparSystemHealth", "1.0"));

            string releaseJson = await http.GetStringAsync(LatestReleaseApi, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(releaseJson);

            string? zipUrl = document.RootElement
                .GetProperty("assets")
                .EnumerateArray()
                .Select(asset => new
                {
                    Name = asset.GetProperty("name").GetString(),
                    Url = asset.GetProperty("browser_download_url").GetString()
                })
                .FirstOrDefault(asset =>
                    asset.Name is not null &&
                    asset.Url is not null &&
                    asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?
                .Url;

            if (string.IsNullOrWhiteSpace(zipUrl) || !SecurityHelpers.IsTrustedLibreHardwareMonitorUrl(zipUrl))
            {
                return BootstrapResult.Failed("Pacchetto LibreHardwareMonitor non trovato nella release ufficiale.");
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "GasparSystemHealth", Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(tempRoot, "LibreHardwareMonitor.zip");
            string extractPath = Path.Combine(tempRoot, "extract");

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(extractPath);

            try
            {
                using (HttpResponseMessage response = await http.GetAsync(zipUrl, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using FileStream stream = File.Create(zipPath);
                    await response.Content.CopyToAsync(stream, cancellationToken);
                }

                SecurityHelpers.ExtractZipSafely(zipPath, extractPath);

                string sourceFolder = FindPackageFolder(extractPath);
                if (!File.Exists(Path.Combine(sourceFolder, "LibreHardwareMonitorLib.dll")))
                {
                    return BootstrapResult.Failed("La libreria sensori non e presente nel pacchetto scaricato.");
                }

                if (Directory.Exists(_packageRoot))
                {
                    Directory.Delete(_packageRoot, recursive: true);
                }

                CopyDirectory(sourceFolder, _packageRoot);
                return BootstrapResult.Installed(_packageRoot);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
        catch (Exception ex)
        {
            return BootstrapResult.Failed(SecurityHelpers.ToSafeUserMessage(ex, "Installazione sensori non riuscita."));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string FindPackageFolder(string extractRoot)
    {
        if (File.Exists(Path.Combine(extractRoot, "LibreHardwareMonitorLib.dll")))
        {
            return extractRoot;
        }

        string? nested = Directory
            .EnumerateDirectories(extractRoot, "*", SearchOption.AllDirectories)
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "LibreHardwareMonitorLib.dll")));

        return nested ?? extractRoot;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destination = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            string destination = Path.Combine(targetDir, Path.GetFileName(directory));
            CopyDirectory(directory, destination);
        }
    }
}

public sealed record BootstrapResult(bool Success, bool Downloaded, bool AlreadyPresent, string Message, string? InstallPath)
{
    public static BootstrapResult Installed(string path) =>
        new(true, true, false, "Pacchetto sensori installato correttamente.", path);

    public static BootstrapResult AlreadyInstalled(string path) =>
        new(true, false, true, "Pacchetto sensori gia disponibile.", path);

    public static BootstrapResult Failed(string message) =>
        new(false, false, false, message, null);
}
