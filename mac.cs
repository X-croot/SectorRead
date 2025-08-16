using System;
using System.Collections.Generic;

public static class MacPlatform
{
    public static List<Core.DeviceInfo> GetDevicesMac()
    {
        var list = new List<Core.DeviceInfo>();

        string sysId = Core.RunProcess("/bin/bash", "-c \"diskutil info / | awk -F: '/Device Identifier/{print $2}' | xargs\"").Trim();
        string sysDisk = sysId;
        int sIdx = sysId.IndexOf('s');
        if (sIdx >= 0) sysDisk = sysId.Substring(0, sIdx);

        string[] lines = Core.RunProcess("/bin/bash", "-c \"diskutil list | grep '^/dev/disk'\"")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            string path = lines[i].Trim().Split(' ')[0];
            if (sysDisk.Length > 0 && path.Contains(sysDisk)) continue;

            long size = -1;
            try
            {
                string sz = Core.RunProcess("/bin/bash", "-c \"diskutil info " + path + " | awk -F: '/Disk Size/{print $2}' | sed 's/([^)]*)//g' | xargs\"");
                size = Core.ParseHumanSizeSI(sz);
            }
            catch { }
            list.Add(new Core.DeviceInfo { Path = path, Model = path, SizeBytes = size, IsSystem = false });
        }
        return list;
    }
}
