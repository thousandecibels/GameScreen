using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("GameScreen")]
[assembly: System.Reflection.AssemblyDescription("Choose one display for your Windows games.")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]

namespace GameMonitor
{
    internal static class Program
    {
        internal const string AppName = "GameScreen";

        [STAThread]
        private static void Main()
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Keep the original instance lock so older copies cannot run alongside GameScreen.
            using (Mutex instance = new Mutex(false, @"Local\GameMonitor.Desktop.1"))
            {
                bool acquired;
                try { acquired = instance.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    MessageBox.Show(AppName + " is already open. Use its icon in the Windows notification area.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    using (MainWindow form = new MainWindow(new PreferenceStore(SettingsPath(AppDomain.CurrentDomain.BaseDirectory))))
                        Application.Run(form);
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    instance.ReleaseMutex();
                }
            }
        }

        internal static string SettingsPath(string applicationFolder)
        {
            if (File.Exists(Path.Combine(applicationFolder, "portable.flag")))
            {
                // Display IDs and fullscreen recovery belong to the computer that saved them.
                return Path.Combine(applicationFolder, "Data", Environment.MachineName, "settings.json");
            }

            string data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // Keep the original settings location so existing selections carry over.
            return Path.Combine(data, "Game Monitor", "settings.json");
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    }

    internal sealed class MainWindow : Form
    {
        private const int AccessDenied = 5;
        private static readonly bool HighContrast = SystemInformation.HighContrast;
        private static readonly Color Canvas = HighContrast ? SystemColors.Window : Color.FromArgb(242, 245, 248);
        private static readonly Color Paper = HighContrast ? SystemColors.Window : Color.White;
        private static readonly Color Field = HighContrast ? SystemColors.Window : Color.FromArgb(250, 252, 254);
        private static readonly Color Ink = HighContrast ? SystemColors.WindowText : Color.FromArgb(22, 34, 53);
        private static readonly Color Secondary = HighContrast ? SystemColors.WindowText : Color.FromArgb(77, 91, 111);
        private static readonly Color Accent = HighContrast ? SystemColors.Highlight : Color.FromArgb(12, 102, 88);
        private static readonly Color AccentHover = HighContrast ? Accent : Color.FromArgb(8, 84, 73);
        private static readonly Color Edge = HighContrast ? SystemColors.WindowText : Color.FromArgb(215, 223, 231);
        private static readonly Color ButtonFill = HighContrast ? SystemColors.ButtonFace : Color.FromArgb(247, 249, 252);
        private static readonly Color ButtonHover = HighContrast ? ButtonFill : Color.FromArgb(232, 239, 245);
        private static readonly Color Header = HighContrast ? SystemColors.Window : Color.FromArgb(15, 29, 51);
        private static readonly Color HeaderPill = HighContrast ? SystemColors.Window : Color.FromArgb(36, 53, 77);
        private static readonly Color HeaderText = HighContrast ? SystemColors.WindowText : Color.FromArgb(249, 252, 255);
        private static readonly Color HeaderSecondary = HighContrast ? HeaderText : Color.FromArgb(187, 204, 225);
        private static readonly Color HeaderActive = HighContrast ? HeaderText : Color.FromArgb(128, 238, 213);
        private readonly PreferenceStore store;
        private readonly Preferences settings;
        private readonly ComboBox monitorPicker = new ComboBox();
        private readonly ListBox addedGames = new ListBox();
        private readonly Label windowsMain;
        private readonly Label status;
        private readonly Label discovered;
        private readonly Button apply;
        private readonly Button pause;
        private readonly Button fullscreen;
        private readonly Button remove;
        private readonly ToolStripMenuItem trayFullscreen = new ToolStripMenuItem();
        private readonly Label routingBadge;
        private readonly Label emptyGames;
        private readonly Bitmap headerImage;
        private readonly Icon windowIcon;
        private readonly Icon trayIcon;
        private NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 450 };
        private Displays displays;
        private GameCatalog catalog = new GameCatalog();
        private bool allowClose;
        private bool layoutChanged;
        private readonly Dictionary<IntPtr, OpenGameWindow> checkedWindows = new Dictionary<IntPtr, OpenGameWindow>();

        private sealed class OpenGameWindow
        {
            public uint ProcessId;
            public int AttemptsLeft;
        }

