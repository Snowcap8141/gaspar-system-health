using GasparSystemHealth.Models;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GasparSystemHealth.Services;

public sealed class SystemProbeService : IDisposable
{
    private readonly CpuUsageReader _cpuReader = new();
    private readonly LibreHardwareMonitorReader _sensorReader;
    private readonly LibreHardwareMonitorBootstrapper _sensorBootstrapper;

    public SystemProbeService(string appRoot)
    {
        AppRoot = appRoot;
        _sensorReader = new LibreHardwareMonitorReader(appRoot);
        _sensorBootstrapper = new LibreHardwareMonitorBootstrapper(appRoot);
    }

    public string AppRoot { get; }

    public bool SensorsInstalled => _sensorReader.IsInstalled;

    public async Task<BootstrapResult> EnsureSensorSupportAsync(CancellationToken cancellationToken = default)
    {
        BootstrapResult result = await _sensorBootstrapper.EnsureInstalledAsync(cancellationToken);
        if (result.Success && !result.AlreadyPresent)
        {
            _sensorReader.Reset();
        }

        return result;
    }

    public SystemSnapshot CaptureSnapshot()
    {
        DriveInfo primaryDrive = DriveInfo
            .GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .OrderByDescending(d => string.Equals(d.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
            ?? new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);

        double totalGb = primaryDrive.IsReady ? Math.Round(primaryDrive.TotalSize / 1024d / 1024d / 1024d, 1) : 0;
        double freeGb = primaryDrive.IsReady ? Math.Round(primaryDrive.TotalFreeSpace / 1024d / 1024d / 1024d, 1) : 0;
        double usedPercent = totalGb <= 0 ? 0 : Math.Round(((totalGb - freeGb) / totalGb) * 100d, 1);

        MemoryStatusEx memory = GetMemoryStatus();
        double totalMemoryGb = Math.Round(memory.TotalPhys / 1024d / 1024d / 1024d, 1);
        double freeMemoryGb = Math.Round(memory.AvailPhys / 1024d / 1024d / 1024d, 1);
        double usedMemoryGb = Math.Max(0, Math.Round(totalMemoryGb - freeMemoryGb, 1));
        double usedMemoryPercent = totalMemoryGb <= 0 ? 0 : Math.Round((usedMemoryGb / totalMemoryGb) * 100d, 1);

        return new SystemSnapshot
        {
            ComputerName = Environment.MachineName,
            OperatingSystem = ReadOsCaption(),
            CpuName = ReadCpuName(),
            GpuName = ReadGpuName(),
            CpuUsagePercent = _cpuReader.ReadUsagePercent(),
            MemoryTotalGb = totalMemoryGb,
            MemoryUsedGb = usedMemoryGb,
            MemoryUsedPercent = usedMemoryPercent,
            PrimaryDriveLabel = primaryDrive.IsReady ? primaryDrive.Name.TrimEnd('\\') : "C:",
            PrimaryDriveTotalGb = totalGb,
            PrimaryDriveFreeGb = freeGb,
            PrimaryDriveUsedPercent = usedPercent,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            Temperatures = _sensorReader.ReadSnapshot(),
            SnapshotTime = DateTime.Now
        };
    }

    public string BuildDetailedReport()
    {
        SystemSnapshot snapshot = CaptureSnapshot();
        var builder = new StringBuilder();

        builder.AppendLine("=== SPECIFICHE COMPLETE SISTEMA ===");
        builder.AppendLine($"PC: {snapshot.ComputerName}");
        builder.AppendLine($"Sistema operativo: {snapshot.OperatingSystem}");
        builder.AppendLine($"Architettura OS: {(Environment.Is64BitOperatingSystem ? "64 bit" : "32 bit")}");
        builder.AppendLine($"Processori logici: {Environment.ProcessorCount}");
        builder.AppendLine($"Uptime: {FormatUptime(snapshot.Uptime)}");
        builder.AppendLine();

        builder.AppendLine("=== CPU ===");
        builder.AppendLine($"Modello: {snapshot.CpuName}");
        builder.AppendLine($"Carico attuale: {snapshot.CpuUsagePercent:F1}%");
        builder.AppendLine();

        builder.AppendLine("=== MEMORIA ===");
        builder.AppendLine($"RAM totale: {snapshot.MemoryTotalGb:F1} GB");
        builder.AppendLine($"RAM in uso: {snapshot.MemoryUsedGb:F1} GB");
        builder.AppendLine($"RAM utilizzata: {snapshot.MemoryUsedPercent:F1}%");
        builder.AppendLine();

        builder.AppendLine("=== GRAFICA ===");
        builder.AppendLine($"GPU: {snapshot.GpuName}");
        builder.AppendLine();

        builder.AppendLine("=== ARCHIVIAZIONE ===");
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            double totalGb = Math.Round(drive.TotalSize / 1024d / 1024d / 1024d, 1);
            double freeGb = Math.Round(drive.TotalFreeSpace / 1024d / 1024d / 1024d, 1);
            double usedPercent = totalGb <= 0 ? 0 : Math.Round(((totalGb - freeGb) / totalGb) * 100d, 1);
            builder.AppendLine($"{drive.Name.TrimEnd('\\')}: {freeGb:F1} GB liberi su {totalGb:F1} GB ({usedPercent:F1}% usato)");
        }
        builder.AppendLine();

        builder.AppendLine("=== TEMPERATURE ===");
        builder.AppendLine($"CPU: {(snapshot.Temperatures.CpuCelsius.HasValue ? $"{snapshot.Temperatures.CpuCelsius:F1} C" : "N/A")}");
        builder.AppendLine($"GPU: {(snapshot.Temperatures.GpuCelsius.HasValue ? $"{snapshot.Temperatures.GpuCelsius:F1} C" : "N/A")}");
        builder.AppendLine($"Sorgente sensori: {snapshot.Temperatures.Source}");
        if (!string.IsNullOrWhiteSpace(snapshot.Temperatures.Note))
        {
            builder.AppendLine($"Nota sensori: {snapshot.Temperatures.Note}");
        }
        builder.AppendLine();

        builder.AppendLine("=== SESSIONE ===");
        builder.AppendLine($"Snapshot generato: {snapshot.SnapshotTime:yyyy-MM-dd HH:mm:ss}");

        return builder.ToString();
    }

