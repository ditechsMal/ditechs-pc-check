using System;
using System.Collections.Generic;
using System.Linq;
using DitechsPcCheck;

internal static class Harness
{
    public static int Passed;
    public static int Failed;

    public static void True(bool condition, string label)
    {
        if (condition) Passed++;
        else { Failed++; Console.WriteLine("   FAIL: " + label); }
    }

    public static void Has(string needle, string haystack, string label)
    {
        True(haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0,
             $"{label} - expected to contain \"{needle}\", got \"{haystack}\"");
    }

    public static void Is(string expected, string actual, string label)
    {
        True(expected == actual, $"{label} - expected \"{expected}\", got \"{actual}\"");
    }
}

internal static class Program
{
    private static readonly DateTime Today = new DateTime(2026, 11, 20, 14, 5, 0);

    /// <summary>Every customer-facing string the program can produce, for the house-rules sweep.</summary>
    private static readonly List<string> AllStrings = new List<string>();

    private static Finding F(Finding f) { AllStrings.Add(f.Section); AllStrings.Add(f.Detail); return f; }

    private static int Main()
    {
        Console.WriteLine("Ditechs PC Check - logic and wording tests");

        // ---------------------------------------------------------------- 1. Windows
        var win11 = F(Rules.WindowsSupport(22631, "23H2"));
        Harness.True(win11.Verdict == Verdict.Good, "Windows 11 is a good result");
        Harness.Has("Windows 11 (23H2)", win11.Detail, "Windows 11 names the version");
        Harness.Has("still supported", win11.Detail, "Windows 11 says supported");

        var win11NoVer = F(Rules.WindowsSupport(22631, null));
        Harness.Has("Windows 11", win11NoVer.Detail, "Windows 11 without a version label");

        var win10 = F(Rules.WindowsSupport(19045, "22H2"));
        Harness.True(win10.Verdict == Verdict.Problem, "Windows 10 is a problem result");
        Harness.Has("stopped sending free security updates", win10.Detail, "Windows 10 explains the end of updates");
        Harness.Has("October 2025", win10.Detail, "Windows 10 gives the date");

        var ancient = F(Rules.WindowsSupport(7601, "Service Pack 1"));
        Harness.True(ancient.Verdict == Verdict.Problem, "Windows 7 is a problem result");
        Harness.Has("no longer supported at all", ancient.Detail, "Old Windows says unsupported");
        Harness.Has("banking, email or shopping", ancient.Detail, "Old Windows gives guidance");

        var winUnknown = F(Rules.WindowsSupport(0, null));
        Harness.True(winUnknown.Verdict == Verdict.Unknown, "Unreadable Windows version is 'couldn't check'");

        // ---------------------------------------------------------------- 2. Updates
        var upRecent = F(Rules.Updates(Today.AddDays(-5), false, Today));
        Harness.True(upRecent.Verdict == Verdict.Good, "Recent update is a good result");
        Harness.Has("last installed on 15 November 2026", upRecent.Detail, "Recent update gives the date");

        var upStale = F(Rules.Updates(Today.AddDays(-50), false, Today));
        Harness.True(upStale.Verdict == Verdict.Attention, "Stale update is 'worth a look'");
        Harness.Has("50 days ago", upStale.Detail, "Stale update counts the days");
        Harness.Has("worth checking for updates", upStale.Detail, "Stale update gives a next step");

        var upEdgeGood = F(Rules.Updates(Today.AddDays(-35), false, Today));
        Harness.True(upEdgeGood.Verdict == Verdict.Good, "35 days is still a good result");

        var upEdgeStale = F(Rules.Updates(Today.AddDays(-36), false, Today));
        Harness.True(upEdgeStale.Verdict == Verdict.Attention, "36 days tips over to 'worth a look'");

        var upRestart = F(Rules.Updates(Today.AddDays(-2), true, Today));
        Harness.True(upRestart.Verdict == Verdict.Attention, "Waiting to restart is 'worth a look'");
        Harness.Has("waiting to restart", upRestart.Detail, "Waiting to restart says so");

        var upUnknown = F(Rules.Updates(null, false, Today));
        Harness.True(upUnknown.Verdict == Verdict.Unknown, "Unreadable update date is 'couldn't check'");
        // House rule: even a "couldn't check" must leave the customer with something to do.
        Harness.Has("Windows Update", upUnknown.Detail, "Unreadable update date still gives a next step");

        // ---------------------------------------------------------------- 3. Security
        var secUnknown = F(Rules.Security(null, false, null, null));
        Harness.True(secUnknown.Verdict == Verdict.Unknown, "Unreadable protection is 'couldn't check'");

        var secNone = F(Rules.Security(false, false, null, null));
        Harness.True(secNone.Verdict == Verdict.Problem, "No protection found is a problem");
        Harness.Has("couldn't find virus protection", secNone.Detail, "No protection says we couldn't find it");

        var secOff = F(Rules.Security(true, false, null, "Windows Defender"));
        Harness.True(secOff.Verdict == Verdict.Problem, "Protection switched off is a problem");
        Harness.Has("Real-time protection is switched off", secOff.Detail, "Protection off says so plainly");

        var secStale = F(Rules.Security(true, true, true, "Windows Defender"));
        Harness.True(secStale.Verdict == Verdict.Attention, "Out-of-date protection is 'worth a look'");
        Harness.Has("let it catch up", secStale.Detail, "Out-of-date protection gives a next step");

        var secOk = F(Rules.Security(true, true, false, "Windows Defender"));
        Harness.True(secOk.Verdict == Verdict.Good, "Working protection is a good result");
        Harness.Has("Virus protection is switched on (Windows Defender)", secOk.Detail, "Working protection names the product");
        Harness.True(secOk.Detail.IndexOf("up to date", StringComparison.OrdinalIgnoreCase) < 0,
                     "Working protection must NOT claim 'up to date' (we can't verify that)");

        var secOkNameless = F(Rules.Security(true, true, false, null));
        Harness.Is("Virus protection is switched on.", secOkNameless.Detail, "Working protection without a product name");

        // ---------------------------------------------------------------- 4. Backup
        var bkOneDrive = F(Rules.Backup(true, false, false));
        Harness.True(bkOneDrive.Verdict == Verdict.Good, "OneDrive backup is a good result");
        Harness.Has("syncing to OneDrive", bkOneDrive.Detail, "OneDrive backup says OneDrive");

        var bkFileHistory = F(Rules.Backup(false, true, false));
        Harness.True(bkFileHistory.Verdict == Verdict.Good, "File History backup is a good result");

        var bkWindows = F(Rules.Backup(false, false, true));
        Harness.True(bkWindows.Verdict == Verdict.Good, "Another backup program is a good result");

        var bkNone = F(Rules.Backup(false, false, false));
        Harness.True(bkNone.Verdict == Verdict.Problem, "No backup found is a problem");
        Harness.Has("We couldn't find a backup", bkNone.Detail, "No backup uses 'we couldn't find'");
        Harness.True(bkNone.Detail.IndexOf("you have no backup", StringComparison.OrdinalIgnoreCase) < 0,
                     "No backup must not assert 'you have no backup'");

        // ---------------------------------------------------------------- 5. Storage
        const long Gb = 1024L * 1024 * 1024;
        var st40 = F(Rules.Storage(190 * Gb, 476 * Gb));
        Harness.True(st40.Verdict == Verdict.Good, "40% free is a good result");
        Harness.Is("190 GB free out of 476 GB - plenty of room.".Replace(" - ", " \u2014 "), st40.Detail, "40% free wording");

        var st15 = F(Rules.Storage((long)(0.15 * 476) * Gb, 476 * Gb));
        Harness.True(st15.Verdict == Verdict.Attention, "15% free is 'worth a look'");
        Harness.Has("getting tight", st15.Detail, "15% free says getting tight");

        var st5 = F(Rules.Storage((long)(0.05 * 476) * Gb, 476 * Gb));
        Harness.True(st5.Verdict == Verdict.Problem, "5% free is a problem");
        Harness.Has("Only", st5.Detail, "5% free starts with Only");
        Harness.Has("can stop updates from installing", st5.Detail, "5% free explains the consequence");

        var stEdge20 = F(Rules.Storage(96 * Gb, 480 * Gb));
        Harness.True(stEdge20.Verdict == Verdict.Good, "exactly 20% free is still a good result");

        var stNone = F(Rules.Storage(0, 0));
        Harness.True(stNone.Verdict == Verdict.Unknown, "Unreadable storage is 'couldn't check'");

        // ---------------------------------------------------------------- 6. Memory
        var mem16 = F(Rules.Memory(16UL * 1073741824));
        Harness.True(mem16.Verdict == Verdict.Good, "16 GB is a good result");
        Harness.Has("16 GB of memory", mem16.Detail, "16 GB names the amount");

        var mem4 = F(Rules.Memory(4UL * 1073741824));
        Harness.True(mem4.Verdict == Verdict.Attention, "4 GB is 'worth a look'");
        Harness.Has("minimum Windows will run in", mem4.Detail, "4 GB explains the minimum");

        var mem6 = F(Rules.Memory(6UL * 1073741824));
        Harness.True(mem6.Verdict == Verdict.Attention, "6 GB is 'worth a look'");

        var mem8 = F(Rules.Memory(8UL * 1073741824));
        Harness.True(mem8.Verdict == Verdict.Good, "8 GB is a good result");

        var mem2 = F(Rules.Memory(2UL * 1073741824));
        Harness.True(mem2.Verdict == Verdict.Problem, "2 GB is a problem");

        var memNone = F(Rules.Memory(0));
        Harness.True(memNone.Verdict == Verdict.Unknown, "Unreadable memory is 'couldn't check'");

        // ---------------------------------------------------------------- 7. Startup
        var su0 = F(Rules.Startup(0));
        Harness.True(su0.Verdict == Verdict.Good, "No startup items is a good result");
        Harness.Has("Nothing extra starts", su0.Detail, "No startup items says nothing extra starts");

        var su1 = F(Rules.Startup(1));
        Harness.Has("1 program starts by itself", su1.Detail, "One startup item is singular");

        var su5 = F(Rules.Startup(5));
        Harness.True(su5.Verdict == Verdict.Good, "5 startup items is a good result");
        Harness.Has("5 programs start by themselves", su5.Detail, "5 startup items is plural");

        var su10 = F(Rules.Startup(10));
        Harness.True(su10.Verdict == Verdict.Attention, "10 startup items is 'worth a look'");
        Harness.Has("most people don't need them all", su10.Detail, "10 startup items explains why");

        var su27 = F(Rules.Startup(27));
        Harness.True(su27.Verdict == Verdict.Problem, "27 startup items is a problem");
        Harness.Has("slow to start", su27.Detail, "27 startup items explains the consequence");

        var suEdge13 = F(Rules.Startup(13));
        Harness.True(suEdge13.Verdict == Verdict.Problem, "13 startup items tips into a problem");
        var suEdge12 = F(Rules.Startup(12));
        Harness.True(suEdge12.Verdict == Verdict.Attention, "12 startup items is still 'worth a look'");

        var suNone = F(Rules.Startup(-1));
        Harness.True(suNone.Verdict == Verdict.Unknown, "Unreadable startup count is 'couldn't check'");

        // ---------------------------------------------------------------- 8. Battery
        var batGood = Rules.Battery(92)!;
        AllStrings.Add(batGood.Detail);
        Harness.True(batGood.Verdict == Verdict.Good, "92% battery is a good result");
        Harness.Has("92% of what it did when new", batGood.Detail, "Good battery names the figure");

        var batMid = Rules.Battery(68)!;
        AllStrings.Add(batMid.Detail);
        Harness.True(batMid.Verdict == Verdict.Attention, "68% battery is 'worth a look'");

        var batBad = Rules.Battery(51)!;
        AllStrings.Add(batBad.Detail);
        Harness.True(batBad.Verdict == Verdict.Problem, "51% battery is a problem");

        Harness.True(Rules.Battery(null) == null, "A desktop (no battery) produces no battery section at all");
        Harness.True(Rules.Battery(200)!.Detail.Contains("100%"), "Battery above 100% is clamped");

        var batUnknown = Rules.BatteryUnreadable();
        AllStrings.Add(batUnknown.Detail);
        Harness.True(batUnknown.Verdict == Verdict.Unknown,
                     "A laptop whose battery figure cannot be read says 'couldn't check' rather than guessing");

        // ---------------------------------------------------------------- 9. Restarts
        var restToday = F(Rules.Uptime(0));
        Harness.Has("Last restarted today", restToday.Detail, "Restarted today reads properly");
        var restYesterday = F(Rules.Uptime(1));
        Harness.Has("Last restarted yesterday", restYesterday.Detail, "Restarted yesterday reads properly");
        var rest3 = F(Rules.Uptime(3));
        Harness.True(rest3.Verdict == Verdict.Good, "3 days uptime is a good result");
        Harness.Is("Last restarted 3 days ago.", rest3.Detail, "3 days uptime wording");
        var rest21 = F(Rules.Uptime(21));
        Harness.True(rest21.Verdict == Verdict.Attention, "21 days uptime is 'worth a look'");
        Harness.Has("keeps Windows healthy", rest21.Detail, "21 days uptime explains why");
        var rest47 = F(Rules.Uptime(47));
        Harness.True(rest47.Verdict == Verdict.Attention, "47 days uptime is 'worth a look'");
        Harness.Has("when convenient", rest47.Detail, "47 days uptime is gentle about it");

        // ---------------------------------------------------------------- The headline
        var allGood = Rules.Overall(new[] { win11, secOk, mem16 });
        Harness.True(allGood.Verdict == Verdict.Good, "All-good headline is green");
        Harness.Is("Your PC looks healthy. Nothing here needs attention.", allGood.Headline, "All-good headline wording");

        var oneAttention = Rules.Overall(new[] { win11, su10 });
        Harness.True(oneAttention.Verdict == Verdict.Attention, "One 'worth a look' makes an amber headline");
        Harness.Is("Your PC is fine to use, but there is 1 thing worth a look.", oneAttention.Headline,
                   "Singular 'worth a look' headline");

        var threeAttention = Rules.Overall(new[] { su10, mem4, upStale });
        Harness.Is("Your PC is fine to use, but there are 3 things worth a look.", threeAttention.Headline,
                   "Plural 'worth a look' headline");

        var oneProblem = Rules.Overall(new[] { win10, su5 });
        Harness.True(oneProblem.Verdict == Verdict.Problem, "A problem makes a red headline");
        Harness.Is("There is 1 thing here worth sorting out.", oneProblem.Headline, "Singular problem headline");

        var twoProblems = Rules.Overall(new[] { win10, bkNone, mem16 });
        Harness.Is("There are 2 things here worth sorting out.", twoProblems.Headline, "Plural problem headline");

        var unknownOnly = Rules.Overall(new[] { upUnknown, winUnknown });
        Harness.True(unknownOnly.Verdict == Verdict.Good,
                     "'Couldn't check' results do not turn the headline amber or red");

        // ---------------------------------------------------------------- Ordering
        var mixed = Rules.Order(new Finding?[] { su5, win11, Rules.Battery(null), mem16, bkNone });
        Harness.Is("Windows,Backup,Memory,Startup", string.Join(",", mixed.Select(f => f.Section)),
                   "Sections appear in the agreed order, desktop battery dropped");

        // ---------------------------------------------------------------- Size formatting
        Harness.Is("476 GB", Rules.Size(476 * Gb), "Whole hundreds of GB");
        Harness.Is("7.4 GB", Rules.Size((long)(7.4 * Gb)), "Small values keep a decimal");
        Harness.Is("1.5 TB", Rules.Size((long)(1536L * Gb)), "Large values switch to TB");

        // ---------------------------------------------------------------- The report
        var findings = Rules.Order(new Finding?[]
        {
            win10, upStale, secOk, bkNone, Rules.Storage(78 * Gb, 476 * Gb),
            Rules.Memory(8UL * 1073741824), Rules.Startup(14), Rules.Battery(68), Rules.Uptime(21)
        });
        string report = Rules.Report(findings, "DESKTOP-7F2K9", Today);
        AllStrings.Add(report);
        AllStrings.Add(Rules.Tag(Verdict.Good)); AllStrings.Add(Rules.Tag(Verdict.Attention));
        AllStrings.Add(Rules.Tag(Verdict.Problem)); AllStrings.Add(Rules.Tag(Verdict.Unknown));

        Harness.Has("DITECHS PC CHECK", report, "Report has a title");
        Harness.Has("DESKTOP-7F2K9", report, "Report names the computer");
        Harness.Has("20 November 2026 at 14:05", report, "Report stamps the date and time");
        Harness.Has("Summary: ", report, "Report has a summary line");
        Harness.Has("Sort out", report, "Report uses plain tag words, not colour names");
        Harness.Has("Nothing on this PC was changed", report, "Report states nothing was changed");
        Harness.Has("nothing was sent anywhere", report, "Report states nothing was sent");
        Harness.Has("Great Yarmouth, Gorleston and Lowestoft", report, "Report carries the Ditechs line");
        Harness.Has("ditechs.co.uk", report, "Report carries the web address");
        Harness.True(!report.Contains("["), "Report has no square-bracket furniture");
        // The report must contain every finding, or a customer would paste a partial picture.
        Harness.True(findings.All(f => report.Contains(f.Detail)), "Every finding appears in the report");
        // And it must be plain text a human can read in an email.
        Harness.True(!report.Contains("\t"), "Report contains no tabs");
        Harness.True(!report.Contains("<"), "Report contains no markup");

        // ---------------------------------------------------------------- House rules sweep
        int swept = 0;
        foreach (string s in AllStrings.Where(s => !string.IsNullOrEmpty(s)))
        {
            swept++;
            foreach (string banned in Rules.BannedWords)
            {
                foreach (string word in s.Split(new[] { ' ', ',', '.', '(', ')', '-', '\u2014', ';', ':' },
                                                StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.Equals(word.Trim('\'', '"'), banned, StringComparison.OrdinalIgnoreCase))
                        Harness.True(false, $"house rule: \"{banned}\" must not appear to the customer - in \"{s}\"");
                }
            }
            Harness.True(!s.Contains("!"), $"house rule: no exclamation marks - in \"{s}\"");
            // The report is exempt: it aligns its tag column with deliberate runs of spaces.
            if (!ReferenceEquals(s, report))
                Harness.True(!s.Contains("  "), $"house rule: no double spaces - in \"{s}\"");
        }
        Console.WriteLine($"   swept {swept} customer-facing strings against {Rules.BannedWords.Length} banned words");

        Console.WriteLine();
        Console.WriteLine($"   {Harness.Passed} passed, {Harness.Failed} failed");
        return Harness.Failed == 0 ? 0 : 1;
    }
}