        public MainWindow(PreferenceStore store)
        {
            this.store = store;
            settings = store.Load();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(688, 616);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = Program.AppName;
            using (Stream iconData = typeof(MainWindow).Assembly.GetManifestResourceStream("GameScreen.ico"))
                windowIcon = new Icon(iconData, SystemInformation.IconSize);
            trayIcon = new Icon(windowIcon, SystemInformation.SmallIconSize);
            using (Icon mark = new Icon(windowIcon, new Size(56, 56)))
                headerImage = mark.ToBitmap();
            Icon = windowIcon;
            BackColor = Canvas;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 10);

            Panel header = new Panel { BackColor = Header, TabStop = false };
            header.SetBounds(0, 0, 688, 112);
            Controls.Add(header);
            PictureBox markView = new PictureBox
            {
                Image = headerImage, SizeMode = PictureBoxSizeMode.Zoom, TabStop = false
            };
            markView.SetBounds(24, 26, 56, 56);
            header.Controls.Add(markView);
            AddLabel(header, Program.AppName, 94, 24, 367, 40, 24, HeaderText, true);
            AddLabel(header, "Choose where your games open.", 96, 65, 378, 24, 10, HeaderSecondary, false);
            Panel statusPill = new Surface(HeaderPill, HighContrast ? Edge : Color.Empty, 15);
            statusPill.SetBounds(495, 42, 166, 31);
            header.Controls.Add(statusPill);
            routingBadge = AddLabel(statusPill, "", 10, 4, 146, 23, 9, HeaderSecondary, true);
            routingBadge.TextAlign = ContentAlignment.MiddleCenter;
            routingBadge.AccessibleName = "Routing status";

            Panel monitorPanel = Card(26, 130, 636, 155);
            AddLabel(monitorPanel, "Gaming display", 19, 12, 480, 30, 14, Ink, true);
            AddLabel(monitorPanel, "Route windowed and borderless games here.", 20, 44, 480, 22, 9, Secondary, false);
            Button refresh = AddButton(monitorPanel, "Refresh", 519, 19, 97, 33, RefreshAll, false);
            monitorPicker.SetBounds(20, 82, 381, 30);
            monitorPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            monitorPicker.BackColor = Field;
            monitorPicker.ForeColor = Ink;
            monitorPicker.AccessibleName = "Gaming display";
            monitorPicker.SelectionChangeCommitted += delegate
            {
                UpdateButtons();
                Monitor selection = monitorPicker.SelectedItem as Monitor;
                if (selection != null)
                    SetStatus("Click Apply to route games to " + selection.Name + ".");
            };
            monitorPanel.Controls.Add(monitorPicker);
            apply = AddButton(monitorPanel, "Apply", 413, 76, 96, 36, ApplyRouting, true);
            pause = AddButton(monitorPanel, "Pause", 519, 76, 97, 36, PauseRouting, false);
            monitorPicker.TabIndex = 0;
            apply.TabIndex = 1;
            pause.TabIndex = 2;
            refresh.TabIndex = 3;
            windowsMain = AddLabel(monitorPanel, "", 20, 119, 596, 22, 9, Secondary, false);

            Panel fullscreenPanel = Card(26, 298, 636, 79);
            AddLabel(fullscreenPanel, "Fullscreen mode", 19, 13, 390, 25, 12, Ink, true);
            AddLabel(fullscreenPanel, "Temporarily make the gaming display your Windows main display.",
                20, 41, 416, 23, 9, Secondary, false);
            fullscreen = AddButton(fullscreenPanel, "Start fullscreen mode", 440, 21, 176, 36, ToggleFullscreen, false);

            Panel gamePanel = Card(26, 390, 636, 154);
            AddLabel(gamePanel, "Game detection", 19, 12, 480, 29, 14, Ink, true);
            discovered = AddLabel(gamePanel, "", 20, 42, 491, 22, 9, Secondary, false);
            addedGames.SetBounds(20, 73, 488, 62);
            addedGames.BackColor = Field;
            addedGames.ForeColor = Ink;
            addedGames.AccessibleName = "Manually added game executables";
            addedGames.HorizontalScrollbar = true;
            addedGames.IntegralHeight = false;
            addedGames.SelectedIndexChanged += delegate { UpdateButtons(); };
            gamePanel.Controls.Add(addedGames);
            emptyGames = AddLabel(gamePanel, "Add an executable for a game from another launcher.",
                20, 73, 488, 62, 9, Secondary, false);
            emptyGames.BackColor = Field;
            emptyGames.BorderStyle = BorderStyle.FixedSingle;
            emptyGames.TextAlign = ContentAlignment.MiddleCenter;
            AddButton(gamePanel, "Add game…", 519, 19, 97, 33, AddGame, false);
            remove = AddButton(gamePanel, "Remove", 519, 73, 97, 33, RemoveGame, false);

