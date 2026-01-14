# System Profiler CLI

A cross-platform C# command-line application that profiles your machine's CPU and memory usage, featuring a rich terminal interface powered
by [Spectre.Console](https://spectreconsole.net/).

## Features

- **Real-time Monitoring** — Live-updating display showing CPU, memory, and top processes
- **Visual Progress Bars** — Colour-coded indicators (green/yellow/red) based on utilisation thresholds
- **Process Tracking** — Captures per-process memory usage, thread counts, and CPU time
- **Configurable Sampling** — Set custom duration and sample rate via command-line options
- **Detailed Logging** — Generates comprehensive plain-text reports with summary statistics
- **Cross-platform** — Works on Windows, Linux, and macOS with platform-specific optimisations

## Requirements

- .NET 8.0 SDK or later

## Building

```bash
cd SystemProfiler
dotnet build -c Release
```

The executable will be created at:

- **Windows**: `bin/Release/net8.0/SystemProfiler.exe`
- **Linux/macOS**: `bin/Release/net8.0/SystemProfiler`

## Usage

```bash
SystemProfiler [OPTIONS]
```

### Options

| Option                       | Description                           | Default       |
|------------------------------|---------------------------------------|---------------|
| `-d`, `--duration <SECONDS>` | Total duration to sample (in seconds) | `60`          |
| `-r`, `--rate <SECONDS>`     | Interval between samples (in seconds) | `2`           |
| `-p`, `--path <FILE>`        | Path to the output log file           | `profile.log` |
| `-h`, `--help`               | Display help information              |               |
| `--version`                  | Display version information           |               |

### Examples

Profile for 60 seconds with default settings:

```bash
dotnet run
```

Profile for 30 seconds, sampling every second:

```bash
dotnet run -- -d 30 -r 1 -p ./my-profile.log
```

Using the built executable:

```bash
./bin/Release/net8.0/SystemProfiler --duration 120 --rate 5 --path ~/logs/system_profile.log
```

Display help:

```bash
dotnet run -- --help
```

## Terminal Output

During profiling, you'll see a live-updating table:

```
───────────────────── System Profiler ─────────────────────

Platform:    Windows
Processors:  8
Duration:    60 seconds
Sample Rate: Every 2 second(s)
Log Path:    profile.log

Starting profiler... Press Ctrl+C to stop early.

╭──────────┬─────────────────────┬──────────────────────────╮
│  Metric  │        Value        │          Status          │
├──────────┼─────────────────────┼──────────────────────────┤
│  Sample  │ #15                 │ 14:32:45                 │
│ Progress │ 50%                 │ ██████████░░░░░░░░░░     │
│   CPU    │ 23.5%               │ █████░░░░░░░░░░░░░░░     │
│  Memory  │ 8192 / 16384 MB     │ ██████████░░░░░░░░░░     │
│ ──────── │ Top Processes       │ ────────                 │
│ 🥇 chrome│ 1024 MB             │ 45 threads               │
│ 🥈 code  │ 512 MB              │ 32 threads               │
│ 🥉 firefox│ 256 MB             │ 28 threads               │
╰──────────┴─────────────────────┴──────────────────────────╯
```

After profiling completes, summary tables are displayed:

```
╭─────────────────────── Summary ───────────────────────╮
│ Metric       │   Min   │   Avg   │   Max              │
├──────────────┼─────────┼─────────┼────────────────────┤
│ CPU Usage    │  5.2%   │  23.5%  │  67.8%             │
│ Memory Usage │ 48.2%   │  52.1%  │  58.9%             │
╰──────────────┴─────────┴─────────┴────────────────────╯

╭─────────────── Top Processes by Memory ───────────────╮
│ Process          │ Avg Memory │ Max Memory │ Threads  │
├──────────────────┼────────────┼────────────┼──────────┤
│ chrome           │  1024.5 MB │  1156.2 MB │    45    │
│ code             │   512.3 MB │   534.1 MB │    32    │
│ firefox          │   256.1 MB │   289.7 MB │    28    │
╰──────────────────┴────────────┴────────────┴──────────╯

✓ Profiling complete. Results saved to: profile.log
```

## Log File Format

The generated log file contains:

1. **Header** — Generation timestamp, platform, and processor count
2. **Summary** — Min/avg/max statistics for CPU and memory usage
3. **Top Processes** — Aggregated statistics across all samples (top 15 by average memory)
4. **Detailed Samples** — Full breakdown of each sample including:
    - Timestamp
    - CPU and memory metrics
    - Top 10 processes with PID, working set, private memory, and thread count

## Platform Notes

### Windows

- Uses `PerformanceCounter` for accurate CPU and memory metrics
- Requires no additional permissions for basic process enumeration

### Linux

- Reads directly from `/proc/stat` for CPU usage
- Reads from `/proc/meminfo` for memory statistics
- Some system processes may not be accessible without elevated permissions

### macOS

- Uses process enumeration for memory statistics
- CPU metrics are estimated from process activity

## Dependencies

| Package                                                                                                       | Version | Purpose                                        |
|---------------------------------------------------------------------------------------------------------------|---------|------------------------------------------------|
| [Spectre.Console](https://www.nuget.org/packages/Spectre.Console)                                             | 0.49.1  | Rich terminal output, tables, and live display |
| [Spectre.Console.Cli](https://www.nuget.org/packages/Spectre.Console.Cli)                                     | 0.49.1  | Command-line argument parsing                  |
| [System.Diagnostics.PerformanceCounter](https://www.nuget.org/packages/System.Diagnostics.PerformanceCounter) | 8.0.0   | Windows performance counters                   |

## Troubleshooting

### Permission Errors

Some processes may not be accessible due to system permissions. The profiler gracefully skips these processes and continues sampling.

### Terminal Rendering Issues

If the live display doesn't render correctly, ensure your terminal supports ANSI escape codes. On Windows, use Windows Terminal or PowerShell 7+ for the best results.

### High Memory Usage in Log

If your log files are large, consider:

- Reducing the duration (`-d`)
- Increasing the sample rate interval (`-r`)

## Licence

MIT
