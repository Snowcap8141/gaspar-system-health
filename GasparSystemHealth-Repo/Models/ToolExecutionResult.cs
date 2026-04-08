namespace GasparSystemHealth.Models;

public sealed class ToolExecutionResult
{
    public required string Title { get; init; }
    public required string CommandLine { get; init; }
    public required string Output { get; init; }
    public int ExitCode { get; init; }
    public bool Success { get; init; }
}