            status = AddLabel(this, "", 28, 553, 632, 36, 10, Accent, true);
            AddLabel(this, "GameScreen keeps running in the tray when you close this window.",
                28, 589, 550, 20, 9, Secondary, false);
            ResumeLayout(false);
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open " + Program.AppName, null, delegate { ShowWindow(); });
            trayMenu.Items.Add(trayFullscreen);
            trayFullscreen.Click += delegate { RunAction(ToggleFullscreen); };
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Quit", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = trayIcon, Text = Program.AppName, ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { ShowWindow(); };
            timer.Tick += delegate { Tick(); };

            RunAction(RefreshAll);
            if (settings.PreviousMain != null)
            {
                RunAction(delegate
                {
                    EndFullscreen();
                    SetStatus("Restored the previous Windows main display.");
                });
            }
            timer.Start();
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int width, int height,
            int size, Color color, bool bold)
        {
            Label label = new Label
            {
                Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color, BackColor = parent.BackColor, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            label.SetBounds(x, y, width, height);
            parent.Controls.Add(label);
            return label;
        }

        private Panel Card(int x, int y, int width, int height)
        {
            Panel panel = new Surface(Paper, Edge, 12);
            panel.SetBounds(x, y, width, height);
            Controls.Add(panel);
            return panel;
        }

        private sealed class Surface : Panel
        {
            private readonly Color fill;
            private readonly Color border;
            private readonly int radius;

            public Surface(Color fill, Color border, int radius)
            {
                this.fill = fill;
                this.border = border;
                this.radius = radius;
                BackColor = fill;
                DoubleBuffered = true;
                TabStop = false;
                ResizeRedraw = true;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                using (Brush behind = new SolidBrush(Parent == null ? SystemColors.Control : Parent.BackColor))
                    e.Graphics.FillRectangle(behind, ClientRectangle);
                float d = Math.Min(2 * radius * e.Graphics.DpiX / 96, Height - 1);
                float right = Width - 1;
                float bottom = Height - 1;
                if (right <= d || bottom < d || d <= 0)
                    return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = new GraphicsPath())
                using (Brush brush = new SolidBrush(fill))
                {
                    shape.AddArc(0.5f, 0.5f, d, d, 180, 90);
                    shape.AddArc(right - d, 0.5f, d, d, 270, 90);
                    shape.AddArc(right - d, bottom - d, d, d, 0, 90);
                    shape.AddArc(0.5f, bottom - d, d, d, 90, 90);
                    shape.CloseFigure();
                    e.Graphics.FillPath(brush, shape);
                    if (!border.IsEmpty)
                    {
                        using (Pen pen = new Pen(border))
                            e.Graphics.DrawPath(pen, shape);
                    }
                }
            }
        }

