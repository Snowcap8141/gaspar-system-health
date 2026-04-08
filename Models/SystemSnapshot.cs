namespace GasparSystemHealth.Models;

public sealed class SystemSnapshot
{
    public string ComputerName { get; init; } = Environment.MachineName;
    public string OperatingSystem { get; init; } = "Windows";
    public string CpuName { get; init; } = "CPU non disponibile";
    public string GpuName { get; init; } = "GPU non disponibile";
    public double CpuUsagePercent { get; init; }
    public double MemoryUsedGb { get; init; }
    public double MemoryTotalGb { get; init; }
    public double MemoryUsedPercent { get; init; }
    public string PrimaryDriveLabel { get; init; } = "C:";
    public double PrimaryDriveUsedPercent { get; init; }
    public double PrimaryDriveFreeGb { get; init; }
    public double PrimaryDriveTotalGb { get; init; }
    public TimeSpan Uptime { get; init; }
    public DateTime SnapshotTime { get; init; } = DateTime.Now;
    public TemperatureSnapshot Temperatures { get; init; } = TemperatureSnapshot.Empty;
}
