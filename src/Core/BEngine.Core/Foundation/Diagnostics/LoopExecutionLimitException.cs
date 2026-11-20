namespace BEngine;

public sealed class LoopExecutionLimitException : Exception
{
    public LoopExecutionLimitException(
        string entryPoint,
        string sourcePath,
        int line,
        int iterationLimit)
        : base($"Loop execution in '{entryPoint}' exceeded {iterationLimit} iterations at " +
               $"'{sourcePath}:{line}'. The editor stopped this callback to remain responsive.")
    {
        EntryPoint = entryPoint;
        SourcePath = sourcePath;
        Line = line;
        IterationLimit = iterationLimit;
    }

    public string EntryPoint { get; }
    public string SourcePath { get; }
    public int Line { get; }
    public int IterationLimit { get; }
}
