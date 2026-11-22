using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace WallStretch
{
    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }

    public class MainForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(15, 15, 19);
        private static readonly Color PanelBg = Color.FromArgb(24, 24, 30);
        private static readonly Color TextMain = Color.FromArgb(237, 237, 242);
        private static readonly Color TextMuted = Color.FromArgb(140, 140, 154);
        private static readonly Color Accent = Color.FromArgb(255, 176, 84);
        private static readonly Color BtnBg = Color.FromArgb(38, 38, 47);
        private static readonly Color BtnHover = Color.FromArgb(54, 54, 66);
        private static readonly Color Line = Color.FromArgb(44, 44, 54);

        private Rectangle _vb;
        private Rectangle _area;
        private List<MonitorInfo> _monitors;
        private Bitmap _image;
        private Bitmap _preview;
        private string _imageName;

        /// <summary>Image top-left corner in virtual-desktop coordinates.</summary>
        private float _ox;
        private float _oy;
        /// <summary>Virtual desktop pixels per image pixel.</summary>
        private float _scale = 1f;

        private Panel _top;
        private BufferedPanel _stage;
        private Panel _chipPanel;
        private Label _status;
        private Label _zoomLabel;
        private readonly List<Button> _chipButtons = new List<Button>();
        private ToolTip _tip;

        private Button _btnPaste;
        private Button _btnOpen;
        private Button _btnCover;
        private Button _btnWidth;
        private Button _btnOne;
        private Button _btnExport;
        private Button _btnApply;
        private Button _btnRefresh;
        private Button _btnZoomOut;
        private Button _btnZoomIn;

        private Rectangle _stageRect;
        private float _ps = 1f;

        private bool _applying;
        private bool _draggingImage;
        private MonitorInfo _draggingMonitor;
        private Point _dragStart;
        private float _dragOx;
        private float _dragOy;
        private int _dragOffset0;
        private Rectangle _frozenArea;
        private Rectangle _frozenStage;
        private float _frozenPs = 1f;
        private Point _mouse;

        public MainForm(string initialImage)
        {
            Text = "WallStretch — обои на несколько мониторов";
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(1400, 700);
            MinimumSize = new Size(1320, 580);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            DoubleBuffered = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            ReloadMonitors();
            LoadSettings();
            Wallpaper.WarmUp();

            BuildTopBar();
            BuildStage();
            BuildMonitorStrip();
            BuildStatus();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            KeyDown += OnKeyDown;

            if (!string.IsNullOrEmpty(initialImage) && File.Exists(initialImage))
            {
                try { LoadImage(initialImage); } catch { }
            }

            UpdateStatus();
        }

        // ---------- monitors ----------

        private void ReloadMonitors()
        {
            var previous = new Dictionary<string, MonitorInfo>();
            if (_monitors != null)
            {
                foreach (var old in _monitors) previous[old.Device] = old;
            }

            _monitors = DisplayInfo.Load(out _vb);
            foreach (var m in _monitors)
            {
                MonitorInfo old;
                if (previous.TryGetValue(m.Device, out old))
                {
                    m.Included = old.Included;
                    m.OffsetY = old.OffsetY;
                }
            }

            RecalcArea();
        }

        private void RecalcArea()
        {
            Rectangle area = Rectangle.Empty;
            foreach (var m in _monitors)
            {
                if (!m.Included) continue;
                area = area.IsEmpty ? m.Frame : Rectangle.Union(area, m.Frame);
            }
            _area = area.Width > 0 ? area : _vb;
        }

        private int CountIncluded()
        {
            int n = 0;
            foreach (var m in _monitors) if (m.Included) n++;
            return n;
        }

        // ---------- settings ----------

        private static string SettingsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallStretch");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.ini");
        }

        private void LoadSettings()
        {
            try
            {
                string path = SettingsPath();
                if (!File.Exists(path)) return;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    if (line.StartsWith("exclude=", StringComparison.Ordinal))
                    {
                        string device = line.Substring(8).Trim();
                        foreach (var m in _monitors) if (m.Device == device) m.Included = false;
                    }
                    else if (line.StartsWith("offset=", StringComparison.Ordinal))
                    {
                        string rest = line.Substring(7);
                        int eq = rest.IndexOf('=');
                        if (eq <= 0) continue;
                        string device = rest.Substring(0, eq).Trim();
                        int value;
                        if (!int.TryParse(rest.Substring(eq + 1).Trim(), out value)) continue;
                        foreach (var m in _monitors) if (m.Device == device) m.OffsetY = value;
                    }
                }

                if (CountIncluded() == 0) foreach (var m in _monitors) m.Included = true;
                RecalcArea();
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var lines = new List<string>();
                foreach (var m in _monitors)
                {
                    if (!m.Included) lines.Add("exclude=" + m.Device);
                    if (m.OffsetY != 0) lines.Add("offset=" + m.Device + "=" + m.OffsetY);
                }
                File.WriteAllLines(SettingsPath(), lines.ToArray());
            }
            catch { }
        }

        // ---------- layout ----------

        private void BuildTopBar()
        {
            _top = new Panel();
            _top.Dock = DockStyle.Top;
            _top.Height = 66;
            _top.BackColor = PanelBg;
            _top.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Line))
                    e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
            };
            Controls.Add(_top);

            var title = new Label();
            title.Text = "WallStretch";
            title.Font = new Font("Segoe UI Semibold", 15f);
            title.ForeColor = TextMain;
            title.AutoSize = true;
            title.Location = new Point(20, 7);
            title.BackColor = Color.Transparent;
            _top.Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "панорама на несколько мониторов";
            subtitle.Font = new Font("Segoe UI", 8.5f);
            subtitle.ForeColor = TextMuted;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(22, 38);
            subtitle.BackColor = Color.Transparent;
            _top.Controls.Add(subtitle);

            _btnPaste = MakeButton("Вставить", 100, false);
            _btnPaste.Click += delegate { PasteImage(); };
            _btnOpen = MakeButton("Открыть…", 100, false);
            _btnOpen.Click += delegate { OpenImage(); };
            _btnCover = MakeButton("Заполнить", 100, false);
            _btnCover.Click += delegate { FitCover(); };
            _btnWidth = MakeButton("По ширине", 100, false);
            _btnWidth.Click += delegate { FitWidth(); };
            _btnOne = MakeButton("1:1", 56, false);
            _btnOne.Click += delegate { FitOneToOne(); };
            _btnExport = MakeButton("Экспорт…", 100, false);
            _btnExport.Click += delegate { Export(); };

            _btnZoomOut = MakeButton("−", 32, false);
            _btnZoomOut.Font = new Font("Segoe UI", 12f);
            _btnZoomOut.Click += delegate { ZoomAtCenter(1f / 1.15f); };
            _btnZoomIn = MakeButton("+", 32, false);
            _btnZoomIn.Font = new Font("Segoe UI", 12f);
            _btnZoomIn.Click += delegate { ZoomAtCenter(1.15f); };

            _zoomLabel = new Label();
            _zoomLabel.Text = "100%";
            _zoomLabel.Font = new Font("Segoe UI", 9f);
            _zoomLabel.ForeColor = TextMain;
            _zoomLabel.BackColor = Color.FromArgb(30, 30, 38);
            _zoomLabel.TextAlign = ContentAlignment.MiddleCenter;
            _zoomLabel.Cursor = Cursors.Hand;
            _zoomLabel.Click += delegate { FitOneToOne(); };
            _tip = new ToolTip();
            _tip.SetToolTip(_zoomLabel, "Масштаб картинки. Клик — вернуть 1:1");

            _btnRefresh = MakeButton("Экраны", 92, false);
            _btnRefresh.Click += delegate { RefreshScreens(); };

            _btnApply = MakeButton("Применить обои", 168, true);
            _btnApply.Click += delegate { ApplyWallpaper(); };

            _top.Controls.Add(_btnPaste);
            _top.Controls.Add(_btnOpen);
            _top.Controls.Add(_btnCover);
            _top.Controls.Add(_btnWidth);
            _top.Controls.Add(_btnOne);
            _top.Controls.Add(_btnExport);
            _top.Controls.Add(_btnZoomOut);
            _top.Controls.Add(_zoomLabel);
            _top.Controls.Add(_btnZoomIn);
            _top.Controls.Add(_btnRefresh);
            _top.Controls.Add(_btnApply);

            LayoutTopBar();
            _top.Resize += delegate { LayoutTopBar(); };
        }

        private void LayoutTopBar()
        {
            int y = 16;
            int x = 224;
            Button[] left = new Button[] { _btnPaste, _btnOpen, _btnCover, _btnWidth, _btnOne, _btnExport };
            foreach (var b in left)
            {
                b.SetBounds(x, y, b.Width, 34);
                x += b.Width + 8;
            }

            x += 10;
            _btnZoomOut.SetBounds(x, y + 2, 32, 30);
            x += 36;
            _zoomLabel.SetBounds(x, y + 2, 54, 30);
            x += 58;
            _btnZoomIn.SetBounds(x, y + 2, 32, 30);

            int right = _top.ClientSize.Width - 18;
            _btnApply.SetBounds(right - _btnApply.Width, y, _btnApply.Width, 34);
            right -= _btnApply.Width + 8;
            _btnRefresh.SetBounds(right - _btnRefresh.Width, y, _btnRefresh.Width, 34);
        }

        private Button MakeButton(string text, int width, bool accent)
        {
            var b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 34;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = accent ? Accent : BtnBg;
            b.ForeColor = accent ? Color.FromArgb(32, 22, 0) : TextMain;
            b.Font = new Font("Segoe UI" + (accent ? " Semibold" : ""), 9f);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(255, 194, 120) : BtnHover;
            b.FlatAppearance.MouseDownBackColor = accent ? Color.FromArgb(240, 160, 70) : Color.FromArgb(62, 62, 74);
            b.UseVisualStyleBackColor = false;
            return b;
        }

        private void BuildStage()
        {
            _stage = new BufferedPanel();
            _stage.Dock = DockStyle.Fill;
            _stage.BackColor = Bg;
            _stage.Paint += StagePaint;
            _stage.MouseDown += StageMouseDown;
            _stage.MouseMove += StageMouseMove;
            _stage.MouseUp += StageMouseUp;
            _stage.MouseWheel += StageMouseWheel;
            _stage.MouseDoubleClick += StageDoubleClick;
            _stage.AllowDrop = true;
            _stage.DragEnter += OnDragEnter;
            _stage.DragDrop += OnDragDrop;
            Controls.Add(_stage);
        }

        private void BuildMonitorStrip()
        {
            _chipPanel = new Panel();
            _chipPanel.Dock = DockStyle.Bottom;
            _chipPanel.Height = 46;
            _chipPanel.BackColor = PanelBg;
            _chipPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Line))
                    e.Graphics.DrawLine(pen, 0, 0, _chipPanel.Width, 0);
            };
            Controls.Add(_chipPanel);

            var label = new Label();
            label.Text = "Панорама на:";
            label.AutoSize = true;
            label.Location = new Point(20, 15);
            label.ForeColor = TextMuted;
            label.Font = new Font("Segoe UI", 8.5f);
            label.BackColor = Color.Transparent;
            _chipPanel.Controls.Add(label);

            _tip.SetToolTip(label, "Нажмите на экран, чтобы исключить его из панорамы или вернуть обратно");

            RebuildChips();
        }

        private void BuildStatus()
        {
            _status = new Label();
            _status.Dock = DockStyle.Bottom;
            _status.Height = 34;
            _status.BackColor = PanelBg;
            _status.ForeColor = TextMuted;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(18, 0, 18, 0);
            _status.Font = new Font("Segoe UI", 8.5f);
            Controls.Add(_status);
        }

        private void RebuildChips()
        {
            if (_chipPanel == null) return;

            foreach (var b in _chipButtons)
            {
                _chipPanel.Controls.Remove(b);
                b.Dispose();
            }
            _chipButtons.Clear();

            int x = 132;
            foreach (var m in _monitors)
            {
                bool on = m.Included;
                var b = new Button();
                b.Text = (on ? "✓  " : "✗  ") + m.Name + "   " + m.Bounds.Width + "×" + m.Bounds.Height;
                b.Font = new Font("Segoe UI", 8.5f);
                b.Height = 30;
                b.Top = 8;
                b.Left = x;
                b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 26;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = on ? Color.FromArgb(76, 76, 92) : Color.FromArgb(34, 34, 42);
                b.BackColor = on ? Color.FromArgb(42, 42, 53) : Color.FromArgb(25, 25, 31);
                b.ForeColor = on ? TextMain : Color.FromArgb(104, 104, 120);
                b.FlatAppearance.MouseOverBackColor = on ? Color.FromArgb(54, 54, 66) : Color.FromArgb(32, 32, 40);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(62, 62, 76);
                b.Cursor = Cursors.Hand;
                b.TabStop = false;
                b.UseVisualStyleBackColor = false;

                var monitor = m;
                b.Click += delegate { ToggleMonitor(monitor); };
                _tip.SetToolTip(b, on ? "Нажмите, чтобы исключить экран" : "Нажмите, чтобы вернуть экран");

                _chipPanel.Controls.Add(b);
                _chipButtons.Add(b);
                x += b.Width + 8;
            }
        }

        private void ToggleMonitor(MonitorInfo m)
        {
            if (m.Included && CountIncluded() <= 1) return;

            m.Included = !m.Included;
            if (CountIncluded() == 0) m.Included = true;

            RecalcArea();
            RebuildChips();
            SaveSettings();

            if (_image != null) FitCover();
            _stage.Invalidate();
            UpdateStatus();
        }

        // ---------- painting ----------

        private void StagePaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Bg);

            if (_monitors.Count == 0) return;

            Rectangle area = _draggingMonitor != null ? _frozenArea : _area;

            int pad = 30;
            int availW = _stage.ClientSize.Width - pad * 2;
            int availH = _stage.ClientSize.Height - pad * 2;
            if (availW <= 10 || availH <= 10) return;

            float fit = Math.Min((float)availW / area.Width, (float)availH / area.Height);
            if (_draggingMonitor != null) fit = _frozenPs;

            int sw = Math.Max(1, (int)(area.Width * fit));
            int sh = Math.Max(1, (int)(area.Height * fit));
            Rectangle stageRect = _draggingMonitor != null
                ? _frozenStage
                : new Rectangle((_stage.ClientSize.Width - sw) / 2, (_stage.ClientSize.Height - sh) / 2, sw, sh);

            _stageRect = stageRect;
            _ps = fit;

            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillRectangle(shadow, stageRect.X + 5, stageRect.Y + 7, stageRect.Width, stageRect.Height);

            g.FillRectangle(Brushes.Black, stageRect);

            if (_image != null)
            {
                var dest = new RectangleF(
                    stageRect.X + (_ox - area.X) * _ps,
                    stageRect.Y + (_oy - area.Y) * _ps,
                    _image.Width * _scale * _ps,
                    _image.Height * _scale * _ps);

                using (var clip = new Region())
                {
                    clip.MakeEmpty();
                    foreach (var m in _monitors)
                    {
                        if (m.Included) clip.Union(MonitorRect(m, area, stageRect));
                    }

                    GraphicsState st = g.Save();
                    g.SetClip(clip, CombineMode.Replace);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(_preview, dest);
                    g.Restore(st);
                }
            }

            using (var pen = new Pen(Color.FromArgb(135, 255, 255, 255), 1.1f))
            {
                foreach (var m in _monitors)
                {
                    if (!m.Included) continue;
                    Rectangle r = MonitorRect(m, area, stageRect);
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
            }

            if (_image == null)
            {
                using (var font = new Font("Segoe UI", 11f))
                using (var brush = new SolidBrush(Color.FromArgb(120, 120, 136)))
                {
                    var fmt = new StringFormat();
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString("Перетащите изображение сюда\r\nили нажмите Ctrl+V / «Открыть»", font, brush, stageRect, fmt);
                }
            }

            foreach (var m in _monitors)
            {
                if (!m.Included) continue;
                Rectangle r = MonitorRect(m, area, stageRect);
                string label = m.Name + "   " + m.Bounds.Width + "×" + m.Bounds.Height;
                if (m.OffsetY != 0) label += "   " + (m.OffsetY > 0 ? "+" : "") + m.OffsetY;
                DrawBadge(g, r, label);

                Rectangle grip = GripRect(r);
                bool hot = grip.Contains(_mouse);
                DrawGrip(g, grip, hot);
            }
        }

        private Rectangle MonitorRect(MonitorInfo m, Rectangle area, Rectangle stageRect)
        {
            return new Rectangle(
                stageRect.X + (int)Math.Round((m.Frame.X - area.X) * _ps),
                stageRect.Y + (int)Math.Round((m.Frame.Y - area.Y) * _ps),
                Math.Max(1, (int)Math.Round(m.Bounds.Width * _ps)),
                Math.Max(1, (int)Math.Round(m.Bounds.Height * _ps)));
        }

        private static Rectangle GripRect(Rectangle frame)
        {
            int w = Math.Min(48, Math.Max(20, frame.Width / 6));
            return new Rectangle(frame.X + frame.Width / 2 - w / 2, frame.Y + 4, w, 12);
        }

        private static void DrawGrip(Graphics g, Rectangle grip, bool hot)
        {
            if (grip.Width < 16) return;

            using (var path = RoundedRect(grip, 6))
            using (var back = new SolidBrush(Color.FromArgb(hot ? 210 : 120, 10, 10, 14)))
            using (var pen = new Pen(Color.FromArgb(hot ? 235 : 150, 255, 255, 255), 1f))
            {
                g.FillPath(back, path);
                g.DrawPath(pen, path);
            }

            using (var dot = new SolidBrush(Color.FromArgb(hot ? 240 : 170, 255, 255, 255)))
            {
                int cy = grip.Y + grip.Height / 2;
                for (int i = -1; i <= 1; i++)
                {
                    g.FillEllipse(dot, grip.X + grip.Width / 2 + i * 8 - 1, cy - 1, 3, 3);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawBadge(Graphics g, Rectangle r, string text)
        {
            if (r.Width < 120 || r.Height < 40) return;

            using (var font = new Font("Segoe UI", 8f))
            {
                SizeF size = g.MeasureString(text, font);
                int w = (int)size.Width + 12;
                int h = (int)size.Height + 4;
                var box = new Rectangle(r.X + 8, r.Bottom - h - 8, Math.Min(w, r.Width - 16), h);

                using (var back = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                    g.FillRectangle(back, box);
                using (var brush = new SolidBrush(Color.FromArgb(225, 235, 235, 245)))
                    g.DrawString(text, font, brush, box.X + 6, box.Y + 1);
            }
        }

        // ---------- interaction ----------

        private MonitorInfo MonitorUnder(Point p)
        {
            foreach (var m in _monitors)
            {
                if (!m.Included) continue;
                Rectangle r = MonitorRect(m, _area, _stageRect);
                if (GripRect(r).Contains(p)) return m;
            }
            return null;
        }

        private void StageMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!_stageRect.Contains(e.Location)) return;

            MonitorInfo grip = _image == null ? null : MonitorUnder(e.Location);
            if (grip != null)
            {
                _draggingMonitor = grip;
                _dragStart = e.Location;
                _dragOffset0 = grip.OffsetY;
                _frozenArea = _area;
                _frozenStage = _stageRect;
                _frozenPs = _ps;
                _stage.Cursor = Cursors.SizeNS;
                return;
            }

            if (_image == null) return;

            _draggingImage = true;
            _dragStart = e.Location;
            _dragOx = _ox;
            _dragOy = _oy;
            _stage.Cursor = Cursors.SizeAll;
        }

        private void StageMouseMove(object sender, MouseEventArgs e)
        {
            _mouse = e.Location;

            if (_draggingMonitor != null)
            {
                int delta = (int)Math.Round((e.Y - _dragStart.Y) / _frozenPs);
                _draggingMonitor.OffsetY = _dragOffset0 + delta;
                _stage.Invalidate();
                UpdateStatus();
                return;
            }

            if (_draggingImage)
            {
                _ox = _dragOx + (e.X - _dragStart.X) / _ps;
                _oy = _dragOy + (e.Y - _dragStart.Y) / _ps;
                _stage.Invalidate();
                UpdateStatus();
                return;
            }

            if (_image != null)
            {
                _stage.Cursor = MonitorUnder(e.Location) != null ? Cursors.SizeNS : Cursors.Default;
                _stage.Invalidate();
            }
        }

        private void StageMouseUp(object sender, MouseEventArgs e)
        {
            if (_draggingMonitor != null)
            {
                _draggingMonitor = null;
                RecalcArea();
                SaveSettings();
                _stage.Cursor = Cursors.Default;
                _stage.Invalidate();
                UpdateStatus();
                return;
            }

            if (!_draggingImage) return;
            _draggingImage = false;
            _stage.Cursor = Cursors.Default;
        }

        private void StageMouseWheel(object sender, MouseEventArgs e)
        {
            if (_image == null) return;
            if (!_stageRect.Contains(e.Location)) return;
            ZoomAt(e.Location, e.Delta > 0 ? 1.12f : 1f / 1.12f);
            ((HandledMouseEventArgs)e).Handled = true;
        }

        private void StageDoubleClick(object sender, MouseEventArgs e)
        {
            if (_image == null) return;

            MonitorInfo grip = MonitorUnder(e.Location);
            if (grip != null)
            {
                grip.OffsetY = 0;
                RecalcArea();
                SaveSettings();
                _stage.Invalidate();
                UpdateStatus();
                return;
            }

            FitCover();
        }

        private void ZoomAt(Point p, float factor)
        {
            float vx = _area.X + (p.X - _stageRect.X) / _ps;
            float vy = _area.Y + (p.Y - _stageRect.Y) / _ps;
            float ix = (vx - _ox) / _scale;
            float iy = (vy - _oy) / _scale;

            _scale = Math.Max(0.02f, Math.Min(30f, _scale * factor));
            _ox = vx - ix * _scale;
            _oy = vy - iy * _scale;

            _stage.Invalidate();
            UpdateStatus();
        }

        private void ZoomAtCenter(float factor)
        {
            if (_image == null) return;
            var p = new Point(_stageRect.X + _stageRect.Width / 2, _stageRect.Y + _stageRect.Height / 2);
            ZoomAt(p, factor);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O) { OpenImage(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.S) { Export(); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.V) { PasteImage(); e.Handled = true; return; }

            if (_image == null) return;

            int step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left) { _ox -= step; }
            else if (e.KeyCode == Keys.Right) { _ox += step; }
            else if (e.KeyCode == Keys.Up) { _oy -= step; }
            else if (e.KeyCode == Keys.Down) { _oy += step; }
            else return;

            _stage.Invalidate();
            UpdateStatus();
            e.Handled = true;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) LoadImage(files[0]);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // ---------- commands ----------

        private void PasteImage()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage())
                    {
                        if (img != null) { LoadImage(new Bitmap(img), "из буфера обмена"); return; }
                    }
                }

                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0) { LoadImage(files[0]); return; }
                }

                MessageBox.Show(this, "В буфере обмена нет картинки.", "WallStretch",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void OpenImage()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Выберите изображение для обоев";
                dlg.Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try { LoadImage(dlg.FileName); }
                    catch (Exception ex) { ShowError(ex); }
                }
            }
        }

        private void LoadImage(string path)
        {
            using (Image img = Image.FromFile(path))
            {
                LoadImage(ApplyOrientation(img), Path.GetFileName(path));
            }
        }

        private static Bitmap ApplyOrientation(Image img)
        {
            RotateFlipType rotate = RotateFlipType.RotateNoneFlipNone;
            try
            {
                const int orientationId = 0x0112;
                if (Array.IndexOf(img.PropertyIdList, orientationId) >= 0)
                {
                    int value = img.GetPropertyItem(orientationId).Value[0];
                    switch (value)
                    {
                        case 2: rotate = RotateFlipType.RotateNoneFlipX; break;
                        case 3: rotate = RotateFlipType.Rotate180FlipNone; break;
                        case 4: rotate = RotateFlipType.Rotate180FlipX; break;
                        case 5: rotate = RotateFlipType.Rotate90FlipX; break;
                        case 6: rotate = RotateFlipType.Rotate90FlipNone; break;
                        case 7: rotate = RotateFlipType.Rotate270FlipX; break;
                        case 8: rotate = RotateFlipType.Rotate270FlipNone; break;
                    }
                }
            }
            catch { }

            var bmp = new Bitmap(img);
            if (rotate != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(rotate);
            return bmp;
        }

        private void LoadImage(Bitmap bmp, string name)
        {
            if (_image != null) _image.Dispose();
            if (_preview != null) _preview.Dispose();

            _image = bmp;
            _preview = MakePreview(bmp, 3200);
            _imageName = name;
            Text = "WallStretch — " + name;

            FitCover();
            _stage.Invalidate();
            UpdateStatus();
        }

        private static Bitmap MakePreview(Image src, int maxSide)
        {
            float k = Math.Min(1f, (float)maxSide / Math.Max(src.Width, src.Height));
            if (k >= 1f) return new Bitmap(src);

            int w = Math.Max(1, (int)Math.Round(src.Width * k));
            int h = Math.Max(1, (int)Math.Round(src.Height * k));
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return bmp;
        }

        private void FitCover()
        {
            if (_image == null) return;
            _scale = Math.Max((float)_area.Width / _image.Width, (float)_area.Height / _image.Height);
            _ox = _area.X + (_area.Width - _image.Width * _scale) / 2f;
            _oy = _area.Y + (_area.Height - _image.Height * _scale) / 2f;
            _stage.Invalidate();
            UpdateStatus();
        }

        private void FitWidth()
        {
            if (_image == null) return;
            _scale = (float)_area.Width / _image.Width;
            _ox = _area.X;
            _oy = _area.Y + (_area.Height - _image.Height * _scale) / 2f;
            _stage.Invalidate();
            UpdateStatus();
        }

        private void FitOneToOne()
        {
            if (_image == null) return;
            _scale = 1f;
            _ox = _area.X + (_area.Width - _image.Width) / 2f;
            _oy = _area.Y + (_area.Height - _image.Height) / 2f;
            _stage.Invalidate();
            UpdateStatus();
        }

        private void RefreshScreens()
        {
            ReloadMonitors();
            if (_image != null) FitCover();
            RebuildChips();
            _stage.Invalidate();
            UpdateStatus();
        }

        private void Export()
        {
            if (_image == null) { NeedImage(); return; }

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Куда сохранить картинки для каждого монитора";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Cursor = Cursors.WaitCursor;
                    foreach (var m in _monitors)
                    {
                        if (!m.Included) continue;
                        using (Bitmap bmp = ComposeMonitor(m))
                        {
                            string file = Path.Combine(dlg.SelectedPath,
                                SafeName(m.Name) + "-" + m.Bounds.Width + "x" + m.Bounds.Height + ".jpg");
                            Wallpaper.SaveJpeg(bmp, file, 92);
                        }
                    }
                    _status.Text = "Сохранено в " + dlg.SelectedPath;
                }
                catch (Exception ex) { ShowError(ex); }
                finally { Cursor = Cursors.Default; }
            }
        }

        private static string SafeName(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if ("\\/:*?\"<>|".IndexOf(chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }

        private Bitmap ComposeMonitor(MonitorInfo m)
        {
            var bmp = new Bitmap(m.Bounds.Width, m.Bounds.Height, PixelFormat.Format24bppRgb);
            bmp.SetResolution(96f, 96f);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                float x = _ox - m.Frame.X;
                float y = _oy - m.Frame.Y;
                g.DrawImage(_image, new RectangleF(x, y, _image.Width * _scale, _image.Height * _scale));
            }
            return bmp;
        }

        private void ApplyWallpaper()
        {
            if (_image == null) { NeedImage(); return; }
            if (_applying) return;

            _applying = true;
            _btnApply.Enabled = false;
            _status.Text = "Собираю и применяю обои…";
            _status.Refresh();

            // Do the COM work outside of the click handler: inside an input-synchronous
            // call Windows refuses to create the desktop-wallpaper COM object.
            BeginInvoke(new MethodInvoker(ApplyWallpaperNow));
        }

        private void ApplyWallpaperNow()
        {
            try
            {
                Cursor = Cursors.WaitCursor;

                var images = new List<MonitorImage>();
                foreach (var m in _monitors)
                {
                    if (!m.Included) continue;
                    images.Add(new MonitorImage(m, ComposeMonitor(m)));
                }

                List<string> files = Wallpaper.ApplyPerMonitor(images);
                foreach (var mi in images) mi.Bitmap.Dispose();

                _status.Text = "Готово: обои применены на " + files.Count + " экран(а)";
            }
            catch (Exception ex)
            {
                ShowApplyError(ex);
            }
            finally
            {
                _applying = false;
                _btnApply.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void ShowApplyError(Exception ex)
        {
            string text = ex.Message;
            if (text.IndexOf("8001010D", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                text = "Windows был занят синхронной операцией. Нажмите «Применить обои» ещё раз.";
            }
            MessageBox.Show(this, "Не удалось применить обои.\n\n" + text, "WallStretch",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void NeedImage()
        {
            MessageBox.Show(this, "Сначала вставьте или откройте изображение.", "WallStretch",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateStatus()
        {
            if (_status == null || _monitors == null) return;

            int included = CountIncluded();
            string monitors = "Экранов: " + included + " из " + _monitors.Count +
                              " · область " + _area.Width + "×" + _area.Height;

            string image;
            if (_image != null)
            {
                image = " · картинка " + _image.Width + "×" + _image.Height;
            }
            else
            {
                image = " · вставьте картинку (Ctrl+V) или перетащите файл";
            }

            string excluded = string.Empty;
            if (included < _monitors.Count)
            {
                var names = new List<string>();
                foreach (var m in _monitors) if (!m.Included) names.Add(m.Name);
                excluded = " · вне панорамы: " + string.Join(", ", names.ToArray());
            }

            _status.Text = monitors + image + excluded;

            if (_zoomLabel != null)
            {
                _zoomLabel.Text = _image == null ? "—" : Math.Round(_scale * 100) + "%";
            }
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "WallStretch", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_image != null) _image.Dispose();
                if (_preview != null) _preview.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