        private Button AddButton(Control parent, string text, int x, int y, int width, int height, Action action, bool primary)
        {
            Button button = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat,
                ForeColor = primary ? (HighContrast ? SystemColors.HighlightText : Color.White) : Ink,
                BackColor = primary ? Accent : ButtonFill,
                UseVisualStyleBackColor = false,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Edge;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : ButtonHover;
            button.SetBounds(x, y, width, height);
            button.Click += delegate { RunAction(action); };
            parent.Controls.Add(button);
            return button;
        }
        private void RunAction(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                SetStatus(error.Message);
                if (!Visible)
                    tray.ShowBalloonTip(6000, Program.AppName, error.Message, ToolTipIcon.Warning);
            }
            finally { UpdateButtons(); }
        }

        private void SetStatus(string message)
        {
            status.Text = message;
            status.AccessibleDescription = message;
        }

        private void RefreshAll()
        {
            RefreshDisplays();
            catalog = GameCatalog.Read();
            discovered.Text = string.Format("{0} Steam and Epic install folders detected.", catalog.Count);
            RefreshGameList();
            checkedWindows.Clear();
            ShowRoutingStatus();
        }

        private void RefreshDisplays()
        {
            Monitor selected = monitorPicker.SelectedItem as Monitor;
            string key = selected == null ? settings.GamingMonitor : selected.Key;
            displays = null;
            displays = Displays.Read();
            monitorPicker.BeginUpdate();
            try
            {
                monitorPicker.Items.Clear();
                monitorPicker.Items.AddRange(displays.Monitors.ToArray());
                Monitor match = displays.Find(key);
                if (match == null && string.IsNullOrEmpty(key))
                    match = displays.Monitors.Find(monitor => monitor.Name.IndexOf("AW2725D", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? displays.Monitors.Find(monitor => !monitor.Primary) ?? displays.Primary;
                monitorPicker.SelectedItem = match;
            }
            finally { monitorPicker.EndUpdate(); }
            windowsMain.Text = "Windows main display  •  " + (displays.Primary == null ? "Unavailable" : displays.Primary.Name);
            UpdateButtons();
        }

        private void RefreshGameList()
        {
            addedGames.Items.Clear();
            addedGames.Items.AddRange(settings.AddedGames.Select(path =>
                Path.GetFileName(path) + "  |  " + Path.GetDirectoryName(path)).ToArray());
            bool empty = addedGames.Items.Count == 0;
            addedGames.Visible = !empty;
            emptyGames.Visible = empty;
        }

        private void UpdateButtons()
        {
            bool session = settings.PreviousMain != null;
            bool selected = monitorPicker.SelectedItem is Monitor;
            monitorPicker.Enabled = !session;
            apply.Enabled = !session && selected;
            pause.Enabled = settings.Routing;
            fullscreen.Enabled = session || selected;
            fullscreen.Text = session ? "End fullscreen mode" : "Start fullscreen mode";
            trayFullscreen.Enabled = fullscreen.Enabled;
            trayFullscreen.Text = fullscreen.Text;
            remove.Enabled = addedGames.SelectedIndex >= 0;

            Monitor target = displays == null ? null : displays.Find(settings.GamingMonitor);
            bool active = settings.Routing && target != null && !target.Mirrored;
            routingBadge.Text = session ? "●  Fullscreen active" :
                active ? "●  Routing active" : settings.Routing ? "●  Waiting for display" : "●  Routing paused";
            routingBadge.ForeColor = session || active ? HeaderActive : HeaderSecondary;
        }

        private void ApplyRouting()
        {
            Monitor selected = (Monitor)monitorPicker.SelectedItem;
            string previousKey = settings.GamingMonitor;
            bool previousRouting = settings.Routing;
            settings.GamingMonitor = selected.Key;
            settings.Routing = true;
            try { store.Save(settings); }
            catch
            {
                settings.GamingMonitor = previousKey;
                settings.Routing = previousRouting;
                throw;
            }
            checkedWindows.Clear();
            ShowRoutingStatus();
        }

        private void PauseRouting()
        {
            settings.Routing = false;
            try { store.Save(settings); }
            catch
            {
                settings.Routing = true;
                throw;
            }
            checkedWindows.Clear();
            ShowRoutingStatus();
        }

        private void ShowRoutingStatus()
        {
            Monitor target = displays == null ? null : displays.Find(settings.GamingMonitor);
            if (settings.PreviousMain != null)
                SetStatus("Fullscreen mode is active. End it after you finish playing.");
            else if (!settings.Routing)
                SetStatus("Select a gaming display and click Apply to enable automatic routing.");
            else if (target == null)
                SetStatus("The saved gaming display is disconnected. Routing resumes when it returns.");
            else if (target.Mirrored)
                SetStatus("Use Extend these displays to route games to a separate monitor.");
            else
                SetStatus("Games are routed to " + target.Name + ".");
        }

        private void AddGame()
        {
            using (OpenFileDialog picker = new OpenFileDialog
            {
                Title = "Select the game executable",
                Filter = "Game executable (*.exe)|*.exe",
                CheckFileExists = true
            })
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                int existing = settings.AddedGames.FindIndex(path => string.Equals(path, picker.FileName, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    addedGames.SelectedIndex = existing;
                    return;
                }
                settings.AddedGames.Add(picker.FileName);
                try { store.Save(settings); }
                catch
                {
                    settings.AddedGames.RemoveAt(settings.AddedGames.Count - 1);
                    throw;
                }
                RefreshGameList();
                addedGames.SelectedIndex = addedGames.Items.Count - 1;
                checkedWindows.Clear();
                SetStatus("Added " + Path.GetFileName(picker.FileName) + " to game detection.");
            }
        }

        private void RemoveGame()
        {
            int index = addedGames.SelectedIndex;
            string path = settings.AddedGames[index];
            settings.AddedGames.RemoveAt(index);
            try { store.Save(settings); }
            catch
            {
                settings.AddedGames.Insert(index, path);
                throw;
            }
            RefreshGameList();
            checkedWindows.Clear();
            ShowRoutingStatus();
        }

        private void ToggleFullscreen()
        {
            if (settings.PreviousMain != null)
            {
                EndFullscreen();
                ShowRoutingStatus();
                return;
            }
            Monitor selection = monitorPicker.SelectedItem as Monitor;
            if (selection == null)
                return;
            Displays live = Displays.Read();
            Monitor target = live.Find(selection.Key);
            if (target == null || live.Primary == null)
                throw new InvalidOperationException("Connect both displays and refresh the monitor list.");
            if (target.Primary)
            {
                SetStatus(target.Name + " is already the Windows main display. Launch your game.");
                return;
            }

            live.SetPrimary(target, true);
            settings.PreviousMain = live.Primary.Key;
            try { store.Save(settings); }
            catch
            {
                settings.PreviousMain = null;
                throw;
            }
            try { live.SetPrimary(target, false); }
            catch (Exception switchError)
            {
                try { EndFullscreen(); }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException(switchError.Message + " Restore pending. " + restoreError.Message, restoreError);
                }
                throw;
            }
            RefreshDisplays();
            SetStatus("Fullscreen mode is active. Launch your game, then end this mode when finished.");
        }

        private void EndFullscreen()
        {
            string previous = settings.PreviousMain;
            if (previous == null)
                return;
            Displays live = Displays.Read();
            Monitor workDisplay = live.Find(previous);
            if (workDisplay == null)
                throw new InvalidOperationException("Reconnect the previous main display, then choose End fullscreen mode.");
            live.SetPrimary(workDisplay, false);
            settings.PreviousMain = null;
            try { store.Save(settings); }
            catch
            {
                settings.PreviousMain = previous;
                throw;
            }
            checkedWindows.Clear();
            RefreshDisplays();
        }

        private void Tick()
        {
            if (layoutChanged)
            {
                layoutChanged = false;
                RunAction(delegate
                {
                    RefreshDisplays();
                    ShowRoutingStatus();
                    checkedWindows.Clear();
                });
            }
            Monitor target = displays == null ? null : displays.Find(settings.GamingMonitor);
            if (!settings.Routing || settings.PreviousMain != null || target == null || target.Mirrored)
                return;

            Dictionary<IntPtr, uint> windows = GameWindow.ReadOpenWindows();
            foreach (IntPtr closed in checkedWindows.Keys.Where(window => !windows.ContainsKey(window)).ToArray())
                checkedWindows.Remove(closed);

            foreach (KeyValuePair<IntPtr, uint> window in windows)
            {
                if (window.Key == Handle)
                    continue;
                OpenGameWindow state;
                if (!checkedWindows.TryGetValue(window.Key, out state) || state.ProcessId != window.Value)
                {
                    bool matched = catalog.Matches(GameWindow.Executable(window.Value), settings.AddedGames);
                    state = new OpenGameWindow { ProcessId = window.Value, AttemptsLeft = matched ? 16 : 0 };
                    checkedWindows[window.Key] = state;
                }
                if (state.AttemptsLeft == 0)
                    continue;
                // Give games time to finish creating or resizing their first window.
                state.AttemptsLeft--;
                int error;
                if (GameWindow.Move(window.Key, target, out error))
                    SetStatus("Game routed to " + target.Name + ".");
                else if (error == AccessDenied)
                {
                    state.AttemptsLeft = 0;
                    const string message = "Windows blocked the move. Quit GameScreen and reopen it as administrator.";
                    SetStatus(message);
                    if (!Visible)
                        tray.ShowBalloonTip(6000, Program.AppName, message, ToolTipIcon.Warning);
                }
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x007e || message.Msg == 0x0219)
                layoutChanged = true;
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void Quit()
        {
            RunAction(delegate
            {
                EndFullscreen();
                allowClose = true;
            });
            if (allowClose)
                Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            bool release = disposing && !IsDisposed;
            if (release)
            {
                timer.Dispose();
                NotifyIcon icon = tray;
                tray = null;
                if (icon != null)
                {
                    ContextMenuStrip menu = icon.ContextMenuStrip;
                    icon.Visible = false;
                    icon.Dispose();
                    if (menu != null)
                        menu.Dispose();
                }
            }
            base.Dispose(disposing);
            if (release)
            {
                if (headerImage != null)
                    headerImage.Dispose();
                if (trayIcon != null)
                    trayIcon.Dispose();
                if (windowIcon != null)
                    windowIcon.Dispose();
            }
        }
    }
}
