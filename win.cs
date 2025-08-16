using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if WINDOWS
using System.Management;
#endif
using Spectre.Console;

public static class WindowsPlatform
{
    public static List<Core.DeviceInfo> GetDevicesWindows()
    {
        var list = new List<Core.DeviceInfo>();
        string sysPath = GetWindowsSystemPhysicalDrive();
        #if WINDOWS
        ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT DeviceID, Model, Size FROM Win32_DiskDrive");
        foreach (ManagementObject mo in searcher.Get())
        {
            string devId = (mo["DeviceID"] ?? "").ToString();
            string model = (mo["Model"] ?? "").ToString();
            long size = 0;
            try { size = Convert.ToInt64(mo["Size"], CultureInfo.InvariantCulture); } catch { size = 0; }

            bool isSystem = sysPath.Length > 0 && devId.ToUpperInvariant().Contains(sysPath.ToUpperInvariant());
            if (isSystem) continue;

            list.Add(new Core.DeviceInfo { Path = devId, Model = model, SizeBytes = size, IsSystem = isSystem });
        }
        #else
        string ps = Core.RunProcess("powershell",
                                    "-NoProfile -Command \"Get-CimInstance Win32_DiskDrive | Select-Object DeviceID,Model,Size | ConvertTo-Csv -NoTypeInformation\"");
        string[] lines = ps.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = Core.SplitCsvLine(lines[i]);
            if (cols.Length < 3) continue;
            string devId = cols[0].Trim('\"');
            string model = cols[1].Trim('\"');
            long sz = Core.ParseLong(cols[2]);
            bool isSystem = sysPath.Length > 0 && devId.ToUpperInvariant().Contains(sysPath.ToUpperInvariant());
            if (isSystem) continue;
            list.Add(new Core.DeviceInfo { Path = devId, Model = model, SizeBytes = sz, IsSystem = isSystem });
        }
        #endif
        return list;
    }

    public static string GetWindowsSystemPhysicalDrive()
    {
        try
        {
            #if WINDOWS
            ManagementObjectSearcher partSrch = new ManagementObjectSearcher("SELECT * FROM Win32_DiskPartition WHERE BootPartition = TRUE");
            foreach (ManagementObject part in partSrch.Get())
            {
                foreach (ManagementObject assoc in part.GetRelated("Win32_DiskDrive"))
                {
                    string devId = (assoc["DeviceID"] ?? "").ToString();
                    int idx = devId.ToUpperInvariant().IndexOf("PHYSICALDRIVE");
                    if (idx >= 0) return devId.Substring(idx);
                }
            }
            #else
            string ps = Core.RunProcess("powershell",
                                        "-NoProfile -Command \"Get-CimInstance Win32_DiskPartition | Where-Object {$_.BootPartition -eq $true} | " +
                                        "ForEach-Object { ($_ | Get-CimAssociatedInstance -ResultClassName Win32_DiskDrive).DeviceID }\"");
            string[] lines = ps.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                string dev = lines[0].Trim();
                int idx = dev.ToUpperInvariant().IndexOf("PHYSICALDRIVE");
                if (idx >= 0) return dev.Substring(idx);
            }
            #endif
        }
        catch { }
        return "";
    }

    public static bool EnsureAdminWindows()
    {
        try
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var pr = new System.Security.Principal.WindowsPrincipal(id);
            if (pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                return true;

            AnsiConsole.MarkupLine("[yellow]Warning:[/] Administrator privileges are required, a UAC prompt will appear.");
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch { }
            return false;
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]Error while checking for administrator privileges[/]");
            return false;
        }
    }

    public static Stream OpenDeviceReadStreamWindows(string path)
    {
        string p = path;
        if (!p.StartsWith(@"\\.\"))
        {
            int idx = p.ToUpperInvariant().IndexOf("PHYSICALDRIVE");
            if (idx >= 0) p = @"\\.\" + p.Substring(idx).Trim('\\');
        }
        return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
