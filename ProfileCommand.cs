using JetBrains.Annotations;

namespace SystemProfilerCli;

[ UsedImplicitly ]
public sealed class ProfileCommand : AsyncCommand<ProfileCommand.Settings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Ensure the directory exists
        string? directory = Path.GetDirectoryName(Settings.DirectoryPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception)
            {
                // AnsiConsole.MarkupLine(value: $"[red]Error:[/] Could not create directory '[yellow]{directory}[/]': {ex.Message}");

                AnsiConsole.WriteException(exception);

                return 1;
            }
        }

        AnsiConsole.Clear();

        // Display header
        Rule rule = new Rule(title: "[bold blue]System Profiler[/]").RuleStyle(style: "blue");

        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine(format: "[grey]Starting profiler... Press [bold yellow]Ctrl+C[/] to stop early.[/]", Justify.Center);
        AnsiConsole.WriteLine();

        try
        {
            await RunProfiler(settings);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(value: $"[green]✓[/] Profiling complete. Results saved to: [link]{Settings.GetGeneratedFilePath()}[/]");

            return 0;
        }
        catch (Exception exception)
        {
            AnsiConsole.WriteException(exception);

            return 1;
        }
    }

    private static async Task RunProfiler(Settings settings)
    {
        int durationSeconds = settings.Duration;
        int rateSeconds     = settings.Rate;

        List<SystemSample> samples     = [];
        Stopwatch          stopwatch   = Stopwatch.StartNew();
        TimeSpan           endTime     = TimeSpan.FromSeconds(durationSeconds);
        int                sampleCount = 0;

        // Initialise the CPU monitor (platform-specific)
        using CpuMonitor cpuMonitor = new();

        // Allow initial reading to settle
        cpuMonitor.GetCpuUsage();

        await Task.Delay(millisecondsDelay: 500);

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "macOS";

        Grid infoGrid = new Grid().Width(width: 50)
                                  .AddColumns(count: 2)
                                  .AddRow("[grey]User:[/]", $"[white]{Environment.UserName}[/]")
                                  .AddRow("[grey]Machine:[/]", $"[white]{Environment.MachineName}[/]")
                                  .AddRow("[grey]Platform:[/]", $"[white]{platform}[/]")
                                  .AddRow("[grey]Processors:[/]", $"[white]{Environment.ProcessorCount}[/]")
                                  .AddRow("[grey]Duration:[/]", $"[white]{settings.Duration} seconds[/]")
                                  .AddRow("[grey]Sample Rate:[/]", $"[white]Every {settings.Rate} second(s)[/]");

        // Create a live display table
        Table metricsTable = new Table().Border(TableBorder.Rounded)
                                        .BorderColor(Color.Grey)
                                        .AddColumn(column: new TableColumn(header: "[bold]Metric[/]").Width(width: 25))
                                        .AddColumn(column: new TableColumn(header: "[bold]Value[/]").Centered()
                                                                                                    .Width(width: 25))
                                        .AddColumn(column: new TableColumn(header: "[bold]Status[/]").Centered()
                                                                                                     .Width(width: 25));

        Grid layoutGrid = new Grid().Expand()
                                    .AddColumns(new GridColumn(), new GridColumn().Alignment(Justify.Right))
                                    .AddRow(infoGrid, Align.Center(metricsTable));

        await AnsiConsole.Live(layoutGrid)
                         .AutoClear(enabled: false)
                         .Overflow(VerticalOverflow.Ellipsis)
                         .StartAsync(async ctx =>
                         {
                             while (stopwatch.Elapsed < endTime)
                             {
                                 sampleCount++;

                                 SystemSample sample = CollectSample(sampleCount, cpuMonitor);

                                 samples.Add(sample);

                                 // Update the live table
                                 UpdateTable(metricsTable, sample, stopwatch.Elapsed, endTime);

                                 ctx.Refresh();

                                 // Wait for the next interval (accounting for collection time)
                                 TimeSpan nextSampleTime = TimeSpan.FromSeconds(seconds: sampleCount * rateSeconds);
                                 TimeSpan waitTime       = nextSampleTime - stopwatch.Elapsed;

                                 if (waitTime > TimeSpan.Zero && stopwatch.Elapsed < endTime)
                                 {
                                     await Task.Delay(waitTime);
                                 }
                             }

                             // Update the table once more to get a final sample and set the live table to 100%.
                             SystemSample finalSample = CollectSample(sampleCount + 1, cpuMonitor);

                             samples.Add(finalSample);

                             UpdateTable(metricsTable, finalSample, stopwatch.Elapsed, endTime, isFinalUpdate: true);
                         });

        AnsiConsole.WriteLine();

        Rule rule = new Rule().RuleStyle(style: "blue");

        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        // Show final summary
        DisplaySummary(samples);

        // Write results to the log file
        WriteLogFile(samples);
    }

    private static void UpdateTable(Table table, SystemSample sample, TimeSpan elapsed, TimeSpan total, bool isFinalUpdate = false)
    {
        table.Rows.Clear();

        double progress = isFinalUpdate ? 100 : elapsed.TotalSeconds / total.TotalSeconds * 100;

        string cpuColour = sample.OverallCpuPercent switch
        {
            > 80  => "red",
            > 50  => "yellow",
            var _ => "green"
        };

        string memColour = sample.MemoryUsagePercent switch
        {
            > 80  => "red",
            > 50  => "yellow",
            var _ => "green"
        };

        table.AddRow("[blue]Sample[/]", $"#{sample.SampleNumber}", $"[grey]{sample.Timestamp:HH:mm:ss}[/]")
             .AddRow("[blue]Progress[/]", $"{progress:F0}%", CreateProgressBar(progress))
             .AddRow("[blue]CPU[/]", $"[{cpuColour}]{sample.OverallCpuPercent:F1}%[/]", CreateProgressBar(sample.OverallCpuPercent, cpuColour))
             .AddRow("[blue]Memory[/]", $"[{memColour}]{sample.UsedMemoryMb:F0} / {sample.TotalMemoryMb:F0} MB[/]", CreateProgressBar(sample.MemoryUsagePercent, memColour));

        // Add top 3 processes
        table.AddRow("[grey bold]Top Processes ───────────[/]", "[grey bold]─────────────────────────[/]", "[grey bold]─────────────────────────[/]");

        int rank = 1;

        foreach (ProcessInfo proc in sample.Processes.Take(count: 3))
        {
            string medal = rank switch
            {
                1     => "(1)",
                2     => "(2)",
                3     => "(3)",
                var _ => "  "
            };

            table.AddRow($"{medal} [white]{proc.ProcessName.Truncate(maxLength: 20)}[/]", $"[grey]{proc.WorkingSetMb:F0} MB[/]", $"[grey]{proc.ThreadCount} threads[/]");

            rank++;
        }
    }

    private static string CreateProgressBar(double percentage, string colour = "blue")
    {
        int filled = (int)(percentage / 5);
        int empty  = 20 - filled;

        filled = Math.Max(val1: 0, val2: Math.Min(val1: 20, filled));
        empty  = Math.Max(val1: 0, val2: Math.Min(val1: 20, empty));

        return $"[{colour}]{new string(c: '█', filled)}[/][grey]{new string(c: '░', empty)}[/]";
    }

    private static void DisplaySummary(List<SystemSample> samples)
    {
        if (samples.Count == 0)
        {
            return;
        }

        Table summaryTable = new Table().Border(TableBorder.Rounded)
                                        .BorderColor(Color.Blue)
                                        .Title(text: "[bold blue]Summary[/]")
                                        .AddColumn(column: new TableColumn(header: "[bold]Metric[/]"))
                                        .AddColumn(column: new TableColumn(header: "[bold]Min[/]").Centered())
                                        .AddColumn(column: new TableColumn(header: "[bold]Avg[/]").Centered())
                                        .AddColumn(column: new TableColumn(header: "[bold]Max[/]").Centered());

        double avgCpu = samples.Average(s => s.OverallCpuPercent);
        double maxCpu = samples.Max(s => s.OverallCpuPercent);
        double minCpu = samples.Min(s => s.OverallCpuPercent);
        double avgMem = samples.Average(s => s.MemoryUsagePercent);
        double maxMem = samples.Max(s => s.MemoryUsagePercent);
        double minMem = samples.Min(s => s.MemoryUsagePercent);

        summaryTable.AddRow("[blue]CPU Usage[/]", $"{minCpu:F1}%", $"[yellow]{avgCpu:F1}%[/]", $"[red]{maxCpu:F1}%[/]")
                    .AddRow("[blue]Memory Usage[/]", $"{minMem:F1}%", $"[yellow]{avgMem:F1}%[/]", $"[red]{maxMem:F1}%[/]");

        // Top processes table
        var processStats = samples.SelectMany(s => s.Processes)
                                  .GroupBy(p => p.ProcessName)
                                  .Select(g => new
                                  {
                                      Name        = g.Key,
                                      AvgMemoryMB = g.Average(p => p.WorkingSetMb),
                                      MaxMemoryMB = g.Max(p => p.WorkingSetMb),
                                      AvgThreads  = g.Average(p => p.ThreadCount)
                                  })
                                  .OrderByDescending(p => p.AvgMemoryMB)
                                  .Take(count: 10)
                                  .ToList();

        Table processTable = new Table().Border(TableBorder.Rounded)
                                        .BorderColor(Color.Green)
                                        .Title(text: "[bold green]Top Processes by Memory[/]")
                                        .AddColumn(column: new TableColumn(header: "[bold]Process[/]"))
                                        .AddColumn(column: new TableColumn(header: "[bold]Avg Memory[/]").RightAligned())
                                        .AddColumn(column: new TableColumn(header: "[bold]Max Memory[/]").RightAligned())
                                        .AddColumn(column: new TableColumn(header: "[bold]Avg Threads[/]").Centered());

        foreach (var proc in processStats)
        {
            processTable.AddRow($"[white]{proc.Name.Truncate(maxLength: 25)}[/]", $"{proc.AvgMemoryMB:F1} MB", $"[yellow]{proc.MaxMemoryMB:F1} MB[/]", $"{proc.AvgThreads:F0}");
        }

        Grid layoutGrid = new Grid().Expand()
                                    .AddColumns(count: 2)
                                    .AddRow(summaryTable.Centered(), processTable.Centered());

        AnsiConsole.Write(renderable: Align.Center(layoutGrid));
        AnsiConsole.WriteLine();
    }

    private static SystemSample CollectSample(int sampleNumber, CpuMonitor cpuMonitor)
    {
        DateTime  timestamp = DateTime.Now;
        Process[] processes = Process.GetProcesses();

        // Get CPU usage from a platform-specific monitor
        double cpuUsage = cpuMonitor.GetCpuUsage();

        // Get memory info
        (double TotalMB, double UsedMB, double AvailableMB, double UsagePercent) memoryInfo = GetMemoryInfo();

        // Get per-process info
        var processInfos = new List<ProcessInfo>();

        foreach (Process proc in processes)
        {
            try
            {
                ProcessInfo info = new()
                {
                    ProcessId       = proc.Id,
                    ProcessName     = proc.ProcessName,
                    WorkingSetMb    = proc.WorkingSet64 / (1024.0 * 1024.0),
                    PrivateMemoryMb = proc.PrivateMemorySize64 / (1024.0 * 1024.0),
                    ThreadCount     = proc.Threads.Count
                };

                try
                {
                    info.TotalProcessorTime = proc.TotalProcessorTime;
                }
                catch
                {
                    info.TotalProcessorTime = TimeSpan.Zero;
                }

                processInfos.Add(info);
            }
            catch
            {
                // Skip processes we can't access
            }
            finally
            {
                proc.Dispose();
            }
        }

        processInfos = processInfos.OrderByDescending(p => p.WorkingSetMb)
                                   .ToList();

        return new SystemSample
        {
            SampleNumber       = sampleNumber,
            Timestamp          = timestamp,
            OverallCpuPercent  = cpuUsage,
            TotalMemoryMb      = memoryInfo.TotalMB,
            UsedMemoryMb       = memoryInfo.UsedMB,
            AvailableMemoryMb  = memoryInfo.AvailableMB,
            MemoryUsagePercent = memoryInfo.UsagePercent,
            Processes          = processInfos
        };
    }

    private static (double TotalMB, double UsedMB, double AvailableMB, double UsagePercent) GetMemoryInfo()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetWindowsMemoryInfo();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return GetLinuxMemoryInfo();
            }

            GCMemoryInfo gcInfo  = GC.GetGCMemoryInfo();
            double       totalMb = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);

            return (totalMb, 0, totalMb, 0);
        }
        catch
        {
            return (0, 0, 0, 0);
        }
    }

    private static (double TotalMB, double UsedMB, double AvailableMB, double UsagePercent) GetWindowsMemoryInfo()
    {
        try
        {
        #pragma warning disable CA1416
            using PerformanceCounter availableBytesCounter = new(categoryName: "Memory", counterName: "Available MBytes");
            double                   availableMb           = availableBytesCounter.NextValue();
        #pragma warning restore CA1416

            GCMemoryInfo gcInfo  = GC.GetGCMemoryInfo();
            double       totalMb = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);

            double usedMb       = totalMb - availableMb;
            double usagePercent = totalMb > 0 ? Math.Round(value: usedMb / totalMb * 100, digits: 1) : 0;

            return (totalMb, usedMb, availableMb, usagePercent);
        }
        catch
        {
            GCMemoryInfo gcInfo  = GC.GetGCMemoryInfo();
            double       totalMb = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);

            return (totalMb, 0, totalMb, 0);
        }
    }

    private static (double TotalMB, double UsedMB, double AvailableMB, double UsagePercent) GetLinuxMemoryInfo()
    {
        string[] lines   = File.ReadAllLines(path: "/proc/meminfo");
        var      memInfo = new Dictionary<string, long>();

        foreach (string line in lines)
        {
            string[] parts = line.Split(separator: ':', StringSplitOptions.TrimEntries);

            if (parts.Length != 2)
            {
                continue;
            }

            string key = parts[0];

            string[] valueParts = parts[1]
                .Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries);

            if (valueParts.Length > 0 && long.TryParse(s: valueParts[0], result: out long value))
            {
                memInfo[key] = value;
            }
        }

        double totalKb     = memInfo.GetValueOrDefault(key: "MemTotal", defaultValue: 0);
        double availableKb = memInfo.GetValueOrDefault(key: "MemAvailable", defaultValue: 0);
        double usedKb      = totalKb - availableKb;

        double totalMb     = totalKb / 1024.0;
        double availableMb = availableKb / 1024.0;
        double usedMb      = usedKb / 1024.0;

        double usagePercent = totalKb > 0 ? Math.Round(value: usedKb / totalKb * 100, digits: 1) : 0;

        return (totalMb, usedMb, availableMb, usagePercent);
    }

    private static void WriteLogFile(List<SystemSample> samples)
    {
        DateTime generatedTime = DateTime.Now;

        StringBuilder sb = new();

        sb.AppendLine(value: "================================================================================")
          .AppendLine(value: "                           SYSTEM PROFILE REPORT")
          .AppendLine(value: "================================================================================")
          .AppendLine()
          .AppendLine(handler: $"User: {Environment.UserName}")
          .AppendLine(handler: $"Machine: {Environment.MachineName}")
          .AppendLine(handler: $"Generated: {generatedTime:yyyy-MM-dd HH:mm:ss}")
          .AppendLine(handler: $"Platform: {RuntimeInformation.OSDescription}")
          .AppendLine(handler: $"Processors: {Environment.ProcessorCount}")
          .AppendLine(handler: $"Total Samples: {samples.Count}");

        if (samples.Count > 0)
        {
            sb.AppendLine(handler: $"Duration: {samples.First().Timestamp:HH:mm:ss} - {samples.Last().Timestamp:HH:mm:ss}");
        }

        sb.AppendLine();

        if (samples.Count == 0)
        {
            sb.AppendLine(value: "No samples collected.");

            File.WriteAllText(path: Settings.DefineGeneratedFilePath(generatedTime), contents: sb.ToString());

            return;
        }

        sb.AppendLine(value: "--------------------------------------------------------------------------------")
          .AppendLine(value: "SUMMARY")
          .AppendLine(value: "--------------------------------------------------------------------------------");

        double avgCpu = samples.Average(s => s.OverallCpuPercent);
        double maxCpu = samples.Max(s => s.OverallCpuPercent);
        double minCpu = samples.Min(s => s.OverallCpuPercent);
        double avgMem = samples.Average(s => s.MemoryUsagePercent);
        double maxMem = samples.Max(s => s.MemoryUsagePercent);
        double minMem = samples.Min(s => s.MemoryUsagePercent);

        sb.AppendLine(handler: $"CPU Usage:    Min: {minCpu:F1}%   Avg: {avgCpu:F1}%   Max: {maxCpu:F1}%")
          .AppendLine(handler: $"Memory Usage: Min: {minMem:F1}%   Avg: {avgMem:F1}%   Max: {maxMem:F1}%")
          .AppendLine();

        var processStats = samples.SelectMany(s => s.Processes)
                                  .GroupBy(p => p.ProcessName)
                                  .Select(g => new
                                  {
                                      Name        = g.Key,
                                      AvgMemoryMB = g.Average(p => p.WorkingSetMb),
                                      MaxMemoryMB = g.Max(p => p.WorkingSetMb),
                                      AvgThreads  = g.Average(p => p.ThreadCount)
                                  })
                                  .OrderByDescending(p => p.AvgMemoryMB)
                                  .Take(count: 15)
                                  .ToList();

        sb.AppendLine(value: "--------------------------------------------------------------------------------")
          .AppendLine(value: "TOP PROCESSES (by average memory usage)")
          .AppendLine(value: "--------------------------------------------------------------------------------")
          .AppendLine(handler: $"{"Process",-30} {"Avg Memory",-15} {"Max Memory",-15} {"Avg Threads",-12}")
          .AppendLine(value: new string(c: '-', count: 80));

        foreach (var proc in processStats)
        {
            sb.AppendLine(handler: $"{proc.Name,-30} {proc.AvgMemoryMB,10:F1} MB   {proc.MaxMemoryMB,10:F1} MB   {proc.AvgThreads,8:F0}");
        }

        sb.AppendLine()
          .AppendLine(value: "================================================================================")
          .AppendLine(value: "DETAILED SAMPLES")
          .AppendLine(value: "================================================================================");

        foreach (SystemSample sample in samples)
        {
            sb.AppendLine()
              .AppendLine(handler: $"--- Sample {sample.SampleNumber} at {sample.Timestamp:yyyy-MM-dd HH:mm:ss.fff} ---")
              .AppendLine()
              .AppendLine(handler: $"CPU Usage: {sample.OverallCpuPercent:F1}%")
              .AppendLine(handler: $"Memory: {sample.UsedMemoryMb:F0} MB used / {sample.TotalMemoryMb:F0} MB total ({sample.MemoryUsagePercent:F1}%)")
              .AppendLine(handler: $"Available Memory: {sample.AvailableMemoryMb:F0} MB")
              .AppendLine()
              .AppendLine(value: "Top 10 Processes by Memory:")
              .AppendLine(handler: $"  {"PID",-8} {"Process",-25} {"Working Set",-15} {"Private Mem",-15} {"Threads",-8}")
              .AppendLine(handler: $"  {new string(c: '-', count: 74)}");

            foreach (ProcessInfo proc in sample.Processes.Take(count: 10))
            {
                sb.AppendLine(handler: $"  {proc.ProcessId,-8} {proc.ProcessName,-25} {proc.WorkingSetMb,10:F1} MB   {proc.PrivateMemoryMb,10:F1} MB   {proc.ThreadCount,-8}");
            }
        }

        File.WriteAllText(path: Settings.DefineGeneratedFilePath(generatedTime), contents: sb.ToString());
    }

    [ UsedImplicitly ]
    public sealed class Settings : CommandSettings
    {
        private static string s_generatedFilePath = string.Empty;

        public static readonly string DirectoryPath = Path.Combine(path1: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path2: "System Profiling Results");

        [ CommandOption(template: "-d|--duration <SECONDS>"), Description(description: "Total duration to sample (in seconds)"), DefaultValue(value: 60) ]
        public int Duration { get; [ UsedImplicitly ] init; }

        [ CommandOption(template: "-r|--rate <SECONDS>"), Description(description: "Interval between samples (in seconds)"), DefaultValue(value: 2) ]
        public int Rate { get; [ UsedImplicitly ] init; }

        public static string DefineGeneratedFilePath(DateTime generatedTime)
        {
            s_generatedFilePath = Path.Combine(DirectoryPath, path2: $"{generatedTime:yyyy-MM-dd HH-mm-ss}.txt");

            return s_generatedFilePath;
        }

        public static string GetGeneratedFilePath() => s_generatedFilePath;

        public override ValidationResult Validate()
        {
            if (Duration <= 0)
            {
                return ValidationResult.Error(message: "Duration must be a positive integer");
            }

            if (Rate <= 0)
            {
                return ValidationResult.Error(message: "Rate must be a positive integer");
            }

            return ValidationResult.Success();
        }
    }
}
