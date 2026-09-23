using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GLoader
{
    internal enum LauncherAction
    {
        Cancel,
        Modded,
        Vanilla
    }

    internal sealed class LauncherResult
    {
        public LauncherAction Action { get; set; }
        public bool ShowConsole { get; set; }
    }

    internal sealed class LauncherForm : Form
    {
        private readonly string _modsDirectory;
        private readonly string _logsDirectory;
        private readonly X64RuntimeBuilder _runtimeBuilder;
        private readonly TableLayoutPanel _modsTable;
        private readonly Label _statusLabel;
        private readonly CheckBox _showConsole;
        private readonly Button _runtimeButton;
        private readonly Button _vanillaButton;
        private readonly Button _launchButton;
        private bool _buildingRuntime;

        public LauncherForm(string modsDirectory, string logsDirectory)
        {
            _modsDirectory = Path.GetFullPath(modsDirectory);
            _logsDirectory = Path.GetFullPath(logsDirectory);

            var loaderDirectory = Path.GetDirectoryName(_modsDirectory);
            if (string.IsNullOrWhiteSpace(loaderDirectory))
                throw new InvalidOperationException("Could not determine the gloader installation directory.");

            _runtimeBuilder = new X64RuntimeBuilder(loaderDirectory, _logsDirectory);

            Text = "gloader";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 410);
            Size = new Size(620, 500);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var heading = new Label
            {
                Text = "Mods",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            };
            root.Controls.Add(heading, 0, 0);

            var modsHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0)
            };
            root.Controls.Add(modsHost, 0, 1);

            _modsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Padding = new Padding(4)
            };
            modsHost.Controls.Add(_modsTable);

            _statusLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 8)
            };
            root.Controls.Add(_statusLabel, 0, 2);

            var utilityRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            utilityRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(utilityRow, 0, 3);

            var utilityButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Dock = DockStyle.Fill
            };
            utilityRow.Controls.Add(utilityButtons, 0, 0);

            var modsFolderButton = new Button { Text = "Mods Folder", AutoSize = true };
            modsFolderButton.Click += (sender, args) => OpenDirectory(_modsDirectory);
            utilityButtons.Controls.Add(modsFolderButton);

            var refreshButton = new Button { Text = "Refresh", AutoSize = true };
            refreshButton.Click += (sender, args) =>
            {
                RefreshMods(showErrors: true);
                RefreshRuntimeState();
            };
            utilityButtons.Controls.Add(refreshButton);

            _runtimeButton = new Button { Text = "Build x64 Runtime", AutoSize = true };
            _runtimeButton.Click += BuildRuntimeClicked;
            utilityButtons.Controls.Add(_runtimeButton);

            var logsButton = new Button { Text = "Logs ▾", AutoSize = true };
            logsButton.Click += (sender, args) => ShowLogsMenu(logsButton);
            utilityButtons.Controls.Add(logsButton);

            _showConsole = new CheckBox
            {
                Text = "Show console",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(12, 5, 0, 0)
            };
            utilityRow.Controls.Add(_showConsole, 1, 0);

            var launchRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0)
            };
            launchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            launchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.Controls.Add(launchRow, 0, 4);

            _vanillaButton = new Button
            {
                Text = "Launch Vanilla",
                Dock = DockStyle.Fill,
                Height = 34,
                Margin = new Padding(0, 0, 4, 0)
            };
            _vanillaButton.Click += (sender, args) => Finish(LauncherAction.Vanilla);
            launchRow.Controls.Add(_vanillaButton, 0, 0);

            _launchButton = new Button
            {
                Text = "Launch Terraria",
                Dock = DockStyle.Fill,
                Height = 34,
                Margin = new Padding(4, 0, 0, 0)
            };
            _launchButton.Click += (sender, args) => Finish(LauncherAction.Modded);
            launchRow.Controls.Add(_launchButton, 1, 0);
            AcceptButton = _launchButton;

            RefreshMods(showErrors: false);
            RefreshRuntimeState();
        }

        public LauncherAction SelectedAction { get; private set; } = LauncherAction.Cancel;

        public static LauncherResult ShowLauncher(string modsDirectory, string logsDirectory)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var form = new LauncherForm(modsDirectory, logsDirectory))
            {
                form.ShowDialog();
                return new LauncherResult
                {
                    Action = form.SelectedAction,
                    ShowConsole = form._showConsole.Checked
                };
            }
        }

        public static void ShowStartupFailure(string logsDirectory, Exception exception)
        {
            var latestLog = FindLatestLog(logsDirectory);
            var message = "gloader failed to start Terraria.\r\n\r\n" + exception.Message;

            if (latestLog == null)
            {
                MessageBox.Show(message, "gloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            message += "\r\n\r\nOpen the latest log?";
            if (MessageBox.Show(message, "gloader", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
            {
                OpenPath(latestLog);
            }
        }

        private void RefreshMods(bool showErrors)
        {
            try
            {
                var mods = ModManager.Discover(_modsDirectory);

                _modsTable.SuspendLayout();
                try
                {
                    while (_modsTable.Controls.Count > 0)
                    {
                        var control = _modsTable.Controls[0];
                        _modsTable.Controls.RemoveAt(0);
                        control.Dispose();
                    }

                    _modsTable.RowStyles.Clear();
                    _modsTable.RowCount = 0;

                    if (mods.Count == 0)
                    {
                        var empty = new Label
                        {
                            Text = "No mods found in gmods.",
                            AutoSize = true,
                            Margin = new Padding(6, 8, 6, 8)
                        };
                        _modsTable.Controls.Add(empty, 0, 0);
                    }
                    else
                    {
                        foreach (var mod in mods)
                        {
                            var row = new ModRow(this, mod)
                            {
                                Dock = DockStyle.Top,
                                Margin = new Padding(0)
                            };
                            _modsTable.RowCount++;
                            _modsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                            _modsTable.Controls.Add(row, 0, _modsTable.RowCount - 1);
                        }
                    }
                }
                finally
                {
                    _modsTable.ResumeLayout(true);
                }

                UpdateStatus();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Could not read gmods.";
                if (showErrors)
                {
                    MessageBox.Show(
                        "Could not refresh the mod list.\r\n\r\n" + ex.Message,
                        "gloader",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateStatus()
        {
            var rows = _modsTable.Controls.OfType<ModRow>().ToArray();
            var enabled = rows.Count(row => row.Mod.Enabled && !row.Mod.HasConflict);
            var conflicts = rows.Count(row => row.Mod.HasConflict);

            string runtimeStatus;
            if (_buildingRuntime)
                runtimeStatus = "Building x64 runtime...";
            else if (_runtimeBuilder.IsReady)
                runtimeStatus = "x64 runtime ready";
            else if (_runtimeBuilder.CanBuild)
                runtimeStatus = "x64 runtime missing";
            else
                runtimeStatus = "x64 runtime missing — gloader must be beside Terraria.exe";

            _statusLabel.Text = runtimeStatus + "   •   " + enabled + " / " + rows.Length + " mods enabled";
            if (conflicts > 0)
            {
                _statusLabel.Text += "   •   " + conflicts + " conflict" + (conflicts == 1 ? string.Empty : "s");
            }
        }

        private void RefreshRuntimeState()
        {
            var ready = _runtimeBuilder.IsReady;
            _runtimeButton.Visible = !ready;
            _runtimeButton.Enabled = !_buildingRuntime && _runtimeBuilder.CanBuild;
            _runtimeButton.Text = _buildingRuntime ? "Building x64 Runtime..." : "Build x64 Runtime";
            _vanillaButton.Enabled = ready && !_buildingRuntime;
            _launchButton.Enabled = ready && !_buildingRuntime;
            UpdateStatus();
        }

        private async void BuildRuntimeClicked(object sender, EventArgs args)
        {
            if (_buildingRuntime)
                return;

            _buildingRuntime = true;
            RefreshRuntimeState();

            try
            {
                var result = await _runtimeBuilder.BuildAsync();
                if (result.Success)
                {
                    MessageBox.Show(
                        "64-bit Terraria runtime built successfully.\r\n\r\ngloader will use it automatically.",
                        "gloader",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    var message = "The 64-bit Terraria runtime build failed.";
                    if (!string.IsNullOrWhiteSpace(result.LastMessage))
                        message += "\r\n\r\n" + result.LastMessage;
                    message += "\r\n\r\nOpen the build log?";

                    if (MessageBox.Show(message, "gloader", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                        OpenPath(result.LogPath);
                }
            }
            catch (Exception ex)
            {
                var message = "Could not build the 64-bit Terraria runtime.\r\n\r\n" + ex.Message;
                if (File.Exists(_runtimeBuilder.LogPath))
                {
                    message += "\r\n\r\nOpen the build log?";
                    if (MessageBox.Show(message, "gloader", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                        OpenPath(_runtimeBuilder.LogPath);
                }
                else
                {
                    MessageBox.Show(message, "gloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _buildingRuntime = false;
                RefreshRuntimeState();
            }
        }

        private void ShowLogsMenu(Button owner)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open latest", null, (sender, args) => OpenLatestLog());
            menu.Items.Add("Client log", null, (sender, args) => OpenLog("gloader-client.log"));
            menu.Items.Add("Server log", null, (sender, args) => OpenLog("gloader-server.log"));
            menu.Items.Add("x64 runtime build log", null, (sender, args) => OpenLog("x64-runtime-build.log"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open logs folder", null, (sender, args) => OpenDirectory(_logsDirectory));
            menu.Closed += (sender, args) => menu.Dispose();
            menu.Show(owner, new Point(0, owner.Height));
        }

        private void OpenLatestLog()
        {
            var path = FindLatestLog(_logsDirectory);
            if (path == null)
            {
                MessageBox.Show("No gloader logs exist yet.", "gloader", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            OpenPath(path);
        }

        private void OpenLog(string fileName)
        {
            var path = Path.Combine(_logsDirectory, fileName);
            if (!File.Exists(path))
            {
                MessageBox.Show(fileName + " does not exist yet.", "gloader", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            OpenPath(path);
        }

        private static string FindLatestLog(string logsDirectory)
        {
            if (!Directory.Exists(logsDirectory))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(logsDirectory, "gloader-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static void OpenDirectory(string path)
        {
            Directory.CreateDirectory(path);
            OpenPath(path);
        }

        private static void OpenPath(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        private void Finish(LauncherAction action)
        {
            if (!_runtimeBuilder.IsReady)
            {
                MessageBox.Show(
                    "Build the 64-bit Terraria runtime first.",
                    "gloader",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SelectedAction = action;
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class ModRow : UserControl
        {
            private readonly LauncherForm _owner;
            private readonly CheckBox _enabled;
            private readonly Button _configure;
            private bool _changing;

            public ModRow(LauncherForm owner, ManagedMod mod)
            {
                _owner = owner;
                Mod = mod;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 3,
                    RowCount = 1,
                    Padding = new Padding(2)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                Controls.Add(layout);

                _enabled = new CheckBox
                {
                    Text = mod.Name,
                    Checked = mod.Enabled,
                    Enabled = !mod.HasConflict,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(4, 7, 8, 7)
                };
                _enabled.CheckedChanged += ModEnabledChanged;
                layout.Controls.Add(_enabled, 0, 0);

                var conflict = new Label
                {
                    Text = mod.HasConflict ? "Conflict" : string.Empty,
                    AutoSize = true,
                    ForeColor = Color.Firebrick,
                    Anchor = AnchorStyles.Right,
                    Margin = new Padding(8, 8, 8, 0)
                };
                layout.Controls.Add(conflict, 1, 0);

                _configure = new Button
                {
                    Text = "Configure",
                    AutoSize = true,
                    Anchor = AnchorStyles.Right,
                    Visible = ModManager.GetConfigurationFiles(mod).Count > 0,
                    Margin = new Padding(4, 3, 4, 3)
                };
                _configure.Click += ConfigureClicked;
                layout.Controls.Add(_configure, 2, 0);
            }

            public ManagedMod Mod { get; }

            private void ModEnabledChanged(object sender, EventArgs args)
            {
                if (_changing)
                {
                    return;
                }

                try
                {
                    ModManager.SetEnabled(_owner._modsDirectory, Mod, _enabled.Checked);
                    _configure.Visible = ModManager.GetConfigurationFiles(Mod).Count > 0;
                    _owner.UpdateStatus();
                }
                catch (Exception ex)
                {
                    _changing = true;
                    try
                    {
                        _enabled.Checked = Mod.Enabled;
                    }
                    finally
                    {
                        _changing = false;
                    }

                    MessageBox.Show(
                        "Could not change " + Mod.Name + ".\r\n\r\n" + ex.Message,
                        "gloader",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }

            private void ConfigureClicked(object sender, EventArgs args)
            {
                var files = ModManager.GetConfigurationFiles(Mod);
                if (files.Count == 0)
                {
                    _configure.Visible = false;
                    return;
                }

                if (files.Count == 1)
                {
                    OpenPath(files[0]);
                    return;
                }

                var menu = new ContextMenuStrip();
                foreach (var path in files)
                {
                    var capturedPath = path;
                    menu.Items.Add(Path.GetFileName(path), null, (itemSender, itemArgs) => OpenPath(capturedPath));
                }

                menu.Closed += (itemSender, itemArgs) => menu.Dispose();
                menu.Show(_configure, new Point(0, _configure.Height));
            }
        }
    }
}
