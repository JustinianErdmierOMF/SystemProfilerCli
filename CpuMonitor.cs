namespace SystemProfilerCli;

/// <summary>Platform-specific CPU monitor using PerformanceCounter on Windows and /proc/stat on Linux.</summary>
public sealed class CpuMonitor : IDisposable
{
    private readonly PerformanceCounter? _cpuCounter;

    private bool _disposed;

    private double _lastCpuIdle;

    private double _lastCpuTotal;

    public CpuMonitor()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            _cpuCounter = new PerformanceCounter(categoryName: "Processor", counterName: "% Processor Time", instanceName: "_Total");

            _cpuCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cpuCounter?.Dispose();
    }

    public double GetCpuUsage()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsCpuUsage();
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? GetLinuxCpuUsage() : EstimateCpuFromProcesses();
        }
        catch
        {
            return 0;
        }
    }

    private double GetWindowsCpuUsage()
    {
        if (_cpuCounter == null)
        {
            return EstimateCpuFromProcesses();
        }

        try
        {
        #pragma warning disable CA1416
            float value = _cpuCounter.NextValue();
        #pragma warning restore CA1416

            return Math.Round(value, digits: 1);
        }
        catch
        {
            return EstimateCpuFromProcesses();
        }
    }

    private double GetLinuxCpuUsage()
    {
        if (!File.Exists(path: "/proc/stat"))
        {
            return 0;
        }

        string[] lines   = File.ReadAllLines(path: "/proc/stat");
        string?  cpuLine = lines.FirstOrDefault(l => l.StartsWith(value: "cpu "));

        if (cpuLine == null)
        {
            return 0;
        }

        string[] parts = cpuLine.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 5)
        {
            return 0;
        }

        double user   = double.Parse(s: parts[1]);
        double nice   = double.Parse(s: parts[2]);
        double system = double.Parse(s: parts[3]);
        double idle   = double.Parse(s: parts[4]);
        double iowait = parts.Length > 5 ? double.Parse(s: parts[5]) : 0;

        double total     = user + nice + system + idle + iowait;
        double idleTotal = idle + iowait;

        if (_lastCpuTotal == 0)
        {
            _lastCpuTotal = total;
            _lastCpuIdle  = idleTotal;

            return 0;
        }

        double totalDelta = total - _lastCpuTotal;
        double idleDelta  = idleTotal - _lastCpuIdle;

        _lastCpuTotal = total;
        _lastCpuIdle  = idleTotal;

        if (totalDelta == 0)
        {
            return 0;
        }

        return Math.Round(value: (1.0 - idleDelta / totalDelta) * 100, digits: 1);
    }

    private static double EstimateCpuFromProcesses()
    {
        try
        {
            Process[] processes = Process.GetProcesses();

            int activeCount = processes.Count(p =>
            {
                try
                {
                    return p.Threads.Count > 0;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    p.Dispose();
                }
            });

            int cpuCount = Environment.ProcessorCount;

            return Math.Min(val1: 100, val2: Math.Round(value: activeCount / (double)(cpuCount * 10) * 100, digits: 1));
        }
        catch
        {
            return 0;
        }
    }
}