    private static string ReadOsCaption()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null)
        {
            return Environment.OSVersion.VersionString;
        }

        string productName = key.GetValue("ProductName")?.ToString() ?? "Windows";
        string build = key.GetValue("CurrentBuildNumber")?.ToString() ?? "?";
        return $"{productName} | Build {build}";
    }

    private static string ReadCpuName()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "CPU non disponibile";
    }

    private static string ReadGpuName()
    {
        using RegistryKey? videoRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
        if (videoRoot is null)
        {
            return "GPU non disponibile";
        }

        foreach (string adapterKeyName in videoRoot.GetSubKeyNames())
        {
            using RegistryKey? adapterRoot = videoRoot.OpenSubKey(adapterKeyName);
            if (adapterRoot is null)
            {
                continue;
            }

            foreach (string childName in adapterRoot.GetSubKeyNames())
            {
                using RegistryKey? child = adapterRoot.OpenSubKey(childName);
                string? desc = child?.GetValue("DriverDesc")?.ToString();
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    return desc.Trim();
                }
            }
        }

        return "GPU non disponibile";
    }

    public void Dispose()
    {
        _sensorReader.Dispose();
    }

    private static MemoryStatusEx GetMemoryStatus()
    {
        var memory = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memory))
        {
            return memory;
        }

        return memory;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}g {uptime.Hours}h {uptime.Minutes}m";
        }

        return $"{uptime.Hours}h {uptime.Minutes}m";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        }

        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
