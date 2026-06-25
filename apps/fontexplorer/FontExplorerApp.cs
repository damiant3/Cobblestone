using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexFontExplorer
{
    public class FontExplorerForm : Form
    {
        private PrivateFontCollection _pfc;
        private FontFamily _currentFamily;
        private string _fontsDir;
        private string _weightsFile;
        private Label _weightsLabel;

        private ComboBox _fontCombo;
        private Label _fontPathLabel;
        private TrackBar _weightSlider;
        private Label _weightVal;
        private TrackBar _sizeSlider;
        private Label _sizeVal;
        private TrackBar _tempSlider;
        private Label _tempVal;
        private CheckBox _boldCheck;
        private CheckBox _italicCheck;
        private CheckBox _lightCheck;
        private Button _generateBtn;
        private ProgressBar _progressBar;
        private Label _statusLabel;
        private TabControl _tabs;
        private Panel _previewPanel;
        private TextBox _sampleBox;

        private ListBox _trainFontList;
        private Button _addTrainFontsBtn;
        private Button _removeTrainFontBtn;
        private TrackBar _epochSlider;
        private Label _epochVal;
        private TrackBar _lrSlider;
        private Label _lrVal;
        private Button _trainBtn;
        private ProgressBar _trainProgress;
        private Label _trainStatus;
        private TextBox _trainLog;
        private Process _trainProcess;

        private static readonly int[] PreviewSizes = { 12, 18, 24, 36, 48, 72 };

        public FontExplorerForm()
        {
            Text = "Codex Font Explorer";
            Size = new Size(1280, 860);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(212, 212, 212);
            DoubleBuffered = true;

            var repoRoot = FindRepoRoot();
            _fontsDir = Path.Combine(repoRoot ?? ".", "apps", "fontai");
            BuildUI();
            ScanFonts();
            InitGpu();
        }

        private void InitGpu()
        {
            if (GpuInit())
            {
                var procPath = Environment.ProcessPath ?? "?";
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var cwd = Directory.GetCurrentDirectory();
                var candidates = new List<string>();
                try { candidates.Add(Path.Combine(Path.GetDirectoryName(procPath), "mlp.ptx")); } catch { }
                candidates.Add(Path.Combine(baseDir, "mlp.ptx"));
                candidates.Add(Path.Combine(cwd, "mlp.ptx"));
                candidates.Add(Path.Combine(_fontsDir, "kernels", "mlp.ptx"));
                candidates.Add(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "test-minimal.ptx"));
                string ptxPath = null;
                foreach (var c in candidates) { if (File.Exists(c)) { ptxPath = c; break; } }
                if (ptxPath != null && GpuLoadPtx(ptxPath))
                {
                    _statusLabel.Text = _cudaError + " + PTX loaded";
                }
                else
                {
                    var log = "ptxPath: " + (ptxPath ?? "null") + "\n"
                            + "cudaError: " + _cudaError + "\n"
                            + string.Join("\n", candidates.Select(c => (File.Exists(c) ? "FOUND " : "MISS  ") + c));
                    File.WriteAllText(Path.Combine(cwd, "gpu-debug.txt"), log);
                    _statusLabel.Text = _cudaError;
                }
                _statusLabel.ForeColor = Color.FromArgb(100, 200, 100);
            }
            else
            {
                _statusLabel.Text = _cudaError + " -- CPU fallback";
                _statusLabel.ForeColor = Color.FromArgb(200, 200, 100);
            }
        }

        private string FindRepoRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                if (File.Exists(Path.Combine(dir, "CLAUDE.md"))) return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CodexFontAI");
            if (Directory.Exists(desktop)) return null;
            return null;
        }

        private void BuildUI()
        {
            var leftPanel = new Panel
            {
                Dock = DockStyle.Left, Width = 320,
                BackColor = Color.FromArgb(37, 37, 38),
                Padding = new Padding(12), AutoScroll = true
            };

            int y = 8;

            var title = MakeLabel("Codex Font Explorer", 14, FontStyle.Bold);
            title.Location = new Point(12, y); title.AutoSize = true;
            leftPanel.Controls.Add(title);
            y += 36;

            leftPanel.Controls.Add(MakeSectionLabel("PREVIEW FONT", y)); y += 22;

            _fontCombo = new ComboBox
            {
                Location = new Point(12, y), Width = 280,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _fontCombo.SelectedIndexChanged += (s, e) => LoadSelectedFont();
            leftPanel.Controls.Add(_fontCombo);
            y += 30;

            var loadTtfBtn = MakeButton("Load TTF...", 280);
            loadTtfBtn.Location = new Point(12, y);
            loadTtfBtn.Click += BrowseFonts_Click;
            leftPanel.Controls.Add(loadTtfBtn);
            y += 32;

            _fontPathLabel = new Label
            {
                Location = new Point(12, y), Width = 280, Height = 16,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 7.5f), AutoEllipsis = true,
                Text = "No font loaded -- use Load TTF or Generate"
            };
            leftPanel.Controls.Add(_fontPathLabel);
            y += 22;

            leftPanel.Controls.Add(MakeSectionLabel("STYLE", y)); y += 22;

            _boldCheck = MakeCheckBox("Bold", 12, y); leftPanel.Controls.Add(_boldCheck);
            _italicCheck = MakeCheckBox("Italic", 100, y); leftPanel.Controls.Add(_italicCheck);
            _lightCheck = MakeCheckBox("Light", 188, y); leftPanel.Controls.Add(_lightCheck);
            _boldCheck.CheckedChanged += (s, e) => _previewPanel.Invalidate();
            _italicCheck.CheckedChanged += (s, e) => _previewPanel.Invalidate();
            _lightCheck.CheckedChanged += (s, e) => _previewPanel.Invalidate();
            y += 28;

            y = AddSlider(leftPanel, "Weight", 100, 900, 400, y, out _weightSlider, out _weightVal);
            y = AddSlider(leftPanel, "Temperature", 0, 100, 0, y, out _tempSlider, out _tempVal);

            leftPanel.Controls.Add(MakeSectionLabel("PREVIEW SIZE", y)); y += 22;
            y = AddSlider(leftPanel, "Size", 8, 120, 36, y, out _sizeSlider, out _sizeVal);
            _sizeSlider.ValueChanged += (s, e) => _previewPanel.Invalidate();

            y += 8;
            leftPanel.Controls.Add(MakeSectionLabel("GENERATE", y)); y += 22;

            var loadWeightsBtn = MakeButton("Load Weights File...", 280);
            loadWeightsBtn.Location = new Point(12, y);
            loadWeightsBtn.Click += LoadWeights_Click;
            leftPanel.Controls.Add(loadWeightsBtn);
            y += 32;

            _weightsLabel = new Label
            {
                Location = new Point(12, y), Width = 280, Height = 16,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 7.5f), AutoEllipsis = true,
                Text = "No weights loaded -- train a model first"
            };
            leftPanel.Controls.Add(_weightsLabel);
            y += 22;

            _generateBtn = MakeButton("Generate Font", 280);
            _generateBtn.Location = new Point(12, y);
            _generateBtn.Height = 36;
            _generateBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _generateBtn.BackColor = Color.FromArgb(0, 122, 204);
            _generateBtn.Click += Generate_Click;
            leftPanel.Controls.Add(_generateBtn);
            y += 44;

            _progressBar = new ProgressBar
            {
                Location = new Point(12, y), Width = 280, Height = 18,
                Style = ProgressBarStyle.Continuous
            };
            leftPanel.Controls.Add(_progressBar);
            y += 24;

            _statusLabel = new Label
            {
                Location = new Point(12, y), Width = 280, Height = 16,
                ForeColor = Color.FromArgb(100, 180, 100),
                Font = new Font("Segoe UI", 8f), Text = "Ready"
            };
            leftPanel.Controls.Add(_statusLabel);
            y += 28;

            leftPanel.Controls.Add(MakeSectionLabel("SAMPLE TEXT", y)); y += 22;
            _sampleBox = new TextBox
            {
                Location = new Point(12, y), Width = 280, Height = 60,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f),
                Text = "The quick brown fox jumps over the lazy dog"
            };
            _sampleBox.TextChanged += (s, e) => _previewPanel.Invalidate();
            leftPanel.Controls.Add(_sampleBox);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f)
            };

            var previewTab = new TabPage("Preview") { BackColor = Color.FromArgb(30, 30, 30) };
            _previewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                AutoScroll = true
            };
            _previewPanel.Paint += PreviewPanel_Paint;
            _previewPanel.Resize += (s, e) => _previewPanel.Invalidate();
            previewTab.Controls.Add(_previewPanel);

            var trainTab = new TabPage("Training") { BackColor = Color.FromArgb(30, 30, 30) };
            BuildTrainingTab(trainTab);

            _tabs.TabPages.Add(previewTab);
            _tabs.TabPages.Add(trainTab);

            Controls.Add(_tabs);
            Controls.Add(leftPanel);
        }

        private void BuildTrainingTab(TabPage tab)
        {
            var topPanel = new Panel
            {
                Dock = DockStyle.Top, Height = 260,
                BackColor = Color.FromArgb(37, 37, 38),
                Padding = new Padding(12)
            };

            int y = 8;
            topPanel.Controls.Add(new Label
            {
                Text = "TRAINING FONTS", Location = new Point(12, y),
                AutoSize = true, ForeColor = Color.FromArgb(0, 122, 204),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            });
            y += 22;

            _trainFontList = new ListBox
            {
                Location = new Point(12, y), Width = 400, Height = 100,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            topPanel.Controls.Add(_trainFontList);

            var btnPanel = new FlowLayoutPanel
            {
                Location = new Point(420, y), Width = 160, Height = 100,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.Transparent, WrapContents = false
            };
            _addTrainFontsBtn = MakeButton("Add Fonts...", 150);
            _addTrainFontsBtn.Click += AddTrainFonts_Click;
            _removeTrainFontBtn = MakeButton("Remove Selected", 150);
            _removeTrainFontBtn.Click += (s, e) =>
            {
                if (_trainFontList.SelectedIndex >= 0)
                    _trainFontList.Items.RemoveAt(_trainFontList.SelectedIndex);
            };
            var addDirBtn = MakeButton("Add Directory...", 150);
            addDirBtn.Click += AddTrainDir_Click;
            var removeAllBtn = MakeButton("Remove All", 150);
            removeAllBtn.Click += (s, e) => { _trainFontList.Items.Clear(); };
            btnPanel.Controls.AddRange(new Control[] { _addTrainFontsBtn, _removeTrainFontBtn, addDirBtn, removeAllBtn });
            topPanel.Controls.Add(btnPanel);
            y += 108;

            topPanel.Controls.Add(new Label
            {
                Text = "PARAMETERS", Location = new Point(12, y),
                AutoSize = true, ForeColor = Color.FromArgb(0, 122, 204),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            });
            y += 20;

            var paramPanel = new FlowLayoutPanel
            {
                Location = new Point(12, y), Width = 580, Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent, WrapContents = false
            };

            paramPanel.Controls.Add(new Label { Text = "Epochs:", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 9f), Padding = new Padding(0, 6, 0, 0) });
            _epochSlider = new TrackBar { Width = 140, Minimum = 100, Maximum = 10000, Value = 2000, TickFrequency = 1000, BackColor = Color.FromArgb(37, 37, 38) };
            _epochVal = new Label { Text = "2000", AutoSize = true, ForeColor = Color.FromArgb(0, 180, 255), Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(0, 6, 8, 0) };
            _epochSlider.ValueChanged += (s, e) => _epochVal.Text = _epochSlider.Value.ToString();

            paramPanel.Controls.Add(_epochSlider);
            paramPanel.Controls.Add(_epochVal);

            paramPanel.Controls.Add(new Label { Text = "LR:", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 9f), Padding = new Padding(8, 6, 0, 0) });
            _lrSlider = new TrackBar { Width = 100, Minimum = 1, Maximum = 100, Value = 10, TickFrequency = 10, BackColor = Color.FromArgb(37, 37, 38) };
            _lrVal = new Label { Text = "0.0010", AutoSize = true, ForeColor = Color.FromArgb(0, 180, 255), Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(0, 6, 0, 0) };
            _lrSlider.ValueChanged += (s, e) => _lrVal.Text = (_lrSlider.Value / 10000.0).ToString("F4");

            paramPanel.Controls.Add(_lrSlider);
            paramPanel.Controls.Add(_lrVal);
            topPanel.Controls.Add(paramPanel);
            y += 48;

            var trainRow = new FlowLayoutPanel
            {
                Location = new Point(12, y), Width = 580, Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent, WrapContents = false
            };

            _trainBtn = new Button
            {
                Text = "Start Training", Width = 140, Height = 36,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(40, 160, 40), ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _trainBtn.Click += Train_Click;
            trainRow.Controls.Add(_trainBtn);

            _trainProgress = new ProgressBar { Width = 300, Height = 24, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Margin = new Padding(8, 6, 0, 0) };
            trainRow.Controls.Add(_trainProgress);

            _trainStatus = new Label
            {
                Text = "Idle", AutoSize = true,
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(8, 8, 0, 0)
            };
            trainRow.Controls.Add(_trainStatus);
            topPanel.Controls.Add(trainRow);

            _trainLog = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(180, 220, 180),
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None
            };

            tab.Controls.Add(_trainLog);
            tab.Controls.Add(topPanel);
        }

        private void AddTrainFonts_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "TrueType Fonts|*.ttf;*.ttc|All Files|*.*",
                Multiselect = true, Title = "Select training fonts"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                foreach (var f in dlg.FileNames)
                    _trainFontList.Items.Add(f);
        }

        private void AddTrainDir_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Select folder with TTF fonts" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var fonts = Directory.GetFiles(dlg.SelectedPath, "*.ttf")
                    .Concat(Directory.GetFiles(dlg.SelectedPath, "*.ttc"));
                foreach (var f in fonts)
                    _trainFontList.Items.Add(f);
                _trainStatus.Text = $"Added {fonts.Count()} fonts from {dlg.SelectedPath}";
            }
        }

        private volatile bool _gpuTrainStop;

        private async void Train_Click(object sender, EventArgs e)
        {
            if (_gpuTrainStop == false && _trainBtn.Text == "Stop Training")
            {
                _gpuTrainStop = true;
                return;
            }

            if (_trainProcess != null && !_trainProcess.HasExited)
            {
                _trainProcess.Kill();
                _trainBtn.Text = "Start Training";
                _trainBtn.BackColor = Color.FromArgb(40, 160, 40);
                _trainProgress.MarqueeAnimationSpeed = 0;
                _trainStatus.Text = "Stopped";
                _trainProcess = null;
                return;
            }

            if (_cudaReady && _cudaModule != IntPtr.Zero)
            {
                await GpuTrainAsync();
                return;
            }

            if (_trainFontList.Items.Count == 0)
            {
                _trainStatus.Text = "Add training fonts first";
                _trainStatus.ForeColor = Color.FromArgb(255, 100, 100);
                return;
            }

            var repoRoot = FindRepoRoot();
            var script = repoRoot != null
                ? Path.Combine(repoRoot, "apps", "fontai", "train.ps1")
                : null;

            if (script == null || !File.Exists(script))
            {
                _trainStatus.Text = "train.ps1 not found";
                _trainStatus.ForeColor = Color.FromArgb(255, 100, 100);
                return;
            }

            var stageDir = Path.Combine(Path.GetTempPath(), "codex-fontai-train-" + DateTime.Now.ToString("HHmmss"));
            Directory.CreateDirectory(stageDir);
            foreach (var item in _trainFontList.Items)
            {
                var src = item.ToString();
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(stageDir, Path.GetFileName(src)), true);
            }
            _trainLog.AppendText($"Staged {_trainFontList.Items.Count} fonts to {stageDir}\r\n");

            var outDir = Path.Combine(_fontsDir, "trained-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(outDir);

            _trainBtn.Text = "Stop Training";
            _trainBtn.BackColor = Color.FromArgb(200, 60, 60);
            _trainProgress.MarqueeAnimationSpeed = 30;
            _trainStatus.Text = "Training...";
            _trainStatus.ForeColor = Color.FromArgb(255, 200, 50);
            _trainLog.Clear();

            var epochs = _epochSlider.Value;
            var lr = _lrSlider.Value / 10000.0;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = $"-NoProfile -File \"{script}\" -FontDir \"{stageDir}\" -Epochs {epochs} -OutDir \"{outDir}\"",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                _trainProcess = Process.Start(psi);

                _ = Task.Run(async () =>
                {
                    while (!_trainProcess.HasExited)
                    {
                        var line = await _trainProcess.StandardOutput.ReadLineAsync();
                        if (line != null)
                            BeginInvoke((Action)(() =>
                            {
                                _trainLog.AppendText(line + Environment.NewLine);
                                if (line.Contains("epoch") || line.Contains("loss"))
                                    _trainStatus.Text = line.Length > 60 ? line.Substring(0, 60) + "..." : line;
                            }));
                    }
                });

                await _trainProcess.WaitForExitAsync();

                _trainProgress.MarqueeAnimationSpeed = 0;
                _trainBtn.Text = "Start Training";
                _trainBtn.BackColor = Color.FromArgb(40, 160, 40);

                if (_trainProcess.ExitCode == 0)
                {
                    _trainStatus.Text = $"Done! Output: {outDir}";
                    _trainStatus.ForeColor = Color.FromArgb(100, 200, 100);
                    var wf = Directory.GetFiles(outDir, "*.codex")
                        .Where(f => f.Contains("Weight")).FirstOrDefault();
                    if (wf != null)
                    {
                        _weightsFile = wf;
                        _weightsLabel.Text = Path.GetFileName(wf);
                        _weightsLabel.ForeColor = Color.FromArgb(100, 200, 100);
                        _statusLabel.Text = $"Weights auto-loaded: {Path.GetFileName(wf)}";
                    }
                }
                else
                {
                    _trainStatus.Text = $"Failed (exit {_trainProcess.ExitCode})";
                    _trainStatus.ForeColor = Color.FromArgb(255, 100, 100);
                }
            }
            catch (Exception ex)
            {
                _trainProgress.MarqueeAnimationSpeed = 0;
                _trainBtn.Text = "Start Training";
                _trainBtn.BackColor = Color.FromArgb(40, 160, 40);
                _trainStatus.Text = $"Error: {ex.Message}";
                _trainStatus.ForeColor = Color.FromArgb(255, 100, 100);
            }
            _trainProcess = null;
        }

        private Label MakeLabel(string text, float size, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text, AutoSize = true,
                Font = new Font("Segoe UI", size, style),
                ForeColor = Color.FromArgb(212, 212, 212)
            };
        }

        private Label MakeSectionLabel(string text, int y)
        {
            return new Label
            {
                Text = text, Location = new Point(12, y),
                AutoSize = true, ForeColor = Color.FromArgb(0, 122, 204),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold)
            };
        }

        private Button MakeButton(string text, int width)
        {
            return new Button
            {
                Text = text, Width = width, Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
        }

        private CheckBox MakeCheckBox(string text, int x, int y)
        {
            return new CheckBox
            {
                Text = text, Location = new Point(x, y),
                AutoSize = true, ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f)
            };
        }

        private int AddSlider(Panel parent, string label, int min, int max, int val, int y,
            out TrackBar slider, out Label valLabel)
        {
            var lbl = new Label
            {
                Text = label, Location = new Point(12, y),
                AutoSize = true, ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8.5f)
            };
            parent.Controls.Add(lbl);

            valLabel = new Label
            {
                Text = val.ToString(), Location = new Point(250, y),
                Width = 42, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(0, 180, 255),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            parent.Controls.Add(valLabel);
            y += 18;

            var tb = new TrackBar
            {
                Location = new Point(12, y), Width = 280,
                Minimum = min, Maximum = max, Value = val,
                TickFrequency = (max - min) / 10,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            var vl = valLabel;
            tb.ValueChanged += (s, e) => { vl.Text = tb.Value.ToString(); };
            parent.Controls.Add(tb);
            slider = tb;
            return y + 42;
        }

        private void ScanFonts()
        {
            _fontCombo.Items.Clear();

            var dirs = new List<string>();
            if (Directory.Exists(_fontsDir)) dirs.Add(_fontsDir);
            var genDir = Path.Combine(_fontsDir, "generated");
            if (Directory.Exists(genDir)) dirs.Add(genDir);
            var desktopAI = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CodexFontAI");
            if (Directory.Exists(desktopAI)) dirs.Add(desktopAI);
            var desktopGen = Path.Combine(desktopAI, "generated");
            if (Directory.Exists(desktopGen)) dirs.Add(desktopGen);

            var fonts = new List<string>();
            foreach (var dir in dirs.Distinct())
            {
                try { fonts.AddRange(Directory.GetFiles(dir, "*.ttf")); } catch { }
            }

            if (fonts.Count == 0)
            {
                _fontPathLabel.Text = "No TTF files found. Use Browse.";
                return;
            }

            foreach (var f in fonts.OrderBy(f => f))
                _fontCombo.Items.Add(new FontFileItem(f));

            if (_fontCombo.Items.Count > 0)
                _fontCombo.SelectedIndex = 0;
        }

        private void LoadSelectedFont()
        {
            if (_fontCombo.SelectedItem is not FontFileItem item) return;
            LoadFont(item.Path);
        }

        private void LoadFont(string path)
        {
            try
            {
                _pfc?.Dispose();
                _pfc = new PrivateFontCollection();
                _pfc.AddFontFile(path);
                _currentFamily = _pfc.Families.Length > 0 ? _pfc.Families[0] : null;
                _fontPathLabel.Text = path;
                _statusLabel.Text = _currentFamily != null
                    ? $"Loaded: {_currentFamily.Name}"
                    : "Failed to load font family";
                _previewPanel.Invalidate();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Error: {ex.Message}";
                _currentFamily = null;
                _previewPanel.Invalidate();
            }
        }

        private void BrowseFonts_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "TrueType Fonts|*.ttf|All Files|*.*",
                Title = "Select a font file",
                InitialDirectory = _fontsDir
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _fontsDir = Path.GetDirectoryName(dlg.FileName);
                ScanFonts();
                for (int i = 0; i < _fontCombo.Items.Count; i++)
                {
                    if (((FontFileItem)_fontCombo.Items[i]).Path == dlg.FileName)
                    { _fontCombo.SelectedIndex = i; break; }
                }
            }
        }

        private void LoadWeights_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Codex Weights|*.codex|JSON Weights|*.json|All Files|*.*",
                Title = "Select a trained weights file",
                InitialDirectory = _fontsDir
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _weightsFile = dlg.FileName;
                _weightsLabel.Text = Path.GetFileName(_weightsFile);
                _weightsLabel.ForeColor = Color.FromArgb(100, 200, 100);
                _statusLabel.Text = $"Weights: {Path.GetFileName(_weightsFile)}";
            }
        }

        private async void Generate_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_weightsFile) || !File.Exists(_weightsFile))
            {
                _statusLabel.Text = "Load a weights file first (Load Weights File...)";
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                return;
            }

            _generateBtn.Enabled = false;
            _progressBar.Value = 0;
            _statusLabel.Text = "Generating...";
            _statusLabel.ForeColor = Color.FromArgb(255, 200, 50);

            var repoRoot = FindRepoRoot();
            var weightsDir = Path.GetDirectoryName(_weightsFile);
            var script = repoRoot != null
                ? Path.Combine(repoRoot, "apps", "fontai", "generate.ps1")
                : Path.Combine(weightsDir, "generate.ps1");

            if (!File.Exists(script))
            {
                _statusLabel.Text = "generate.ps1 not found";
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                _generateBtn.Enabled = true;
                return;
            }

            var outDir = Path.Combine(weightsDir, "generated");
            Directory.CreateDirectory(outDir);

            var weight = _weightSlider.Value;
            var temp = _tempSlider.Value;
            var styles = "";
            if (_boldCheck.Checked) styles += "Bold";
            if (_italicCheck.Checked) styles += (styles.Length > 0 ? "," : "") + "Italic";
            if (_lightCheck.Checked) styles += (styles.Length > 0 ? "," : "") + "Light";
            if (styles.Length == 0) styles = "Regular";

            _progressBar.Style = ProgressBarStyle.Marquee;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = $"-NoProfile -File \"{script}\" -WeightsFile \"{_weightsFile}\" -OutDir \"{outDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var proc = Process.Start(psi);
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = 100;

                if (proc.ExitCode == 0)
                {
                    _statusLabel.Text = "Generated! Scanning for new fonts...";
                    _statusLabel.ForeColor = Color.FromArgb(100, 200, 100);
                    _fontsDir = outDir;
                    ScanFonts();
                    if (_fontCombo.Items.Count > 0)
                        _fontCombo.SelectedIndex = _fontCombo.Items.Count - 1;
                }
                else
                {
                    var errMsg = stderr.Length > 0 ? stderr.Trim() : stdout.Trim();
                    if (errMsg.Length > 120) errMsg = errMsg.Substring(0, 120) + "...";
                    _statusLabel.Text = $"Failed (exit {proc.ExitCode}): {errMsg}";
                    _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                }
            }
            catch (Exception ex)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _statusLabel.Text = $"Error: {ex.Message}";
                _statusLabel.ForeColor = Color.FromArgb(255, 100, 100);
            }
            finally
            {
                _generateBtn.Enabled = true;
            }
        }

        private void PreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.FromArgb(30, 30, 30));

            var sample = _sampleBox?.Text ?? "The quick brown fox jumps over the lazy dog";
            var singleSize = _sizeSlider?.Value ?? 36;

            var style = FontStyle.Regular;
            if (_boldCheck?.Checked == true) style |= FontStyle.Bold;
            if (_italicCheck?.Checked == true) style |= FontStyle.Italic;

            float y = 16;
            var labelBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
            var textBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            var dimBrush = new SolidBrush(Color.FromArgb(140, 140, 140));
            var accentBrush = new SolidBrush(Color.FromArgb(0, 150, 220));
            var labelFont = new Font("Segoe UI", 9f);
            var headerFont = new Font("Segoe UI", 11f, FontStyle.Bold);

            var familyName = _currentFamily?.Name ?? "System Default";
            g.DrawString(familyName, headerFont, accentBrush, 20, y);
            y += 28;

            if (_currentFamily != null)
            {
                var infoFont = new Font("Segoe UI", 8f);
                var info = $"Bold: {_currentFamily.IsStyleAvailable(FontStyle.Bold)}  " +
                           $"Italic: {_currentFamily.IsStyleAvailable(FontStyle.Italic)}  " +
                           $"Regular: {_currentFamily.IsStyleAvailable(FontStyle.Regular)}";
                g.DrawString(info, infoFont, dimBrush, 20, y);
                y += 20;
                infoFont.Dispose();
            }

            var divPen = new Pen(Color.FromArgb(50, 50, 50));
            g.DrawLine(divPen, 20, y, _previewPanel.Width - 40, y);
            y += 12;

            g.DrawString("SINGLE SIZE PREVIEW", labelFont, accentBrush, 20, y);
            y += 22;

            using (var font = MakeFont(singleSize, style))
            {
                g.DrawString($"{singleSize}pt", labelFont, labelBrush, 20, y + 4);
                g.DrawString(sample, font, textBrush, 70, y);
                y += Math.Max(font.GetHeight(g) + 8, 24);
            }

            y += 8;
            g.DrawLine(divPen, 20, y, _previewPanel.Width - 40, y);
            y += 12;

            g.DrawString("SIZE CASCADE", labelFont, accentBrush, 20, y);
            y += 22;

            foreach (var size in PreviewSizes)
            {
                using var font = MakeFont(size, style);
                var sizeText = $"{size}pt";
                g.DrawString(sizeText, labelFont, labelBrush, 20, y + 2);

                var text = size >= 48 ? "ABCDEFGHIJKLM"
                         : size >= 36 ? "The quick brown fox"
                         : "The quick brown fox jumps over the lazy dog";
                g.DrawString(text, font, textBrush, 70, y);
                y += font.GetHeight(g) + 6;
            }

            y += 8;
            g.DrawLine(divPen, 20, y, _previewPanel.Width - 40, y);
            y += 12;

            g.DrawString("CHARACTER SET", labelFont, accentBrush, 20, y);
            y += 22;

            using (var charFont = MakeFont(20, style))
            {
                g.DrawString("ABCDEFGHIJKLMNOPQRSTUVWXYZ", charFont, textBrush, 20, y);
                y += charFont.GetHeight(g) + 4;
                g.DrawString("abcdefghijklmnopqrstuvwxyz", charFont, textBrush, 20, y);
                y += charFont.GetHeight(g) + 4;
                g.DrawString("0123456789 !@#$%^&*()-=+[]{}|;:',.<>?/", charFont, textBrush, 20, y);
                y += charFont.GetHeight(g) + 4;
            }

            _previewPanel.AutoScrollMinSize = new Size(0, (int)y + 20);

            labelBrush.Dispose(); textBrush.Dispose();
            dimBrush.Dispose(); accentBrush.Dispose();
            labelFont.Dispose(); headerFont.Dispose();
            divPen.Dispose();
        }

        private Font MakeFont(float size, FontStyle style)
        {
            if (_currentFamily != null)
            {
                if (_currentFamily.IsStyleAvailable(style))
                    return new Font(_currentFamily, size, style, GraphicsUnit.Point);
                if (_currentFamily.IsStyleAvailable(FontStyle.Regular))
                    return new Font(_currentFamily, size, FontStyle.Regular, GraphicsUnit.Point);
                foreach (FontStyle fs in Enum.GetValues(typeof(FontStyle)))
                {
                    if (_currentFamily.IsStyleAvailable(fs))
                        return new Font(_currentFamily, size, fs, GraphicsUnit.Point);
                }
            }
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _pfc?.Dispose();
            base.Dispose(disposing);
        }

        private async Task GpuTrainAsync()
        {
            _trainBtn.Text = "Stop Training";
            _trainBtn.BackColor = Color.FromArgb(200, 60, 60);
            int epochs = _epochSlider.Value;
            _trainProgress.Style = ProgressBarStyle.Continuous;
            _trainProgress.Maximum = epochs;
            _trainProgress.Value = 0;
            _trainStatus.ForeColor = Color.FromArgb(255, 200, 50);
            _trainLog.Clear();
            _gpuTrainStop = false;

            int batchSize = 64;
            _trainLog.AppendText($"GPU Training on {_cudaError}\r\n");
            _trainLog.AppendText($"MLP: {mlp_input_dim}->{mlp_hidden1}->{mlp_hidden2}->{mlp_output_dim}\r\n");
            _trainLog.AppendText($"Epochs: {epochs}, Batch: {batchSize}\r\n");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            await Task.Run(() =>
            {
                var m = (Dictionary<string,dynamic>)gpu_mlp_alloc();

                var rng = new Random(42);
                long[] initW1 = new long[(int)(long)mlp_w1_size];
                for (int i = 0; i < initW1.Length; i++) initW1[i] = (long)((rng.NextDouble() * 2 - 1) * 100);
                GpuCopyTo((long)m["w1"], initW1);
                long[] initW2 = new long[(int)(long)mlp_w2_size];
                for (int i = 0; i < initW2.Length; i++) initW2[i] = (long)((rng.NextDouble() * 2 - 1) * 50);
                GpuCopyTo((long)m["w2"], initW2);
                long[] initW3 = new long[(int)(long)mlp_w3_size];
                for (int i = 0; i < initW3.Length; i++) initW3[i] = (long)((rng.NextDouble() * 2 - 1) * 30);
                GpuCopyTo((long)m["w3"], initW3);

                int nSamples = 95;
                int batchSize = 64;
                var inputs = new long[nSamples][];
                var targets = new long[nSamples][];
                for (int cp = 32; cp < 127; cp++)
                {
                    inputs[cp - 32] = BuildGlyphInput(cp, 0.5, 0.0);
                    targets[cp - 32] = new long[119];
                    for (int j = 0; j < 119; j++) targets[cp - 32][j] = (long)(rng.NextDouble() * 500);
                }

                gpu_mlp_zero_grads(m);

                GpuCopyTo((long)m["inp"], inputs[0]);
                var fwdOk = GpuLaunch("kernel_matmul_relu", 1, 256, new long[]{(long)m["w1"],(long)m["inp"],(long)m["b1"],(long)m["h1"],256,102});
                GpuSync();
                var testH1 = GpuCopyFrom((long)m["h1"], 8);
                var testInp = GpuCopyFrom((long)m["inp"], 5);
                try { BeginInvoke((Action)(() => {
                    _trainLog.AppendText($"[diag] launch kernel_matmul_relu: {fwdOk}\r\n");
                    _trainLog.AppendText($"[diag] inp[0..4]: {string.Join(", ", testInp)}\r\n");
                    _trainLog.AppendText($"[diag] h1[0..7]: {string.Join(", ", testH1)}\r\n");
                })); } catch { }

                for (int ep = 0; ep < epochs && !_gpuTrainStop; ep++)
                {
                    for (int si = 0; si < nSamples; si++)
                    {
                        GpuCopyTo((long)m["inp"], inputs[si]);
                        GpuCopyTo((long)m["tgt"], targets[si]);
                        gpu_mlp_forward(m);
                        gpu_mlp_backward(m);

                        if ((si + 1) % batchSize == 0 || si == nSamples - 1)
                        {
                            gpu_mlp_adam(m, batchSize);
                            gpu_mlp_zero_grads(m);
                        }
                    }

                    if (ep % 100 == 0 || ep == epochs - 1)
                    {
                        long lossBuf = gpu_alloc(mlp_output_dim);
                        GpuCopyTo((long)m["inp"], inputs[0]);
                        GpuCopyTo((long)m["tgt"], targets[0]);
                        gpu_mlp_forward(m);
                        gpu_launch("kernel_mse_loss", 1L, mlp_output_dim, new List<dynamic>{m["out"], m["tgt"], lossBuf, mlp_output_dim});
                        GpuSync();
                        var lossArr = GpuCopyFrom(lossBuf, (int)(long)mlp_output_dim);
                        gpu_free(lossBuf);
                        double totalLoss = 0;
                        for (int li = 0; li < lossArr.Length; li++) totalLoss += lossArr[li];
                        double avgLoss = totalLoss / lossArr.Length;
                        var elapsed = sw.ElapsedMilliseconds;
                        var msg = $"epoch {ep,5}/{epochs}  loss={avgLoss:F2}  ({elapsed}ms)\r\n";
                        var epCopy = ep;
                        try { BeginInvoke((Action)(() => {
                            _trainLog.AppendText(msg);
                            _trainStatus.Text = $"Epoch {epCopy}/{epochs} loss={avgLoss:F2}";
                            _trainProgress.Value = Math.Min(epCopy, _trainProgress.Maximum);
                        })); } catch { }
                    }
                }

                var totalMs = sw.ElapsedMilliseconds;

                var w1out = GpuCopyFrom((long)m["w1"], (int)(long)mlp_w1_size);
                var b1out = GpuCopyFrom((long)m["b1"], (int)(long)mlp_hidden1);
                var w2out = GpuCopyFrom((long)m["w2"], (int)(long)mlp_w2_size);
                var b2out = GpuCopyFrom((long)m["b2"], (int)(long)mlp_hidden2);
                var w3out = GpuCopyFrom((long)m["w3"], (int)(long)mlp_w3_size);
                var b3out = GpuCopyFrom((long)m["b3"], (int)(long)mlp_output_dim);

                var outDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? Directory.GetCurrentDirectory(), "trained"));
                Directory.CreateDirectory(outDir);
                var wPath = Path.GetFullPath(Path.Combine(outDir, "FontAiWeights.codex"));
                using (var sw2 = new StreamWriter(wPath))
                {
                    sw2.WriteLine("Chapter: FontAiWeights\n\nSection: Weights\n");
                    WriteWeightArray(sw2, "fai-w1", w1out);
                    WriteWeightArray(sw2, "fai-b1", b1out);
                    WriteWeightArray(sw2, "fai-w2", w2out);
                    WriteWeightArray(sw2, "fai-b2", b2out);
                    WriteWeightArray(sw2, "fai-w3", w3out);
                    WriteWeightArray(sw2, "fai-b3", b3out);
                    sw2.WriteLine($"\n  fai-input-dim : Integer = {mlp_input_dim}");
                    sw2.WriteLine($"  fai-hidden1-dim : Integer = {mlp_hidden1}");
                    sw2.WriteLine($"  fai-hidden2-dim : Integer = {mlp_hidden2}");
                    sw2.WriteLine($"  fai-output-dim : Integer = {mlp_output_dim}");
                }

                try { BeginInvoke((Action)(() => {
                    _trainLog.AppendText($"\r\nDone! {epochs} epochs in {totalMs}ms ({(double)totalMs/epochs:F1}ms/epoch)\r\n");
                    _trainLog.AppendText($"Weights saved: {wPath}\r\n");
                    _trainStatus.Text = $"Done: {totalMs}ms total";
                    _trainStatus.ForeColor = Color.FromArgb(100, 200, 100);
                    _trainBtn.Text = "Start Training";
                    _trainBtn.BackColor = Color.FromArgb(40, 160, 40);
                    _trainProgress.MarqueeAnimationSpeed = 0;
                    _weightsFile = wPath;
                    _weightsLabel.Text = Path.GetFileName(wPath);
                    _weightsLabel.ForeColor = Color.FromArgb(100, 200, 100);
                })); } catch { }
            });
        }

        static void WriteWeightArray(StreamWriter w, string name, long[] arr)
        {
            w.Write($"  {name} : List Integer\n  {name} = [");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) w.Write(", ");
                if (i % 20 == 0 && i > 0) w.Write("\n    ");
                w.Write(arr[i]);
            }
            w.WriteLine("]\n");
        }

        #region GPU Training (transpiled from FontAiTrainer.codex)

        static long gpu_alloc(dynamic count) { return GpuAlloc((int)(long)count); }
        static void gpu_free(dynamic ptr) { GpuFree((long)ptr); }
        static void gpu_copy_to(dynamic dst, dynamic src) { GpuCopyTo((long)dst, (long[])((List<dynamic>)src).Select(x => (long)x).ToArray()); }
        static long[] gpu_copy_from(dynamic src, dynamic count) { return GpuCopyFrom((long)src, (int)(long)count); }
        static dynamic gpu_launch(dynamic name, dynamic grid, dynamic block, dynamic args) { return GpuLaunch((string)name, (int)(long)grid, (int)(long)block, ((List<dynamic>)args).Select(x => (long)x).ToArray()) ? 1L : 0L; }
        static void gpu_sync() { GpuSync(); }
        static long list_at(dynamic xs, dynamic i) { return (long)((List<dynamic>)xs)[(int)(long)i]; }
        static long list_length(dynamic xs) { return ((List<dynamic>)xs).Count; }

        static dynamic mlp_input_dim = 102L;
        static dynamic mlp_hidden1 = 256L;
        static dynamic mlp_hidden2 = 128L;
        static dynamic mlp_output_dim = 119L;
        static dynamic mlp_w1_size = 256L * 102L;
        static dynamic mlp_w2_size = 128L * 256L;
        static dynamic mlp_w3_size = 119L * 128L;

        static dynamic gpu_mlp_alloc() {
            long w1=gpu_alloc(mlp_w1_size), b1=gpu_alloc(mlp_hidden1), w2=gpu_alloc(mlp_w2_size), b2=gpu_alloc(mlp_hidden2);
            long w3=gpu_alloc(mlp_w3_size), b3=gpu_alloc(mlp_output_dim);
            long h1=gpu_alloc(mlp_hidden1), h2=gpu_alloc(mlp_hidden2), ou=gpu_alloc(mlp_output_dim);
            long inp=gpu_alloc(mlp_input_dim), tgt=gpu_alloc(mlp_output_dim);
            long go=gpu_alloc(mlp_output_dim), gh2=gpu_alloc(mlp_hidden2), gh1=gpu_alloc(mlp_hidden1);
            long gw1=gpu_alloc(mlp_w1_size), gb1=gpu_alloc(mlp_hidden1), gw2=gpu_alloc(mlp_w2_size), gb2=gpu_alloc(mlp_hidden2), gw3=gpu_alloc(mlp_w3_size), gb3=gpu_alloc(mlp_output_dim);
            long mw1=gpu_alloc(mlp_w1_size), vw1=gpu_alloc(mlp_w1_size), mb1=gpu_alloc(mlp_hidden1), vb1=gpu_alloc(mlp_hidden1);
            long mw2=gpu_alloc(mlp_w2_size), vw2=gpu_alloc(mlp_w2_size), mb2=gpu_alloc(mlp_hidden2), vb2=gpu_alloc(mlp_hidden2);
            long mw3=gpu_alloc(mlp_w3_size), vw3=gpu_alloc(mlp_w3_size), mb3=gpu_alloc(mlp_output_dim), vb3=gpu_alloc(mlp_output_dim);
            return new Dictionary<string,dynamic> {
                ["w1"]=w1,["b1"]=b1,["w2"]=w2,["b2"]=b2,["w3"]=w3,["b3"]=b3,
                ["h1"]=h1,["h2"]=h2,["out"]=ou,["inp"]=inp,["tgt"]=tgt,
                ["go"]=go,["gh2"]=gh2,["gh1"]=gh1,
                ["gw1"]=gw1,["gb1"]=gb1,["gw2"]=gw2,["gb2"]=gb2,["gw3"]=gw3,["gb3"]=gb3,
                ["mw1"]=mw1,["vw1"]=vw1,["mb1"]=mb1,["vb1"]=vb1,
                ["mw2"]=mw2,["vw2"]=vw2,["mb2"]=mb2,["vb2"]=vb2,
                ["mw3"]=mw3,["vw3"]=vw3,["mb3"]=mb3,["vb3"]=vb3
            };
        }

        static void gpu_mlp_forward(Dictionary<string,dynamic> m) {
            gpu_launch("kernel_matmul_relu", 1L, mlp_hidden1, new List<dynamic>{m["w1"],m["inp"],m["b1"],m["h1"],mlp_hidden1,mlp_input_dim});
            gpu_launch("kernel_matmul_relu", 1L, mlp_hidden2, new List<dynamic>{m["w2"],m["h1"],m["b2"],m["h2"],mlp_hidden2,mlp_hidden1});
            gpu_launch("kernel_matmul", 1L, mlp_output_dim, new List<dynamic>{m["w3"],m["h2"],m["b3"],m["out"],mlp_output_dim,mlp_hidden2});
            GpuSync();
        }

        static void gpu_mlp_backward(Dictionary<string,dynamic> m) {
            gpu_launch("kernel_mse_grad", 1L, mlp_output_dim, new List<dynamic>{m["out"],m["tgt"],m["go"],mlp_output_dim});
            gpu_launch("kernel_matmul_transpose", 1L, mlp_hidden2, new List<dynamic>{m["w3"],m["go"],m["gh2"],mlp_output_dim,mlp_hidden2});
            gpu_launch("kernel_relu_grad", 1L, mlp_hidden2, new List<dynamic>{m["gh2"],m["h2"],m["gh2"],mlp_hidden2});
            gpu_launch("kernel_matmul_transpose", 1L, mlp_hidden1, new List<dynamic>{m["w2"],m["gh2"],m["gh1"],mlp_hidden2,mlp_hidden1});
            gpu_launch("kernel_relu_grad", 1L, mlp_hidden1, new List<dynamic>{m["gh1"],m["h1"],m["gh1"],mlp_hidden1});
            gpu_launch("kernel_outer_product_add", 1L, mlp_w3_size, new List<dynamic>{m["gw3"],m["go"],m["h2"],mlp_output_dim,mlp_hidden2});
            gpu_launch("kernel_bias_grad_add", 1L, mlp_output_dim, new List<dynamic>{m["gb3"],m["go"],mlp_output_dim});
            gpu_launch("kernel_outer_product_add", 1L, mlp_w2_size, new List<dynamic>{m["gw2"],m["gh2"],m["h1"],mlp_hidden2,mlp_hidden1});
            gpu_launch("kernel_bias_grad_add", 1L, mlp_hidden2, new List<dynamic>{m["gb2"],m["gh2"],mlp_hidden2});
            gpu_launch("kernel_outer_product_add", 1L, mlp_w1_size, new List<dynamic>{m["gw1"],m["gh1"],m["inp"],mlp_hidden1,mlp_input_dim});
            gpu_launch("kernel_bias_grad_add", 1L, mlp_hidden1, new List<dynamic>{m["gb1"],m["gh1"],mlp_hidden1});
            GpuSync();
        }

        static void gpu_mlp_adam(Dictionary<string,dynamic> m, long batchSize) {
            var bs = new List<dynamic>{batchSize};
            gpu_launch("kernel_adam_update", 1L, mlp_w1_size, new List<dynamic>{m["w1"],m["gw1"],m["mw1"],m["vw1"],mlp_w1_size,batchSize});
            gpu_launch("kernel_adam_update", 1L, mlp_hidden1, new List<dynamic>{m["b1"],m["gb1"],m["mb1"],m["vb1"],mlp_hidden1,batchSize});
            gpu_launch("kernel_adam_update", 1L, mlp_w2_size, new List<dynamic>{m["w2"],m["gw2"],m["mw2"],m["vw2"],mlp_w2_size,batchSize});
            gpu_launch("kernel_adam_update", 1L, mlp_hidden2, new List<dynamic>{m["b2"],m["gb2"],m["mb2"],m["vb2"],mlp_hidden2,batchSize});
            gpu_launch("kernel_adam_update", 1L, mlp_w3_size, new List<dynamic>{m["w3"],m["gw3"],m["mw3"],m["vw3"],mlp_w3_size,batchSize});
            gpu_launch("kernel_adam_update", 1L, mlp_output_dim, new List<dynamic>{m["b3"],m["gb3"],m["mb3"],m["vb3"],mlp_output_dim,batchSize});
            GpuSync();
        }

        static void gpu_mlp_zero_grads(Dictionary<string,dynamic> m) {
            gpu_launch("kernel_zero_fill", 1L, mlp_w1_size, new List<dynamic>{m["gw1"],mlp_w1_size});
            gpu_launch("kernel_zero_fill", 1L, mlp_hidden1, new List<dynamic>{m["gb1"],mlp_hidden1});
            gpu_launch("kernel_zero_fill", 1L, mlp_w2_size, new List<dynamic>{m["gw2"],mlp_w2_size});
            gpu_launch("kernel_zero_fill", 1L, mlp_hidden2, new List<dynamic>{m["gb2"],mlp_hidden2});
            gpu_launch("kernel_zero_fill", 1L, mlp_w3_size, new List<dynamic>{m["gw3"],mlp_w3_size});
            gpu_launch("kernel_zero_fill", 1L, mlp_output_dim, new List<dynamic>{m["gb3"],mlp_output_dim});
            GpuSync();
        }

        static long[] BuildGlyphInput(int codepoint, double weight, double italic) {
            var input = new long[102];
            int gi = codepoint - 32;
            if (gi >= 0 && gi < 95) input[gi] = 1000;
            input[95] = (long)(weight * 1000);
            input[96] = (long)(italic * 1000);
            input[97] = (codepoint >= 65 && codepoint <= 90) ? 1000L : 0L;
            input[98] = (codepoint >= 97 && codepoint <= 122) ? 1000L : 0L;
            input[99] = (codepoint >= 48 && codepoint <= 57) ? 1000L : 0L;
            input[100] = (input[97]==0 && input[98]==0 && input[99]==0 && codepoint!=32) ? 1000L : 0L;
            input[101] = (codepoint==103||codepoint==106||codepoint==112||codepoint==113||codepoint==121) ? 1000L : 0L;
            return input;
        }

        #endregion

        #region CUDA Driver API

        static IntPtr _cudaCtx, _cudaModule;
        static bool _cudaReady;
        static string _cudaError = "";

        [DllImport("nvcuda")] static extern int cuInit(int f);
        [DllImport("nvcuda")] static extern int cuDeviceGet(out int d, int o);
        [DllImport("nvcuda")] static extern int cuCtxCreate_v2(out IntPtr c, uint f, int d);
        [DllImport("nvcuda")] static extern int cuModuleLoadData(out IntPtr m, byte[] p);
        [DllImport("nvcuda")] static extern int cuModuleGetFunction(out IntPtr f, IntPtr m, string n);
        [DllImport("nvcuda")] static extern int cuMemAlloc_v2(out IntPtr p, long s);
        [DllImport("nvcuda")] static extern int cuMemFree_v2(IntPtr p);
        [DllImport("nvcuda")] static extern int cuMemcpyHtoD_v2(IntPtr d, long[] s, long n);
        [DllImport("nvcuda")] static extern int cuMemcpyDtoH_v2(long[] d, IntPtr s, long n);
        [DllImport("nvcuda")] static extern int cuLaunchKernel(IntPtr f, int gx, int gy, int gz, int bx, int by, int bz, int sh, IntPtr st, IntPtr[] a, IntPtr e);
        [DllImport("nvcuda")] static extern int cuCtxSynchronize();
        [DllImport("nvcuda")] static extern int cuDeviceGetName(byte[] n, int len, int d);

        static bool GpuInit()
        {
            try
            {
                if (cuInit(0) != 0) { _cudaError = "cuInit failed"; return false; }
                if (cuDeviceGet(out int dev, 0) != 0) { _cudaError = "no device"; return false; }
                if (cuCtxCreate_v2(out _cudaCtx, 0, dev) != 0) { _cudaError = "ctx failed"; return false; }
                var nameBuf = new byte[256]; cuDeviceGetName(nameBuf, 256, dev);
                var devName = System.Text.Encoding.ASCII.GetString(nameBuf).TrimEnd('\0');
                _cudaError = "GPU: " + devName; _cudaReady = true; return true;
            }
            catch (Exception ex) { _cudaError = "no CUDA: " + ex.Message; return false; }
        }

        static bool GpuLoadPtx(string path)
        {
            if (!_cudaReady) return false;
            try
            {
                var ptx = File.ReadAllText(path);
                var bytes = System.Text.Encoding.ASCII.GetBytes(ptx + "\0");
                var rc = cuModuleLoadData(out _cudaModule, bytes);
                if (rc != 0) { _cudaError = "PTX load error " + rc + " (" + path + ")"; return false; }
                _cudaError = "PTX loaded"; return true;
            }
            catch (Exception ex) { _cudaError = "PTX exception: " + ex.Message; return false; }
        }

        static long GpuAlloc(int count) { cuMemAlloc_v2(out IntPtr ptr, count * 8L); return (long)ptr; }
        static void GpuFree(long ptr) { cuMemFree_v2((IntPtr)ptr); }
        static void GpuCopyTo(long dst, long[] src) { cuMemcpyHtoD_v2((IntPtr)dst, src, src.Length * 8L); }
        static long[] GpuCopyFrom(long src, int count) { var a = new long[count]; cuMemcpyDtoH_v2(a, (IntPtr)src, count * 8L); return a; }

        static bool GpuLaunch(string kernel, int grid, int block, long[] args)
        {
            if (!_cudaReady) return false;
            if (cuModuleGetFunction(out IntPtr func, _cudaModule, kernel) != 0) return false;
            var handle = GCHandle.Alloc(args, GCHandleType.Pinned);
            var basePtr = handle.AddrOfPinnedObject();
            var ptrs = new IntPtr[args.Length];
            for (int i = 0; i < args.Length; i++) ptrs[i] = basePtr + i * 8;
            var rc = cuLaunchKernel(func, grid, 1, 1, block, 1, 1, 0, IntPtr.Zero, ptrs, IntPtr.Zero);
            handle.Free();
            return rc == 0;
        }

        static void GpuSync() { if (_cudaReady) cuCtxSynchronize(); }

        #endregion

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FontExplorerForm());
        }
    }

    internal class FontFileItem
    {
        public string Path { get; }
        public FontFileItem(string path) { Path = path; }
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
}
