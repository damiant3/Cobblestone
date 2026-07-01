using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexGlobe
{
    public enum Scene { Earth, BlackHole, Sosaria }

    public class GlobeForm : Form
    {
        private DoubleBufferedPanel _viewport;
        private Bitmap _backbuffer;
        private System.Windows.Forms.Timer _timer;
        private Scene _scene = Scene.Earth;
        private ComboBox _sceneCombo;

        private double _yaw, _pitch, _lookYaw, _lookPitch;
        private double _camPitch = 0.3, _zoom = 2.8;
        private bool _autoRotate = true;
        private bool _dragging;
        private MouseButtons _dragButton;
        private Point _dragStart;
        private double _dragYaw, _dragPitch, _dragLookYaw, _dragLookPitch;
        private double _moonOrbitAngle = 0.8, _bhTime;

        private byte[] _earthTex;
        private int _texW = 2048, _texH = 1024;

        private Panel _sidebar;
        private CheckBox _autoRotateCheck, _atmosphereCheck;
        private TrackBar _zoomSlider;
        private Label _zoomVal, _fpsLabel, _statusLabel;
        private int _frameCount;
        private DateTime _lastFpsTime = DateTime.Now;

        // CUDA state
        private static IntPtr _cudaCtx, _cudaModule;
        private static bool _cudaReady;
        private static string _cudaStatus = "";
        private long _gpuFramebuf, _gpuTex, _gpuParams;
        private int _gpuW, _gpuH;
        private bool _gpuTexUploaded;
        private long[] _fbHostBuf;

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
        [DllImport("nvcuda")] static extern int cuCtxSetLimit(int limit, long value);
        [DllImport("nvcuda")] static extern int cuDeviceGetName(byte[] n, int len, int d);

        public GlobeForm()
        {
            Text = "Codex Globe";
            Size = new Size(1280, 860);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(20, 20, 25);
            ForeColor = Color.FromArgb(212, 212, 212);
            DoubleBuffered = true;

            LoadTexture();
            BuildUI();

            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        private void LoadTexture()
        {
            foreach (var name in new[] { "earth-texture-ice.raw", "earth-texture.raw" })
            {
                foreach (var dir in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
                {
                    var p = Path.Combine(dir, name);
                    if (!File.Exists(p)) continue;
                    var raw = File.ReadAllBytes(p);
                    if (raw.Length == _texW * _texH * 3) { _earthTex = raw; return; }
                }
                var repo = FindRepoFile("apps/globe/" + name);
                if (repo != null && File.Exists(repo))
                {
                    var raw = File.ReadAllBytes(repo);
                    if (raw.Length == _texW * _texH * 3) { _earthTex = raw; return; }
                }
            }
            _earthTex = new byte[_texW * _texH * 3];
        }

        public void InitGpu()
        {
            try
            {
                if (cuInit(0) != 0) { _cudaStatus = "cuInit failed"; return; }
                if (cuDeviceGet(out int dev, 0) != 0) { _cudaStatus = "no device"; return; }
                if (cuCtxCreate_v2(out _cudaCtx, 0, dev) != 0) { _cudaStatus = "ctx failed"; return; }
                cuCtxSetLimit(0x00, 65536);
                var nb = new byte[256]; cuDeviceGetName(nb, 256, dev);
                var devName = System.Text.Encoding.ASCII.GetString(nb).TrimEnd('\0');

                string ptxPath = null;
                foreach (var dir in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
                {
                    var p = Path.Combine(dir, "globe.ptx");
                    if (File.Exists(p)) { ptxPath = p; break; }
                }
                if (ptxPath == null)
                {
                    var repo = FindRepoFile("apps/globe/kernels/globe.ptx");
                    if (repo != null && File.Exists(repo)) ptxPath = repo;
                }
                if (ptxPath == null) { _cudaStatus = devName + " (no PTX)"; return; }

                var ptx = File.ReadAllText(ptxPath);
                var bytes = System.Text.Encoding.ASCII.GetBytes(ptx + "\0");
                var rc = cuModuleLoadData(out _cudaModule, bytes);
                if (rc != 0) { _cudaStatus = devName + " (PTX load rc=" + rc + " from " + ptxPath + ")"; return; }

                cuMemAlloc_v2(out IntPtr paramPtr, 16 * 8);
                _gpuParams = (long)paramPtr;

                _cudaReady = true;
                _cudaStatus = devName + " + PTX";
            }
            catch (Exception ex) { _cudaStatus = "no CUDA: " + ex.Message; }
            File.WriteAllText(@"D:\Projects\NewRepository-blu\apps\globe\out\gpu-status.txt", _cudaStatus + "\ncudaReady=" + _cudaReady);

            if (_statusLabel != null)
            {
                _statusLabel.Text = _cudaStatus;
                _statusLabel.ForeColor = _cudaReady ? Color.FromArgb(100, 200, 100) : Color.FromArgb(200, 200, 100);
            }
        }

        private void EnsureGpuBuffers(int w, int h)
        {
            if (_gpuW == w && _gpuH == h) return;
            if (_gpuFramebuf != 0) cuMemFree_v2((IntPtr)_gpuFramebuf);
            cuMemAlloc_v2(out IntPtr fb, (long)w * h * 8);
            _gpuFramebuf = (long)fb;
            _gpuW = w; _gpuH = h;
            _fbHostBuf = new long[w * h];
        }

        private void UploadTexture()
        {
            if (_gpuTexUploaded) return;
            var texLongs = new long[_earthTex.Length];
            for (int i = 0; i < _earthTex.Length; i++) texLongs[i] = _earthTex[i];
            cuMemAlloc_v2(out IntPtr tp, (long)texLongs.Length * 8);
            _gpuTex = (long)tp;
            cuMemcpyHtoD_v2((IntPtr)_gpuTex, texLongs, (long)texLongs.Length * 8);
            _gpuTexUploaded = true;
        }

        private bool GpuLaunch(string kernel, int gridX, int blockX, long[] args)
        {
            if (cuModuleGetFunction(out IntPtr func, _cudaModule, kernel) != 0) return false;
            var handle = GCHandle.Alloc(args, GCHandleType.Pinned);
            var basePtr = handle.AddrOfPinnedObject();
            var ptrs = new IntPtr[args.Length];
            for (int i = 0; i < args.Length; i++) ptrs[i] = basePtr + i * 8;
            var rc = cuLaunchKernel(func, gridX, 1, 1, blockX, 1, 1, 0, IntPtr.Zero, ptrs, IntPtr.Zero);
            handle.Free();
            return rc == 0;
        }

        private static long F(double v) => (long)(v * 1000.0);

        private static string StripDeadStubs(string ptx)
        {
            var lines = ptx.Split('\n');
            var keep = new System.Collections.Generic.List<string>();
            bool inStub = false;
            int braceDepth = 0;
            var stubNames = new System.Collections.Generic.HashSet<string> {
                "cordic_sin","cordic_cos","real_sqrt","real_abs","real_min","real_max",
                "real_floor","__int_to_real","__real_to_int","cordic_atan2",
                "asin_approx","atan2_approx","gamma_byte","clamp_int","sphere_hit"
            };

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!inStub && line.StartsWith(".visible .func"))
                {
                    bool isStub = false;
                    foreach (var s in stubNames) { if (line.Contains(") " + s + " (")) { isStub = true; break; } }
                    if (isStub)
                    {
                        if (line.TrimEnd().EndsWith(";")) continue;
                        inStub = true; braceDepth = 0; continue;
                    }
                }
                if (inStub) { if (line.Contains("{")) braceDepth++; if (line.Contains("}")) { braceDepth--; if (braceDepth <= 0) { inStub = false; } } continue; }

                if (line.Contains("mov.u32 %rd"))
                {
                    var fixLine = line.Replace("mov.u32 %rd", "mov.u32 %ru");
                    int commaIdx = fixLine.IndexOf(", %");
                    keep.Add(fixLine);
                    string ru = fixLine.Substring(fixLine.IndexOf("%ru"), fixLine.IndexOf(",") - fixLine.IndexOf("%ru"));
                    string rd = ru.Replace("%ru", "%rd");
                    keep.Add("    cvt.s64.u32 " + rd + ", " + ru + ";");
                    if (i + 1 < lines.Length && lines[i + 1].Contains("cvt.s64.u32")) i++;
                }
                else
                {
                    keep.Add(line);
                }
            }
            return string.Join("\n", keep);
        }

        private string FindRepoFile(string rel)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var c = Path.Combine(dir, rel);
                if (File.Exists(c)) return c;
                if (File.Exists(Path.Combine(dir, "CLAUDE.md"))) return c;
                var p = Directory.GetParent(dir);
                if (p == null) break;
                dir = p.FullName;
            }
            return null;
        }

        private void BuildUI()
        {
            _sidebar = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = Color.FromArgb(30, 30, 35), Padding = new Padding(12), AutoScroll = true };
            int y = 8;
            _sidebar.Controls.Add(new Label { Text = "Codex Globe", Location = new Point(12, y), AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Color.FromArgb(80, 180, 255) }); y += 38;

            _sidebar.Controls.Add(Sec("SCENE", y)); y += 22;
            _sceneCombo = new ComboBox { Location = new Point(12, y), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f) };
            _sceneCombo.Items.AddRange(new object[] { "Earth", "Black Hole", "Sosaria" });
            _sceneCombo.SelectedIndex = 0;
            _sceneCombo.SelectedIndexChanged += (s, e) => SwitchScene((Scene)_sceneCombo.SelectedIndex);
            _sidebar.Controls.Add(_sceneCombo); y += 34;

            _sidebar.Controls.Add(Sec("VIEW", y)); y += 22;
            _autoRotateCheck = Chk("Auto-rotate", 12, y, true); _autoRotateCheck.CheckedChanged += (s, e) => _autoRotate = _autoRotateCheck.Checked;
            _sidebar.Controls.Add(_autoRotateCheck); y += 24;
            _atmosphereCheck = Chk("Atmosphere", 12, y, true); _sidebar.Controls.Add(_atmosphereCheck); y += 28;

            _sidebar.Controls.Add(Sec("ZOOM", y)); y += 22;
            _zoomSlider = new TrackBar { Location = new Point(12, y), Width = 220, Minimum = 15, Maximum = 100, Value = 28, TickFrequency = 10, BackColor = Color.FromArgb(30, 30, 35) };
            _zoomVal = new Label { Text = "2.8x", Location = new Point(200, y - 18), AutoSize = true, ForeColor = Color.FromArgb(0, 180, 255), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            _zoomSlider.ValueChanged += (s, e) => { _zoom = _zoomSlider.Value / 10.0; _zoomVal.Text = $"{_zoom:F1}x"; };
            _sidebar.Controls.Add(_zoomSlider); _sidebar.Controls.Add(_zoomVal); y += 48;

            _sidebar.Controls.Add(Sec("INFO", y)); y += 22;
            _fpsLabel = new Label { Text = "FPS: --", Location = new Point(12, y), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Segoe UI", 9f) }; _sidebar.Controls.Add(_fpsLabel); y += 20;
            _statusLabel = new Label { Text = "GPU: ...", Location = new Point(12, y), Width = 220, Height = 32, ForeColor = Color.FromArgb(150, 150, 150), Font = new Font("Segoe UI", 8f) }; _sidebar.Controls.Add(_statusLabel); y += 36;

            _sidebar.Controls.Add(Sec("CONTROLS", y)); y += 22;
            _sidebar.Controls.Add(new Label { Text = "L drag: spin\nR drag: pan\nScroll: zoom", Location = new Point(12, y), Width = 220, Height = 50, ForeColor = Color.FromArgb(100, 100, 100), Font = new Font("Segoe UI", 8.5f) }); y += 52;
            var rb = new Button { Text = "Reorient", Location = new Point(12, y), Width = 220, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White, Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand };
            rb.Click += (s, e) => Reorient();
            _sidebar.Controls.Add(rb);

            _viewport = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Black };
            _viewport.Paint += (s, e) => { if (_backbuffer != null) e.Graphics.DrawImageUnscaled(_backbuffer, 0, 0); };
            _viewport.Resize += (s, e) => ResizeBackbuffer();
            _viewport.MouseDown += MouseDn;
            _viewport.MouseMove += MouseMv;
            _viewport.MouseUp += (s, e) => _dragging = false;
            _viewport.MouseWheel += (s, e) => { _zoom = Math.Clamp(_zoom - e.Delta * 0.002, 1.5, 10.0); _zoomSlider.Value = Math.Clamp((int)(_zoom * 10), 15, 100); };
            _viewport.MouseClick += (s, e) => { if (e.Button == MouseButtons.Middle) Reorient(); };

            Controls.Add(_viewport); Controls.Add(_sidebar);
        }

        private Label Sec(string t, int y) => new Label { Text = t, Location = new Point(12, y), AutoSize = true, ForeColor = Color.FromArgb(0, 122, 204), Font = new Font("Segoe UI", 8f, FontStyle.Bold) };
        private CheckBox Chk(string t, int x, int y, bool c) => new CheckBox { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 9f), Checked = c };

        private void SwitchScene(Scene s)
        {
            _scene = s; _yaw = 0; _pitch = 0; _lookYaw = 0; _lookPitch = 0; _autoRotate = true;
            if (_autoRotateCheck != null) _autoRotateCheck.Checked = true;
            switch (s) { case Scene.Earth: _zoom = 2.8; _camPitch = 0.2; _yaw = 3.5; break; case Scene.BlackHole: _zoom = 4.4; _camPitch = 0.35; break; case Scene.Sosaria: _zoom = 2.8; _camPitch = 0.3; break; }
            _zoomSlider.Value = Math.Clamp((int)(_zoom * 10), 15, 100);
            Text = "Codex Globe — " + s;
        }

        private void Reorient() => SwitchScene(_scene);

        public void ResizeBackbuffer() { int w = Math.Max(_viewport.Width, 1), h = Math.Max(_viewport.Height, 1); _backbuffer?.Dispose(); _backbuffer = new Bitmap(w, h, PixelFormat.Format32bppArgb); }

        public void Tick()
        {
            if (_autoRotate) _yaw += 0.004;
            _bhTime += 0.05;
            _moonOrbitAngle += 0.001;
            RenderFrame();
            _viewport.Invalidate();
            _frameCount++;
            var now = DateTime.Now;
            if ((now - _lastFpsTime).TotalSeconds >= 1.0) { _fpsLabel.Text = $"FPS: {_frameCount}"; _frameCount = 0; _lastFpsTime = now; }
        }

        private unsafe void RenderFrame()
        {
            if (_backbuffer == null) ResizeBackbuffer();
            int w = _backbuffer.Width, h = _backbuffer.Height;

            if (_cudaReady)
            {
                EnsureGpuBuffers(w, h);
                int pixels = w * h;
                int blockSize = 256;
                int gridSize = (pixels + blockSize - 1) / blockSize;

                switch (_scene)
                {
                    case Scene.Earth:
                    case Scene.Sosaria:
                        UploadTexture();
                        var ep = new long[] { F((double)w / h), F(_zoom), F(_camPitch), F(_yaw), F(_pitch), F(0.3), F(0.5), F(-0.8), _gpuTex };
                        cuMemcpyHtoD_v2((IntPtr)_gpuParams, ep, ep.Length * 8);
                        var ok = GpuLaunch("earth_pixel_kernel", gridSize, blockSize,
                            new long[] { _gpuFramebuf, _gpuTex, _gpuParams, (long)_texW, (long)_texH, (long)w, (long)h, (long)pixels });
                        cuCtxSynchronize();
                        break;
                    case Scene.BlackHole:
                        var bp = new long[] { F((double)w / h), F(_zoom), F(_camPitch + _pitch), F(_yaw), F(_bhTime) };
                        cuMemcpyHtoD_v2((IntPtr)_gpuParams, bp, bp.Length * 8);
                        GpuLaunch("bh_pixel_kernel", gridSize, blockSize,
                            new long[] { _gpuFramebuf, _gpuParams, (long)w, (long)h, (long)pixels });
                        break;
                }

                cuCtxSynchronize();
                cuMemcpyDtoH_v2(_fbHostBuf, (IntPtr)_gpuFramebuf, (long)pixels * 8);

                var bdata = _backbuffer.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int* dst = (int*)bdata.Scan0;
                for (int i = 0; i < pixels; i++) dst[i] = (int)_fbHostBuf[i];
                _backbuffer.UnlockBits(bdata);
            }
            else
            {
                var bdata = _backbuffer.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int* dst = (int*)bdata.Scan0;
                for (int i = 0; i < w * h; i++) dst[i] = unchecked((int)0xFFFF0000);
                _backbuffer.UnlockBits(bdata);
            }
        }

        private void MouseDn(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _dragButton = MouseButtons.Left; _dragStart = e.Location; _dragYaw = _yaw; _dragPitch = _pitch; _autoRotate = false; if (_autoRotateCheck != null) _autoRotateCheck.Checked = false; }
            else if (e.Button == MouseButtons.Right) { _dragging = true; _dragButton = MouseButtons.Right; _dragStart = e.Location; _dragLookYaw = _lookYaw; _dragLookPitch = _lookPitch; }
        }

        private void MouseMv(object s, MouseEventArgs e)
        {
            if (!_dragging) return;
            double dx = (e.X - _dragStart.X) * 0.005, dy = (e.Y - _dragStart.Y) * 0.005;
            if (_dragButton == MouseButtons.Left) { _yaw = _dragYaw + dx; _pitch = Math.Clamp(_dragPitch - dy, -1.56, 1.56); }
            else { _lookYaw = _dragLookYaw + dx; _lookPitch = Math.Clamp(_dragLookPitch - dy, -1.0, 1.0); }
        }

        protected override void Dispose(bool d) { if (d) { _timer?.Dispose(); _backbuffer?.Dispose(); } base.Dispose(d); }
        public void SaveScreenshot(string p) => _backbuffer?.Save(p, ImageFormat.Png);

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new GlobeForm();
            try { form.InitGpu(); } catch (Exception ex) { File.WriteAllText(@"D:\Projects\NewRepository-blu\apps\globe\out\gpu-status.txt", "GPU CRASH: " + ex.GetType().Name + ": " + ex.Message); }
            File.WriteAllText(@"D:\Projects\NewRepository-blu\apps\globe\out\gpu-status.txt", _cudaStatus + " ready=" + _cudaReady);
            if (args.Length > 0 && args[0] == "--screenshot")
            {
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--pole") form._pitch = -1.4;
                    if (args[i] == "--scene" && i + 1 < args.Length)
                    {
                        if (args[i + 1] == "blackhole") { form._scene = Scene.BlackHole; form._zoom = 4.4; form._camPitch = 0.14; }
                        else if (args[i + 1] == "sosaria") form._scene = Scene.Sosaria;
                    }
                }
                form.Show(); Application.DoEvents(); form.ResizeBackbuffer();
                for (int i = 0; i < 60; i++) { form.Tick(); Application.DoEvents(); }
                form.SaveScreenshot(args.Length > 1 ? args[1] : "globe-screenshot.png");
                form.Close(); return;
            }
            Application.Run(form);
        }
    }

    internal class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); UpdateStyles(); }
    }
}
