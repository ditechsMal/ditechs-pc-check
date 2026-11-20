using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DitechsPcCheck
{
    /// <summary>
    /// The report card.
    ///
    /// Fixed-size window, absolute layout (nothing to get wrong on resize), no menu, no
    /// settings, no options. It opens, it tells you, it offers three buttons.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const string BookUrl = "https://www.ditechs.co.uk/contact/";
        // The Ditechs palette, read off ditechs.co.uk itself so the app matches the site:
        //   #0091D0  the brand blue (hero headings, the blue cards)
        //   #2B2C2A  the site's button and heading colour
        //   #666666  the site's body text
        private static readonly Color Brand = Color.FromArgb(0, 145, 208);
        private static readonly Color Dark = Color.FromArgb(43, 44, 42);
        private static readonly Color Ink = Color.FromArgb(43, 44, 42);
        private static readonly Color Muted = Color.FromArgb(102, 102, 102);

        private readonly Label _headline = new Label();
        private readonly Panel _listHost = new Panel();
        private readonly FlowLayoutPanel _list = new FlowLayoutPanel();
        private readonly Button _copy = new Button();
        private readonly Button _save = new Button();
        private readonly Button _book = new Button();
        private readonly Label _toast = new Label();
        private readonly Timer _toastTimer = new Timer();

        private string _report = string.Empty;
        private bool _busy = true;

        public MainForm()
        {
            Build();
            Shown += async (s, e) => await RunChecksAsync();
        }

        private void Build()
        {
            Text = "Ditechs PC Check";
            ClientSize = new Size(780, 680);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            // ---- header ----
            var header = new Panel { Location = new Point(0, 0), Size = new Size(780, 88), BackColor = Brand };
            var title = new Label
            {
                Text = "Ditechs PC Check",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(24, 16)
            };
            var subtitle = new Label
            {
                Text = $"This PC: {Environment.MachineName}   ·   {DateTime.Now:d MMMM yyyy}",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(27, 55)
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            // ---- the promise, then the verdict ----
            var promise = new Label
            {
                Text = "This only looks at your PC. It doesn't change anything, and nothing is sent anywhere.",
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(24, 100)
            };

            _headline.Text = "Checking your PC...";
            _headline.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
            _headline.ForeColor = Ink;
            _headline.AutoSize = false;
            _headline.Location = new Point(24, 126);
            _headline.Size = new Size(730, 32);

            // ---- the findings ----
            _listHost.Location = new Point(18, 168);
            _listHost.Size = new Size(744, 400);
            _listHost.AutoScroll = true;
            _listHost.BorderStyle = BorderStyle.FixedSingle;
            _listHost.BackColor = Color.White;

            _list.FlowDirection = FlowDirection.TopDown;
            _list.WrapContents = false;
            _list.AutoSize = true;
            _list.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _list.Location = new Point(6, 6);
            _list.BackColor = Color.White;
            _listHost.Controls.Add(_list);

            // ---- buttons ----
            StyleButton(_copy, "Copy this report", new Point(24, 580), 190, true);
            StyleButton(_save, "Save as a text file", new Point(226, 580), 200, true);
            StyleButton(_book, "Book a free health check", new Point(438, 580), 250, true);

            _copy.Click += (s, e) => DoCopy();
            _save.Click += (s, e) => DoSave();
            _book.Click += (s, e) => DoBook();

            _toast.Location = new Point(24, 622);
            _toast.Size = new Size(700, 20);
            _toast.ForeColor = Color.FromArgb(24, 148, 79);
            _toast.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

            var footer = new Label
            {
                Text = "Nothing on this PC was changed, and nothing was sent anywhere.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(24, 648)
            };

            _toastTimer.Interval = 4000;
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); _toast.Text = string.Empty; };

            Controls.Add(header);
            Controls.Add(promise);
            Controls.Add(_headline);
            Controls.Add(_listHost);
            Controls.Add(_copy);
            Controls.Add(_save);
            Controls.Add(_book);
            Controls.Add(_toast);
            Controls.Add(footer);
        }

        private static void StyleButton(Button b, string text, Point where, int width, bool enabled)
        {
            b.Text = text;
            b.Location = where;
            b.Size = new Size(width, 34);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Dark;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.FlatAppearance.BorderSize = 0;
            b.Enabled = enabled;
            b.Cursor = Cursors.Hand;
        }

        // ------------------------------------------------------------------ the run
        private async System.Threading.Tasks.Task RunChecksAsync()
        {
            RawData raw;
            try
            {
                raw = await System.Threading.Tasks.Task.Run(() => Collector.Gather(Say));
            }
            catch
            {
                raw = new RawData();
            }

            var findings = Rules.Order(new Finding?[]
            {
                Rules.WindowsSupport(raw.WindowsBuild, raw.WindowsDisplayVersion),
                Rules.Updates(raw.UpdateLastInstalled, raw.RestartWaiting, DateTime.Now),
                Rules.Security(raw.ProtectionFound, raw.RealtimeOn, raw.DefinitionsOutOfDate, raw.ProtectionName),
                Rules.Backup(raw.BackupOneDrive, raw.BackupFileHistory, raw.BackupOtherTool),
                Rules.Storage(raw.StorageFree, raw.StorageTotal),
                Rules.Memory(raw.MemoryBytes),
                Rules.Startup(raw.StartupCount),
                raw.BatteryPresent
                    ? (raw.BatteryHealthPercent != null
                        ? Rules.Battery(raw.BatteryHealthPercent)
                        : Rules.BatteryUnreadable())
                    : null,
                Rules.Uptime(raw.UptimeDays)
            });

            _report = Rules.Report(findings, Environment.MachineName, DateTime.Now);

            var overall = Rules.Overall(findings);
            _headline.Text = overall.Headline;
            _headline.ForeColor = ColourOf(overall.Verdict);

            foreach (Finding f in findings) _list.Controls.Add(BuildFindingPanel(f));

            _busy = false;
            _copy.Enabled = _save.Enabled = _book.Enabled = true;
        }

        private void Say(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!_busy) return; _headline.Text = message; })); }
            catch { }
        }

        private Panel BuildFindingPanel(Finding f)
        {
            var panel = new Panel
            {
                Width = 700,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 8)
            };

            var detail = new Label
            {
                Text = f.Detail,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Muted,
                AutoSize = true,
                MaximumSize = new Size(664, 0),
                Location = new Point(16, 26)
            };

            var section = new Label
            {
                Text = f.Section,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(16, 2)
            };

            panel.Height = 26 + detail.PreferredHeight + 10;
            panel.Controls.Add(new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(4, panel.Height),
                BackColor = ColourOf(f.Verdict)
            });
            panel.Controls.Add(section);
            panel.Controls.Add(detail);
            return panel;
        }

        private static Color ColourOf(Verdict v)
        {
            switch (v)
            {
                case Verdict.Good: return Color.FromArgb(24, 148, 79);
                case Verdict.Attention: return Color.FromArgb(206, 138, 12);
                case Verdict.Problem: return Color.FromArgb(198, 40, 40);
                default: return Color.FromArgb(130, 138, 148);
            }
        }

        // ------------------------------------------------------------------ the buttons
        private void DoCopy()
        {
            if (string.IsNullOrEmpty(_report)) return;
            try
            {
                Clipboard.SetText(_report);
                Toast("Copied. Paste it into an email to us and we'll take a look.");
            }
            catch
            {
                Toast("Couldn't copy it. Try Save as a text file instead.");
            }
        }

        private void DoSave()
        {
            if (string.IsNullOrEmpty(_report)) return;
            using var dialog = new SaveFileDialog
            {
                Title = "Save your PC Check report",
                FileName = $"Ditechs PC Check - {Environment.MachineName} - {DateTime.Now:yyyy-MM-dd}.txt",
                Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dialog.FileName, _report);
                Toast("Saved.");
            }
            catch
            {
                Toast("Couldn't save it there. Try your Desktop instead.");
            }
        }

        private void DoBook()
        {
            try { Process.Start(new ProcessStartInfo(BookUrl) { UseShellExecute = true }); }
            catch { Toast("Please visit ditechs.co.uk/contact to book."); }
        }

        private void Toast(string message)
        {
            _toast.Text = message;
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
}
