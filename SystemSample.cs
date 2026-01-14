namespace SystemProfilerCli;

public class SystemSample
{
    public int SampleNumber { get; set; }

    public DateTime Timestamp { get; set; }

    public double OverallCpuPercent { get; set; }

    public double TotalMemoryMb { get; set; }

    public double UsedMemoryMb { get; set; }

    public double AvailableMemoryMb { get; set; }

    public double MemoryUsagePercent { get; set; }

    public List<ProcessInfo> Processes { get; set; } = [];
}
