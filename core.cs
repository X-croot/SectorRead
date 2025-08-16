using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using Spectre.Console;

public static class Core
{
    public class DeviceInfo
    {
        public string Path;
        public string Model;
        public long SizeBytes;
        public bool IsSystem;
    }

    public static string[] SplitSpaces(string s)
    {
        return s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string[] SplitCsvLine(string line)
    {
        var list = new List<string>();
        bool inQ = false;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"') { inQ = !inQ; continue; }
            if (c == ',' && !inQ) { list.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        list.Add(sb.ToString());
        return list.ToArray();
    }

    public static string JoinFrom(string[] arr, int start)
    {
        if (arr == null || start >= arr.Length) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = start; i < arr.Length; i++)
        {
            if (i > start) sb.Append(" ");
            sb.Append(arr[i]);
        }
        return sb.ToString();
    }

    public static long ParseLong(string s)
    {
        try { return long.Parse(s.Trim(), CultureInfo.InvariantCulture); } catch { return -1; }
    }

    public static long ParseHumanSizeSI(string s)
    {
        try
        {
            s = s.Trim();
            string[] parts = SplitSpaces(s);
            if (parts.Length < 2) return -1;
            double val = double.Parse(parts[0], CultureInfo.InvariantCulture);
            string u = parts[1].ToUpperInvariant();
            double mul = 1.0;
            if (u.StartsWith("TB")) mul = 1e12;
            else if (u.StartsWith("GB")) mul = 1e9;
            else if (u.StartsWith("MB")) mul = 1e6;
            else if (u.StartsWith("KB")) mul = 1e3;
            return (long)(val * mul);
        }
        catch { return -1; }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "unknown";
        string[] u = new string[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024.0 && i < u.Length - 1) { v /= 1024.0; i++; }
        return v.ToString("0.##", CultureInfo.InvariantCulture) + " " + u[i];
    }

    public static string FormatDevice(DeviceInfo d)
    {
        string m = Safe(d.Model);
        string sz = FormatBytes(d.SizeBytes);
        return m + "  (" + sz + ")  —  " + d.Path;
    }

    public static string Safe(string s) { return string.IsNullOrWhiteSpace(s) ? "-" : s; }

    public static string GetDesktopPath()
    {
        try
        {
            string p = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        catch { }
        try { return Environment.GetFolderPath(Environment.SpecialFolder.Desktop); } catch { }
        return "";
    }

    public static void EnsureDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Failed to create directory:[/] " + ex.Message);
            Environment.Exit(5);
        }
    }

    public static string RunProcess(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo();
            psi.FileName = file;
            psi.Arguments = args;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return stdout;
            }
        }
        catch { return ""; }
    }

    public static string BrowseForDirectory(string startPath)
    {
        string current = string.IsNullOrWhiteSpace(startPath) ? Directory.GetCurrentDirectory() : startPath;
        while (true)
        {
            if (!Directory.Exists(current))
                current = Directory.GetCurrentDirectory();

            var options = new List<string>();

            options.Add(".. (Go up)");

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    options.Add(Path.GetFileName(dir));
                }
            }
            catch { }

            options.Add("✔ Select this directory");
            options.Add("+ Create new folder");
            options.Add("⌨ Enter path");

            var prompt = new SelectionPrompt<string>()
            .Title($"[cyan]Output directory[/]\n[grey]{current}[/]")
            .PageSize(15)
            .MoreChoicesText("[grey](use up/down to navigate)[/]")
            .AddChoices(options);

            string choice = AnsiConsole.Prompt(prompt);

            if (choice == ".. (Go up)")
            {
                string parent = Directory.GetParent(current)?.FullName;
                if (!string.IsNullOrEmpty(parent)) current = parent;
            }
            else if (choice == "✔ Select this directory")
            {
                EnsureDirectory(current);
                return current;
            }
            else if (choice == "+ Create new folder")
            {
                string name = AnsiConsole.Ask<string>("New folder name:");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    string newPath = Path.Combine(current, name);
                    EnsureDirectory(newPath);
                    current = newPath;
                }
            }
            else if (choice == "⌨ Enter path")
            {
                string path = AnsiConsole.Ask<string>("Enter path:");
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    current = Path.GetFullPath(path);
            }
            else
            {
                string next = Path.Combine(current, choice);
                if (Directory.Exists(next)) current = next;
            }
        }
    }
}
