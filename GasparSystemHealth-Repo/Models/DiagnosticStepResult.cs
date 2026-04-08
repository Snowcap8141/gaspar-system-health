namespace GasparSystemHealth.Models;

public sealed class DiagnosticStepResult
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Status { get; init; }
    public bool Success { get; init; }
}
