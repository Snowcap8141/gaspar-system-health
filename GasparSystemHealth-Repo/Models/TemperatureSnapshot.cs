namespace GasparSystemHealth.Models;

public sealed class TemperatureSnapshot
{
    public static TemperatureSnapshot Empty { get; } = new();

    public double? CpuCelsius { get; init; }
    public double? GpuCelsius { get; init; }
    public string Source { get; init; } = "Non disponibile";
    public string Note { get; init; } = "Sensori non disponibili";
}
