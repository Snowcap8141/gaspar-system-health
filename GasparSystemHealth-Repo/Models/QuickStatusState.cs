namespace GasparSystemHealth.Models;

public enum QuickStatusLevel
{
    Neutral,
    Good,
    Warning,
    Error
}

public sealed class QuickStatusState
{
    public required QuickStatusLevel Level { get; init; }
    public required string Message { get; init; }
}
