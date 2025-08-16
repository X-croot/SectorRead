using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Globalization;
using Spectre.Console;

class Program
{
    static volatile bool _cancelRequested = false;

    static int Main(string[] args)
    {
        Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _cancelRequested = true;
        };

        AnsiConsole.Write(new FigletText("SectorRead").Color(Color.Purple));
        AnsiConsole.MarkupLine("[dim]Cross-platform disk imaging tool (Windows/Linux/macOS)[/]");

        if (!EnsureAdmin())
            return 1;

        List<Core.DeviceInfo> devices = GetDevicesSafe();

        if (devices.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No suitable device found (system disk excluded).[/]");
            return 2;
        }

        var choices = new List<string>();
        for (int i = 0; i < devices.Count; i++)
            choices.Add(Core.FormatDevice(devices[i]));

        string picked = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
            .Title("[green]Select the device to image[/]")
            .PageSize(10)
            .MoreChoicesText("[grey](use up/down to navigate)[/]")
            .AddChoices(choices));

        Core.DeviceInfo dev = devices[choices.IndexOf(picked)];

        int blockMb = AnsiConsole.Prompt(
            new SelectionPrompt<int>()
            .Title("[yellow]Block size (MB)[/]")
            .AddChoices(new[] { 1, 2, 4, 8 }));

        bool custom = AnsiConsole.Confirm("Enter a custom block size (MB)?", false);
        if (custom)
        {
            blockMb = AnsiConsole.Ask<int>("Block size (MB):", 4);
            if (blockMb < 1) blockMb = 1;
            if (blockMb > 64) blockMb = 64;
        }

        int blockSize = blockMb * 1024 * 1024;

        string desktop = Core.GetDesktopPath();
        if (string.IsNullOrWhiteSpace(desktop)) desktop = Directory.GetCurrentDirectory();

        string outDir = Core.BrowseForDirectory(string.IsNullOrWhiteSpace(desktop) ? Directory.GetCurrentDirectory() : desktop);

        string defName = "image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".img";
        string fileName = AnsiConsole.Ask<string>("[cyan]Output file name[/] (default: " + defName + "):", "");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = defName;

        string outPath = Path.Combine(outDir, fileName);

        AnsiConsole.WriteLine();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Device", dev.Path);
        table.AddRow("Model", Core.Safe(dev.Model));
        table.AddRow("Size", Core.FormatBytes(dev.SizeBytes));
        table.AddRow("Block", blockMb.ToString(CultureInfo.InvariantCulture) + " MB (" + Core.FormatBytes(blockSize) + ")");
        table.AddRow("Output", outPath);
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("[bold]Proceed?[/]", true))
            return 0;

        int code = CopyDeviceToFile(dev, outPath, blockSize);
        if (code == 0)
        {
            AnsiConsole.MarkupLine("[bold green]✔ Completed[/] => " + outPath);
            return 0;
        }
        return code;
    }

    static int CopyDeviceToFile(Core.DeviceInfo dev, string outFile, int blockSize)
    {
        try
        {
            using (Stream src = OpenDeviceReadStream(dev.Path))
            using (FileStream dst = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: blockSize))
            {
                long total = dev.SizeBytes > 0 ? dev.SizeBytes : TryGetLength(src);
                if (total <= 0) total = -1;

                var progress = AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                         new ProgressBarColumn(),
                         new PercentageColumn(),
                         new RemainingTimeColumn(),
                         new SpinnerColumn(),
                });

                long copied = 0;
                byte[] buffer = new byte[blockSize];
                var start = Stopwatch.StartNew();
                double emaSpeed = 0.0;
                double alpha = 0.2;
                long lastCopied = 0;
                var lastSample = Stopwatch.StartNew();

                progress.Start(ctx =>
                {
                    var task = ctx.AddTask("[purple]Imaging[/]");
                    if (total > 0) task.MaxValue = total; else task.IsIndeterminate = true;

                    while (true)
                    {
                        if (_cancelRequested)
                            throw new OperationCanceledException();

                        int read = src.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;

                        dst.Write(buffer, 0, read);
                        copied += read;

                        if (total > 0)
                        {
                            task.Increment(read);
                        }

                        if (lastSample.ElapsedMilliseconds >= 500)
                        {
                            long deltaBytes = copied - lastCopied;
                            double deltaSec = Math.Max(0.001, lastSample.Elapsed.TotalSeconds);
                            double instSpeed = deltaBytes / deltaSec;
                            emaSpeed = emaSpeed <= 0 ? instSpeed : (alpha * instSpeed + (1 - alpha) * emaSpeed);
                            lastCopied = copied;
                            lastSample.Restart();

                            if (total > 0)
                            {
                                double remainingSec = emaSpeed > 0 ? (total - copied) / emaSpeed : double.NaN;
                                string eta = double.IsFinite(remainingSec) ? TimeSpan.FromSeconds(remainingSec).ToString(@"hh\:mm\:ss") : "N/A";
                                task.Description = $"Imaging — {Core.FormatBytes(copied)} / {Core.FormatBytes(total)} @ {Core.FormatBytes((long)emaSpeed)}/s — ETA {eta}";
                            }
                            else
                            {
                                task.Description = $"Imaging — {Core.FormatBytes(copied)} @ {Core.FormatBytes((long)emaSpeed)}/s";
                            }
                        }
                    }
                });

                dst.Flush(true);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled (Ctrl+C).[/]");
            return 3;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] " + ex.Message);
            return 4;
        }
    }

    static List<Core.DeviceInfo> GetDevicesSafe()
    {
        try { return GetDevices(); }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Device enumeration error:[/] " + ex.Message);
            return new List<Core.DeviceInfo>();
        }
    }

    static List<Core.DeviceInfo> GetDevices()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.GetDevicesWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacPlatform.GetDevicesMac();
        return LinuxPlatform.GetDevicesLinux();
    }

    static Stream OpenDeviceReadStream(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.OpenDeviceReadStreamWindows(path);
        else
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    static long TryGetLength(Stream s)
    {
        try { return s.Length; } catch { return -1; }
    }

    static bool EnsureAdmin()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsPlatform.EnsureAdminWindows();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return LinuxPlatform.EnsureAdminLinux(); // mac ve linux aynı şekilde root kontrol
            else
                return LinuxPlatform.EnsureAdminLinux();
    }
}
