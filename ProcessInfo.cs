namespace SystemProfilerCli;

public class ProcessInfo
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = "";

    public double WorkingSetMb { get; init; }

    public double PrivateMemoryMb { get; init; }

    public int ThreadCount { get; init; }

    public TimeSpan TotalProcessorTime { get; set; }
}
