using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Spectre.Console;

public static class LinuxPlatform
{
    [DllImport("libc")]
    static extern uint getuid();

    public static bool EnsureAdminLinux()
    {
        if (getuid() == 0) return true;
        AnsiConsole.MarkupLine("[red]Root privileges are required.[/]");
        AnsiConsole.MarkupLine("Please re-run with [bold]sudo[/].");
        return false;
    }

    public static List<Core.DeviceInfo> GetDevicesLinux()
    {
        var list = new List<Core.DeviceInfo>();
        string sysDisk = Core.RunProcess("/bin/bash", "-c \"lsblk -no PKNAME $(findmnt -no SOURCE / || df / | tail -1 | awk '{print $1}')\"").Trim();

        string outp = Core.RunProcess("/bin/bash", "-c \"lsblk -b -d -o NAME,SIZE,PATH,MODEL\"");
        string[] lines = outp.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i].Trim();
            if (l.Length == 0 || l.StartsWith("NAME")) continue;

            string[] parts = Core.SplitSpaces(l);
            if (parts.Length < 4) continue;

            string name = parts[0];
            if (sysDisk.Length > 0 && name == sysDisk) continue;

            long size = Core.ParseLong(parts[1]);
            string path = parts[2];
            string model = Core.JoinFrom(parts, 3);

            list.Add(new Core.DeviceInfo { Path = path, Model = model, SizeBytes = size, IsSystem = false });
        }
        return list;
    }
}
