using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DitechsPcCheck
{
    public enum Verdict { Good, Attention, Problem, Unknown }

    /// <summary>One line of the report: a section, a verdict, and the exact words the customer sees.</summary>
    public sealed class Finding
    {
        public string Section { get; }
        public Verdict Verdict { get; }
        public string Detail { get; }

        public Finding(string section, Verdict verdict, string detail)
        {
            Section = section;
            Verdict = verdict;
            Detail = detail;
        }
    }

    /// <summary>
    /// Every decision and every word in Ditechs PC Check.
    ///
    /// This file deliberately references NO Windows API. That way the wording and the
    /// thresholds can be tested on any machine (see tests/), which is the half of this
    /// program most likely to be wrong in a way that matters.
    ///
    /// HOUSE RULES (from SPEC.md, approved by Mal 20 Nov 2026):
    ///   - Plain English. Banned words are listed in <see cref="BannedWords"/> and asserted
    ///     against every produced string by the test suite.
    ///   - Never frighten: no exclamation marks, and no "sort out" without a next step.
    ///   - Never blame the user.
    ///   - Say "we couldn't find" rather than claiming something does not exist.
    /// </summary>
    public static class Rules
    {
        // ---- thresholds, all in one place so they are easy to argue about ----
        public const int StorageAttentionPercent = 20;
        public const int StorageProblemPercent = 10;
        public const int StartupAttentionCount = 6;    // more than this -> worth a look
        public const int StartupProblemCount = 12;     // more than this -> sort out
        public const int MemoryGoodGb = 8;
        public const int MemoryMinimumGb = 4;
        public const int UpdateStaleDays = 35;
        public const int UptimeAttentionDays = 14;
        public const int BatteryGoodPercent = 80;
        public const int BatteryAttentionPercent = 60;

        /// <summary>Words that must never reach the customer. Asserted by the test suite.</summary>
        public static readonly string[] BannedWords =
        {
            "registry", "service", "process", "driver", "SMART", "defrag",
            "cache", "kernel", "BIOS", "firmware", "corrupt", "exception",
            "error", "failed", "failure", "invalid", "byte", "administrator", "spooler"
        };

        public static readonly string[] SectionOrder =
        {
            "Windows", "Updates", "Security", "Backup", "Storage", "Memory", "Startup", "Battery", "Restarts"
        };

        // =====================================================================
        //  1. Windows — is it still supported?
        // =====================================================================
        public static Finding WindowsSupport(int build, string? displayVersion)
        {
            if (build <= 0)
                return new Finding("Windows", Verdict.Unknown, "Couldn't check which version of Windows this is.");

            string suffix = string.IsNullOrWhiteSpace(displayVersion) ? "" : $" ({displayVersion!.Trim()})";

            if (build >= 22000)
                return new Finding("Windows", Verdict.Good,
                    $"Windows 11{suffix} — still supported. Microsoft is still sending security updates for it.");

            if (build >= 10240)
                return new Finding("Windows", Verdict.Problem,
                    "Windows 10 — Microsoft stopped sending free security updates for this in October 2025. " +
                    "It still works, but newly found security flaws will not be fixed.");

            return new Finding("Windows", Verdict.Problem,
                "This version of Windows is no longer supported at all. It should not be used for anything " +
                "important — banking, email or shopping.");
        }

        // =====================================================================
        //  2. Updates
        // =====================================================================
        public static Finding Updates(DateTime? lastInstalled, bool restartWaiting, DateTime today)
        {
            if (restartWaiting)
                return new Finding("Updates", Verdict.Attention,
                    "This PC is waiting to restart to finish an update that has already downloaded.");

            if (lastInstalled == null)
                return new Finding("Updates", Verdict.Unknown,
                    "We couldn't read this PC's update history. It's worth opening Settings and " +
                    "looking at Windows Update.");

            string when = lastInstalled.Value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            int days = (today.Date - lastInstalled.Value.Date).Days;

            if (days <= UpdateStaleDays)
                return new Finding("Updates", Verdict.Good, $"Windows updates were last installed on {when}.");

            return new Finding("Updates", Verdict.Attention,
                $"Windows updates were last installed on {when}, which is {days} days ago. " +
                "It's worth checking for updates.");
        }

        // =====================================================================
        //  3. Security
        // =====================================================================
        public static Finding Security(bool? protectionFound, bool realtimeOn, bool? definitionsOutOfDate,
                                       string? productName)
        {
            if (protectionFound == null)
                return new Finding("Security", Verdict.Unknown, "Couldn't check your protection settings.");

            if (protectionFound == false)
                return new Finding("Security", Verdict.Problem,
                    "We couldn't find virus protection running on this PC.");

            if (!realtimeOn)
                return new Finding("Security", Verdict.Problem,
                    "Real-time protection is switched off. This PC is not currently protected against viruses.");

            if (definitionsOutOfDate == true)
                return new Finding("Security", Verdict.Attention,
                    "Protection is switched on but hasn't updated recently. Connect to the internet and let it catch up.");

            string named = string.IsNullOrWhiteSpace(productName) ? "" : $" ({productName!.Trim()})";
            return new Finding("Security", Verdict.Good, $"Virus protection is switched on{named}.");
        }

        // =====================================================================
        //  4. Backup
        // =====================================================================
        public static Finding Backup(bool oneDrive, bool fileHistory, bool windowsBackup)
        {
            if (oneDrive)
                return new Finding("Backup", Verdict.Good,
                    "A backup of your files appears to be set up — your files are syncing to OneDrive.");

            if (fileHistory || windowsBackup)
                return new Finding("Backup", Verdict.Good, "A backup of your files appears to be set up.");

            return new Finding("Backup", Verdict.Problem,
                "We couldn't find a backup of the files on this PC. If it stopped working tomorrow, " +
                "your photos and documents would go with it.");
        }

        // =====================================================================
        //  5. Storage
        // =====================================================================
        public static Finding Storage(long freeBytes, long totalBytes)
        {
            if (totalBytes <= 0 || freeBytes < 0)
                return new Finding("Storage", Verdict.Unknown, "Couldn't check the free space on this PC.");

            string free = Size(freeBytes);
            string total = Size(totalBytes);
            double percentFree = freeBytes * 100.0 / totalBytes;

            if (percentFree < StorageProblemPercent)
                return new Finding("Storage", Verdict.Problem,
                    $"Only {free} free. On its own that makes a PC slow, and it can stop updates from installing.");

            if (percentFree < StorageAttentionPercent)
                return new Finding("Storage", Verdict.Attention,
                    $"{free} free. That's getting tight — Windows starts behaving oddly when the disk fills up.");

            return new Finding("Storage", Verdict.Good, $"{free} free out of {total} — plenty of room.");
        }

        // =====================================================================
        //  6. Memory
        // =====================================================================
        public static Finding Memory(ulong totalBytes)
        {
            if (totalBytes == 0)
                return new Finding("Memory", Verdict.Unknown, "Couldn't check how much memory this PC has.");

            int gb = (int)Math.Round(totalBytes / 1073741824.0);

            if (gb >= MemoryGoodGb)
                return new Finding("Memory", Verdict.Good,
                    $"This PC has {gb} GB of memory — plenty for everyday use.");

            if (gb == MemoryMinimumGb)
                return new Finding("Memory", Verdict.Attention,
                    "4 GB of memory. That's the minimum Windows will run in, so expect it to feel slow " +
                    "with several things open.");

            if (gb > MemoryMinimumGb)
                return new Finding("Memory", Verdict.Attention,
                    $"This PC has {gb} GB of memory. Windows will run, but it can feel slow with several " +
                    "things open at once.");

            return new Finding("Memory", Verdict.Problem,
                "Under 4 GB of memory — below what Windows needs to run comfortably.");
        }

        // =====================================================================
        //  7. Startup
        // =====================================================================
        public static Finding Startup(int count)
        {
            if (count < 0)
                return new Finding("Startup", Verdict.Unknown, "Couldn't check what starts with this PC.");

            if (count == 0)
                return new Finding("Startup", Verdict.Good, "Nothing extra starts when you switch this PC on.");

            if (count == 1)
                return new Finding("Startup", Verdict.Good,
                    "1 program starts by itself when you switch this PC on. That's normal.");

            if (count <= StartupAttentionCount)
                return new Finding("Startup", Verdict.Good,
                    $"{count} programs start by themselves when you switch this PC on. That's normal.");

            if (count <= StartupProblemCount)
                return new Finding("Startup", Verdict.Attention,
                    $"{count} programs start by themselves. Each one adds to how long switching on takes, " +
                    "and most people don't need them all.");

            return new Finding("Startup", Verdict.Problem,
                $"{count} programs start by themselves. That's a lot, and it's one of the most common " +
                "reasons a PC feels slow to start.");
        }

        // =====================================================================
        //  8. Battery — laptops only. null in => section omitted entirely.
        // =====================================================================
        public static Finding? Battery(int? healthPercent)
        {
            if (healthPercent == null) return null;
            int p = Math.Max(0, Math.Min(100, healthPercent.Value));

            if (p >= BatteryGoodPercent)
                return new Finding("Battery", Verdict.Good,
                    $"Battery is in good health — it still holds about {p}% of what it did when new.");

            if (p >= BatteryAttentionPercent)
                return new Finding("Battery", Verdict.Attention,
                    $"Battery has worn to about {p}% of its original capacity. It won't last as long as it used to.");

            return new Finding("Battery", Verdict.Problem,
                $"Battery is worn to about {p}% of what it once held, and will start failing to hold charge.");
        }

        /// <summary>A laptop whose battery figure we cannot read. Says so rather than guessing.</summary>
        public static Finding BatteryUnreadable()
            => new Finding("Battery", Verdict.Unknown, "Couldn't check the battery on this PC.");

        // =====================================================================
        //  9. Restarts
        // =====================================================================
        public static Finding Uptime(int days)
        {
            if (days < 0)
                return new Finding("Restarts", Verdict.Unknown, "Couldn't check when this PC was last restarted.");

            string when = days == 0 ? "Last restarted today." : days == 1 ? "Last restarted yesterday." : $"Last restarted {days} days ago.";

            if (days <= UptimeAttentionDays)
                return new Finding("Restarts", Verdict.Good, when);

            if (days <= 30)
                return new Finding("Restarts", Verdict.Attention, when + " Restarting now and again keeps Windows healthy.");

            return new Finding("Restarts", Verdict.Attention, when + " Worth restarting it when convenient.");
        }

        // =====================================================================
        //  The headline
        // =====================================================================
        public static (Verdict Verdict, string Headline) Overall(IEnumerable<Finding> findings)
        {
            var list = findings.Where(f => f != null).ToList();
            int problems = list.Count(f => f.Verdict == Verdict.Problem);
            int attention = list.Count(f => f.Verdict == Verdict.Attention);

            if (problems > 0)
                return (Verdict.Problem,
                    $"There {(problems == 1 ? "is 1 thing" : $"are {problems} things")} here worth sorting out.");

            if (attention > 0)
                return (Verdict.Attention,
                    $"Your PC is fine to use, but there {(attention == 1 ? "is 1 thing" : $"are {attention} things")} worth a look.");

            return (Verdict.Good, "Your PC looks healthy. Nothing here needs attention.");
        }

        /// <summary>Findings in display order, with any nulls (desktop battery) dropped.</summary>
        public static List<Finding> Order(IEnumerable<Finding?> findings)
        {
            var present = findings.Where(f => f != null).Select(f => f!).ToList();
            return present
                .OrderBy(f => Array.IndexOf(SectionOrder, f.Section) is int i && i >= 0 ? i : 99)
                .ToList();
        }

        // =====================================================================
        //  The report — what "Copy this report" puts on the clipboard
        // =====================================================================
        public static string Report(IReadOnlyList<Finding> findings, string computerName, DateTime when)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DITECHS PC CHECK");
            sb.AppendLine($"Computer: {computerName}");
            sb.AppendLine($"Checked:  {when.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)} at {when:HH:mm}");
            sb.AppendLine();
            sb.AppendLine("Summary: " + Overall(findings).Headline);
            sb.AppendLine();

            foreach (var f in findings)
            {
                sb.AppendLine(Tag(f.Verdict).PadRight(16) + f.Section);
                sb.AppendLine(new string(' ', 16) + f.Detail);
                sb.AppendLine();
            }

            sb.AppendLine("Nothing on this PC was changed, and nothing was sent anywhere.");
            sb.AppendLine();
            sb.AppendLine("Anything here we can help with? Ditechs — computer, laptop and network help in");
            sb.AppendLine("Great Yarmouth, Gorleston and Lowestoft. ditechs.co.uk");
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        public static string Tag(Verdict v)
        {
            switch (v)
            {
                case Verdict.Good: return "OK";
                case Verdict.Attention: return "Worth a look";
                case Verdict.Problem: return "Sort out";
                default: return "Couldn't check";
            }
        }

        public static string ColourWord(Verdict v)
        {
            switch (v)
            {
                case Verdict.Good: return "green";
                case Verdict.Attention: return "amber";
                case Verdict.Problem: return "red";
                default: return "grey";
            }
        }

        /// <summary>"476 GB", "78 GB", "7.4 GB", "1.5 TB".</summary>
        public static string Size(long bytes)
        {
            const double Gb = 1024.0 * 1024.0 * 1024.0;
            double g = bytes / Gb;
            if (g >= 1024) return (g / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " TB";
            if (g >= 10) return Math.Round(g).ToString("0", CultureInfo.InvariantCulture) + " GB";
            return g.ToString("0.#", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
