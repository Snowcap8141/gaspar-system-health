using System.Text;
using System.IO;

namespace GasparSystemHealth.Services;

public sealed class SessionLogger
{
    private readonly object _sync = new();

    public SessionLogger()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GasparSystemHealth");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Logs"));

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        SessionLogPath = Path.Combine(root, "Logs", $"session_{stamp}.log");
        Write("Sessione avviata.");
    }

    public string SessionLogPath { get; }

    public void Write(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_sync)
        {
            try
            {
                File.AppendAllText(SessionLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never terminate the app.
            }
        }
    }
}
