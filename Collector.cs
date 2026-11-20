using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DitechsPcCheck
{
    /// <summary>Raw readings. Defaults are the "couldn't check" values on purpose.</summary>
    public sealed class RawData
    {
        public int WindowsBuild = -1;
        public string? WindowsDisplayVersion;

        public DateTime? UpdateLastInstalled;
        public bool RestartWaiting;

        public bool? ProtectionFound;          // null means we could not look at all
        public bool RealtimeOn;
        public bool? DefinitionsOutOfDate;
        public string? ProtectionName;

        public bool BackupOneDrive;
        public bool BackupFileHistory;
        public bool BackupOtherTool;

        public long StorageFree;
        public long StorageTotal;

        public ulong MemoryBytes;

        public int StartupCount = -1;

        public bool BatteryPresent;
        public int? BatteryHealthPercent;

        public int UptimeDays = -1;
    }

    /// <summary>
    /// Reads things off the PC. READ ONLY.
    ///
    /// Nothing here writes, deletes, changes a setting, or touches the network. Every read
    /// is wrapped so that a failure leaves the "couldn't check" default in place rather
    /// than producing an error the customer has to deal with.
    /// </summary>
    public static class Collector
    {
        public static RawData Gather(Action<string>? say = null)
        {
            var d = new RawData();
            Step("which version of Windows this is", say, () => ReadWindows(d));
            Step("your updates", say, () => ReadUpdates(d));
            Step("your protection", say, () => ReadSecurity(d));
            Step("your backups", say, () => ReadBackup(d));
            Step("your disk", say, () => ReadStorage(d));
            Step("your memory", say, () => ReadMemory(d));
            Step("what starts with the PC", say, () => ReadStartup(d));
            Step("your battery", say, () => ReadBattery(d));
            Step("how long the PC has been on", say, () => ReadUptime(d));
            return d;
        }

        private static void Step(string what, Action<string>? say, Action read)
        {
            try { say?.Invoke("Checking " + what + "..."); read(); }
            catch { /* leave the "couldn't check" default in place */ }
        }

        // ---------------------------------------------------------------- Windows
        private static void ReadWindows(RawData d)
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k == null) return;

            if (int.TryParse(k.GetValue("CurrentBuildNumber") as string, out int build)) d.WindowsBuild = build;

            d.WindowsDisplayVersion =
                (k.GetValue("DisplayVersion") as string)
                ?? (k.GetValue("ReleaseId") as string);
        }

        // ---------------------------------------------------------------- Updates
        private static void ReadUpdates(RawData d)
        {
            d.UpdateLastInstalled = ReadLastInstallDate();

            d.RestartWaiting =
                KeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") ||
                KeyExists(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") ||
                ValueExists(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");
        }

        /// <summary>
        /// When were updates last installed?
        ///
        /// Three sources, because no single one is dependable. The legacy registry value
        /// is absent on plenty of modern Windows installs - its absence says nothing at
        /// all about the machine - which is exactly the trap that made the first version
        /// of this tool report "couldn't check" on a perfectly healthy PC.
        ///
        ///   1. the update agent's own stored result (the most accurate, and it reads a
        ///      cached value rather than starting a search, so it stays quick);
        ///   2. the legacy registry value (older machines have it);
        ///   3. the installed-update list, as a last resort.
        /// </summary>
        private static DateTime? ReadLastInstallDate()
        {
            DateTime now = DateTime.Now;
            bool Plausible(DateTime when) => when.Year >= 2001 && when <= now.AddDays(1);

            DateTime? fromAgent = ReadLastInstallFromAgent();
            if (fromAgent != null && Plausible(fromAgent.Value)) return fromAgent;

            DateTime? fromRegistry = ReadLastInstallFromRegistry();
            if (fromRegistry != null && Plausible(fromRegistry.Value)) return fromRegistry;

            DateTime? fromFixes = ReadNewestInstalledFix();
            if (fromFixes != null && Plausible(fromFixes.Value)) return fromFixes;

            return null;
        }

        private static DateTime? ReadLastInstallFromAgent()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate");
                if (type == null) return null;

                object? agent = Activator.CreateInstance(type);
                if (agent == null) return null;

                object? results = type.InvokeMember("Results", BindingFlags.GetProperty, null, agent, null);
                if (results == null) return null;

                object? last = results.GetType().InvokeMember("LastInstallationSuccessDate",
                    BindingFlags.GetProperty, null, results, null);

                return last as DateTime?;
            }
            catch { return null; }
        }

        private static DateTime? ReadLastInstallFromRegistry()
        {
            try
            {
                using RegistryKey? k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
                if (k?.GetValue("LastSuccessTime") is string s &&
                    DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
                    return when;
            }
            catch { }
            return null;
        }

        private static DateTime? ReadNewestInstalledFix()
        {
            try
            {
                DateTime newest = DateTime.MinValue;
                using var searcher = new ManagementObjectSearcher(
                    @"root\cimv2", "SELECT InstalledOn FROM Win32_QuickFixEngineering");

                foreach (ManagementBaseObject o in searcher.Get())
                {
                    object? value = o["InstalledOn"];
                    DateTime when;

                    if (value is DateTime direct) { when = direct; }
                    else if (value is string raw)
                    {
                        // This one comes back as a localised date string, so try both.
                        if (!DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out when) &&
                            !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                            continue;
                    }
                    else continue;

                    if (when > newest) newest = when;
                }

                return newest == DateTime.MinValue ? null : newest;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- Security
        private static void ReadSecurity(RawData d)
        {
            var found = new List<(string name, bool realtime, bool outOfDate)>();

            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                string name = (o["displayName"] as string) ?? "virus protection";
                uint state = 0;
                try { state = Convert.ToUInt32(o["productState"] ?? 0u); } catch { }

                // Widely used decoding of the Security Center status word:
                //   bit 0x1000 set  -> real-time protection is on
                //   bit 0x10   set  -> the definitions are out of date
                bool realtime = (state & 0x1000) != 0;
                bool stale = (state & 0x10) != 0;
                found.Add((name, realtime, stale));
            }

            d.ProtectionFound = found.Count > 0;
            if (found.Count == 0) return;

            // Report on whichever product is actually protecting the PC.
            var best = found.FirstOrDefault(f => f.realtime);
            if (best.name == null) best = found[0];

            d.ProtectionName = best.name;
            d.RealtimeOn = best.realtime;
            d.DefinitionsOutOfDate = best.outOfDate;
        }

        // ---------------------------------------------------------------- Backup
        private static void ReadBackup(RawData d)
        {
            // 1. Are the known folders syncing to OneDrive?
            using (RegistryKey? k = Registry.CurrentUser.OpenSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
            {
                foreach (string value in new[] { "Personal", "My Pictures", "Desktop" })
                {
                    if (k?.GetValue(value) is string path &&
                        path.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        d.BackupOneDrive = true;
                        break;
                    }
                }
            }

            // 2. Is there a File History store on any drive we can see?
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;
                    if (Directory.Exists(Path.Combine(drive.RootDirectory.FullName, "FileHistory")))
                    {
                        d.BackupFileHistory = true;
                        break;
                    }
                }
            }
            catch { }

            // 3. Is a backup program installed? (OneDrive and File History are not the only
            //    answer, and telling someone with a working backup that they have none is the
            //    worst mistake this tool could make.)
            string[] known =
            {
                "acronis", "macrium", "backblaze", "carbonite", "idrive", "easeus", "aomei",
                "paragon", "veeam", "shadowprotect", "crashplan", "duplicati", "cobian",
                "syncback", "genie backup", "norton 360", "kaspersky", "bitdefender total",
                "microsoft 365", "dropbox", "google drive", "pcloud", "idrive", "onedrive"
            };

            foreach (string name in InstalledProgramNames())
            {
                string lower = name.ToLowerInvariant();
                if (known.Any(word => lower.Contains(word))) { d.BackupOtherTool = true; break; }
            }
        }

        private static IEnumerable<string> InstalledProgramNames()
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in roots)
            {
                RegistryKey? baseKey = null;
                try { baseKey = Registry.LocalMachine.OpenSubKey(root); } catch { }
                if (baseKey != null)
                {
                    using (baseKey)
                    foreach (string sub in baseKey.GetSubKeyNames())
                    {
                        string? display = null;
                        try { using RegistryKey? k = baseKey.OpenSubKey(sub); display = k?.GetValue("DisplayName") as string; }
                        catch { }
                        if (!string.IsNullOrWhiteSpace(display)) yield return display!;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- Storage
        private static void ReadStorage(RawData d)
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            d.StorageTotal = drive.TotalSize;
            d.StorageFree = drive.AvailableFreeSpace;
        }

        // ---------------------------------------------------------------- Memory
        private static void ReadMemory(RawData d)
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status)) d.MemoryBytes = status.ullTotalPhys;
        }

        // ---------------------------------------------------------------- Startup
        private static void ReadStartup(RawData d)
        {
            int count = 0;

            string[] runKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"
            };

            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string path in runKeys)
                {
                    using RegistryKey? k = hive.OpenSubKey(path);
                    if (k == null) continue;
                    count += k.GetValueNames()
                              .Count(n => !string.IsNullOrWhiteSpace(n) &&
                                          !string.IsNullOrWhiteSpace(k.GetValue(n) as string));
                }

            foreach (Environment.SpecialFolder folder in new[]
                     { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                try
                {
                    string dir = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        count += Directory.GetFiles(dir).Count(f => !Path.GetFileName(f).StartsWith("desktop.ini",
                                     StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }

            d.StartupCount = count;
        }

        // ---------------------------------------------------------------- Battery
        private static void ReadBattery(RawData d)
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS power)) return;
            if (power.BatteryFlag == 128 || power.BatteryFlag == 255) return;   // no battery in this PC
            d.BatteryPresent = true;

            try
            {
                uint designed = 0, full = 0;

                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                    foreach (ManagementBaseObject o in s.Get())
                        designed = Convert.ToUInt32(o["DesignedCapacity"] ?? 0u);

                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                    foreach (ManagementBaseObject o in s.Get())
                        full = Convert.ToUInt32(o["FullChargedCapacity"] ?? 0u);

                if (designed > 0 && full > 0)
                    d.BatteryHealthPercent = (int)Math.Round(full * 100.0 / designed);
            }
            catch
            {
                d.BatteryHealthPercent = null;   // laptop, but the figure is not readable
            }
        }

        // ---------------------------------------------------------------- Uptime
        private static void ReadUptime(RawData d)
        {
            try { d.UptimeDays = (int)(GetTickCount64() / 86400000UL); }
            catch { d.UptimeDays = -1; }
        }

        // ---------------------------------------------------------------- helpers
        private static bool KeyExists(RegistryKey hive, string path)
        {
            try { using RegistryKey? k = hive.OpenSubKey(path); return k != null; }
            catch { return false; }
        }

        private static bool ValueExists(RegistryKey hive, string path, string value)
        {
            try { using RegistryKey? k = hive.OpenSubKey(path); return k?.GetValue(value) != null; }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
