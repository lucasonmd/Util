using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MaterialDesignThemes.Wpf;

namespace WpfOverlayApp
{
    public partial class MainWindow : Window
    {
        private static readonly string SavePath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout.json");

        private static readonly string LaunchPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launch.json");

        private Process? _exeProcess;

        private static readonly List<Rect> DefaultBoxes = new()
        {
            new Rect(0,    108, 1536, 864),  // MainView
            new Rect(1536, 0,   384,  216),  // SubView 1
            new Rect(1536, 216, 384,  216),  // SubView 2
            new Rect(1536, 432, 384,  216),  // SubView 3
            new Rect(1536, 648, 384,  216),  // SubView 4
            new Rect(1536, 864, 384,  216),  // SubView 5
        };

        private static readonly string[] BoxNames =
            { "MainView", "SubView 1~5" };

        private const double AspectRatio = 1920.0 / 1080.0;
        private const double HandleSize  = 12;
        private const double MinSize     = 40;

        private static readonly string[] MainViewButtons =
            { "SA", "WPN", "DEF", "SYS", "DRV", "STR", "COM", "BMS" };

        private const double TabBarH = 63;

        private List<Rect>   _boxes          = DefaultBoxes.Select(r => r).ToList();
        private List<string> _subLabels      = new() { "SUB 1", "SUB 2", "SUB 3", "SUB 4", "SUB 5" };
        private bool         _editMode       = false;
        private int          _selectedIndex  = -1;
        private int          _activeSubView  = -1;  // 0~4
        private int          _activeMainTab  = -1;  // 0~7
        private bool         _suppressKeyDown = false; // 내부 SimulateKey 이중처리 방지

        private int            _dragIndex        = -1;
        private Point          _dragStart;
        private Rect           _dragOrigRect;
        private List<Rect>     _dragGroupOrig    = new();
        private ResizeDir      _dragDir;

        internal enum ResizeDir { Move, NW, NE, SW, SE }

        private static bool IsSubView(int i) => i >= 1;

        public MainWindow()
        {
            InitializeComponent();
            LoadLayout();
            LoadLaunchConfig();
            LayoutCanvas.MouseMove         += OnCanvasMouseMove;
            LayoutCanvas.MouseLeftButtonUp += OnCanvasMouseUp;
            KeyDown += Window_KeyDown;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = 0;
            Top  = 0;
            RenderAll();
            Focus();
        }

        // ── 저장 / 불러오기 ──────────────────────────────────────

        private void LoadLayout()
        {
            try
            {
                if (!System.IO.File.Exists(SavePath)) return;
                var opts  = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var saved = JsonSerializer.Deserialize<List<RectData>>(
                    System.IO.File.ReadAllText(SavePath), opts);
                if (saved is { Count: > 0 })
                {
                    _boxes = saved.Select(r => new Rect(r.X, r.Y, r.W, r.W / AspectRatio)).ToList();
                    // 레이블 (index 1~5 = SubView 0~4)
                    for (int i = 0; i < 5 && i + 1 < saved.Count; i++)
                        if (!string.IsNullOrEmpty(saved[i + 1].Label))
                            _subLabels[i] = saved[i + 1].Label;
                }
            }
            catch { }
        }

        private void SaveLayout()
        {
            var data = _boxes.Select((r, i) => new RectData(
                r.X, r.Y, r.Width,
                i >= 1 ? _subLabels[i - 1] : ""
            )).ToList();
            System.IO.File.WriteAllText(SavePath, JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        // ── 렌더링 ───────────────────────────────────────────────

        private void RenderAll()
        {
            LayoutCanvas.Children.Clear();
            RenderBackground();
            RenderBoxBorders();
            if (_editMode)
            {
                RenderBoxOverlay(0, _selectedIndex == 0);
                RenderGroupOverlay(_selectedIndex >= 1);
            }
            else
            {
                RenderSubViewPanels();
            }
            RenderMainViewButtons();
        }

        private void RenderSubViewPanels()
        {
            for (int i = 0; i < 5; i++)
            {
                var b   = _boxes[i + 1];
                bool sel = _activeSubView == i;
                int  ci  = i;

                // 외부 Border (배경 + 테두리)
                var outer = new Border
                {
                    Width           = b.Width,
                    Height          = b.Height,
                    Background      = sel
                        ? new SolidColorBrush(Color.FromArgb(18, 0, 122, 255))
                        : new SolidColorBrush(Color.FromArgb(4, 255, 255, 255)),
                    BorderBrush     = sel
                        ? new SolidColorBrush(Color.FromArgb(200, 0, 122, 255))
                        : new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)),
                    BorderThickness = new Thickness(sel ? 2 : 1),
                    ClipToBounds    = true,
                    Cursor          = Cursors.Hand
                };
                Canvas.SetLeft(outer, b.X);
                Canvas.SetTop(outer, b.Y);
                Panel.SetZIndex(outer, 2);

                // 콘텐츠 Grid
                var grid = new Grid { Width = b.Width, Height = b.Height };

                // 레이블 (항상 표시)
                grid.Children.Add(new TextBlock
                {
                    Text       = _subLabels[i],
                    Foreground = sel
                        ? new SolidColorBrush(Color.FromArgb(220, 60, 180, 255))
                        : new SolidColorBrush(Color.FromArgb(80, 160, 160, 160)),
                    FontSize            = 13,
                    FontWeight          = FontWeights.Medium,
                    Margin              = new Thickness(10, 8, 0, 0),
                    VerticalAlignment   = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsHitTestVisible    = false
                });

                outer.Child = grid;

                // 클릭 → 직접 선택 처리 후 외부용 키 이벤트 주입
                outer.MouseLeftButtonDown += (s, e) =>
                {
                    _activeSubView = ci;
                    var pt = e.GetPosition(LayoutCanvas);
                    RenderAll();
                    FireMdRipple(_boxes[ci + 1], pt);
                    _suppressKeyDown = true;
                    Focus();
                    SimulateKey(VK_LSHIFT, (ushort)(0x30 + ci));
                    Dispatcher.InvokeAsync(() => _suppressKeyDown = false,
                        System.Windows.Threading.DispatcherPriority.Input);
                    e.Handled = true;
                };

                // 선택 시 스케일 인 애니메이션
                if (sel)
                {
                    outer.RenderTransformOrigin = new Point(0.5, 0.5);
                    outer.RenderTransform       = new ScaleTransform(0.97, 0.97);
                    var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
                        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    ((ScaleTransform)outer.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    ((ScaleTransform)outer.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }

                LayoutCanvas.Children.Add(outer);
            }
        }

        // MD 스타일 원형 리플 (클릭 지점에서 확장, SubView 영역에 클리핑)
        private void FireMdRipple(Rect box, Point clickPt)
        {
            double maxR = Math.Sqrt(
                Math.Pow(Math.Max(clickPt.X - box.X, box.Right  - clickPt.X), 2) +
                Math.Pow(Math.Max(clickPt.Y - box.Y, box.Bottom - clickPt.Y), 2));

            var geo = new EllipseGeometry(clickPt, 2, 2);
            var shape = new System.Windows.Shapes.Path
            {
                Data             = geo,
                Fill             = new SolidColorBrush(Color.FromArgb(38, 60, 150, 255)),
                IsHitTestVisible = false,
                Clip             = new RectangleGeometry(box)
            };
            Panel.SetZIndex(shape, 10);
            LayoutCanvas.Children.Add(shape);

            var dur  = TimeSpan.FromMilliseconds(400);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            geo.BeginAnimation(EllipseGeometry.RadiusXProperty,
                new DoubleAnimation(maxR, dur) { EasingFunction = ease });
            geo.BeginAnimation(EllipseGeometry.RadiusYProperty,
                new DoubleAnimation(maxR, dur) { EasingFunction = ease });
            shape.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)));

            var t = new System.Windows.Threading.DispatcherTimer
                { Interval = dur + TimeSpan.FromMilliseconds(50) };
            t.Tick += (_, _) => { LayoutCanvas.Children.Remove(shape); t.Stop(); };
            t.Start();
        }

        private void RenderMainViewButtons()
        {
            var mv   = _boxes[0];
            double barY = mv.Y - TabBarH;

            // ── 탭 바 컨테이너 ──
            var bar = new Border
            {
                Width           = mv.Width,
                Height          = TabBarH,
                Background      = new SolidColorBrush(Color.FromArgb(0xE8, 0x25, 0x26, 0x28)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x40, 0x50, 0x50, 0x55)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                ClipToBounds    = true
            };
            Canvas.SetLeft(bar, mv.X);
            Canvas.SetTop(bar, barY);
            Panel.SetZIndex(bar, 3);

            // ── 균등 분할 Grid ──
            var grid = new Grid { Width = mv.Width, Height = TabBarH };
            for (int i = 0; i < MainViewButtons.Length; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var flatStyle = (Style)Application.Current.FindResource("MaterialDesignFlatButton");

            for (int i = 0; i < MainViewButtons.Length; i++)
            {
                bool isActive = i == _activeMainTab;

                var btn = new Button
                {
                    Content             = MainViewButtons[i],
                    Style               = flatStyle,
                    MinHeight           = 0,
                    MinWidth            = 0,
                    Height              = TabBarH,
                    Padding             = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment   = VerticalAlignment.Stretch,
                    Cursor              = Cursors.Hand,
                    FontSize            = 15,
                    FontWeight          = isActive ? FontWeights.SemiBold : FontWeights.Medium,
                    Foreground          = isActive
                        ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF))
                        : new SolidColorBrush(Color.FromArgb(0x88, 0xAA, 0xAA, 0xAA)),
                    Background          = isActive
                        ? new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF))
                        : Brushes.Transparent,
                };
                btn.Click += OnMainViewButtonClick;
                Grid.SetColumn(btn, i);
                grid.Children.Add(btn);

                // 활성 탭 하단 인디케이터
                if (isActive)
                {
                    var indicator = new Rectangle
                    {
                        Width               = double.NaN,
                        Height              = 2,
                        Fill                = new SolidColorBrush(Color.FromArgb(0xDD, 0x00, 0x7A, 0xFF)),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment   = VerticalAlignment.Bottom,
                        IsHitTestVisible    = false,
                        Margin              = new Thickness(8, 0, 8, 0)
                    };
                    Grid.SetColumn(indicator, i);
                    Panel.SetZIndex(indicator, 2);
                    grid.Children.Add(indicator);
                }

                // 탭 간 구분선 (첫 번째 제외)
                if (i > 0)
                {
                    var sep = new Rectangle
                    {
                        Width               = 1,
                        Height              = TabBarH * 0.4,
                        Fill                = new SolidColorBrush(Color.FromArgb(0x30, 0x88, 0x88, 0x88)),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment   = VerticalAlignment.Center,
                        IsHitTestVisible    = false
                    };
                    Grid.SetColumn(sep, i);
                    Panel.SetZIndex(sep, 1);
                    grid.Children.Add(sep);
                }
            }

            bar.Child = grid;
            LayoutCanvas.Children.Add(bar);
        }

        // ── 키보드 단축키 ────────────────────────────────────────

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_suppressKeyDown) { e.Handled = true; return; }

            bool ctrl   = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool lshift = Keyboard.IsKeyDown(Key.LeftShift);

            // Ctrl + 1~8 → 탭 버튼 (SA~BMS)
            if (ctrl && !lshift)
            {
                int n = ToNumKey(e.Key);
                if (n >= 1 && n <= 8)
                {
                    ActivateMainButton(n - 1);
                    e.Handled = true;
                }
            }
            // LShift + 0~4 → SubView 선택
            else if (lshift && !ctrl)
            {
                int n = ToNumKey(e.Key);
                if (n >= 0 && n <= 4)
                {
                    _activeSubView = n;
                    RenderAll();
                    var box = _boxes[n + 1];
                    FireMdRipple(box, new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
                    e.Handled = true;
                }
            }
        }

        private static int ToNumKey(Key k) => k switch
        {
            Key.D0 or Key.NumPad0 => 0,
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            _ => -1
        };

        // 키보드 트리거 — FireMdRipple 시각 피드백 포함
        private void ActivateMainButton(int index)
        {
            if (index < 0 || index >= MainViewButtons.Length) return;
            _activeMainTab = index;
            DoMainButtonAction(index);
            RenderAll();

            var mv    = _boxes[0];
            double tabW = mv.Width / MainViewButtons.Length;
            double barY = mv.Y - TabBarH;
            var box   = new Rect(mv.X + index * tabW, barY, tabW, TabBarH);
            FireMdRipple(box, new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
        }

        // 마우스 클릭 — LCtrl + 1~8 키보드 이벤트 주입 → Window_KeyDown 경유
        private void OnMainViewButtonClick(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            int idx = Array.IndexOf(MainViewButtons, btn.Content as string);
            if (idx >= 0)
                SimulateKey(VK_LCONTROL, (ushort)(0x31 + idx)); // VK '1'~'8'
        }

        private void DoMainButtonAction(int index)
        {
            // TODO: 버튼별 동작 구현 (index 0~7 → SA~BMS)
        }

        // ── 실행 설정 패널 ────────────────────────────────────────

        private void LoadLaunchConfig()
        {
            try
            {
                if (!System.IO.File.Exists(LaunchPath)) return;
                var cfg = JsonSerializer.Deserialize<LaunchConfig>(
                    System.IO.File.ReadAllText(LaunchPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg is null) return;
                TbExePath.Text = cfg.ExePath;
                TbExeArg.Text  = cfg.Arg;
            }
            catch { }
        }

        private void SaveLaunchConfig()
        {
            var cfg = new LaunchConfig(TbExePath.Text, TbExeArg.Text);
            System.IO.File.WriteAllText(LaunchPath,
                JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void LaunchPanelToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = LaunchPanel.Visibility != Visibility.Visible;
            LaunchPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            LaunchPanelBtn.IsChecked = show;
            UpdateLaunchStatus();
        }

        private void BrowseExe_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
                Title  = "실행 파일 선택"
            };
            if (dlg.ShowDialog() == true)
            {
                TbExePath.Text = dlg.FileName;
                SaveLaunchConfig();
            }
        }

        private void LaunchExe_Click(object sender, RoutedEventArgs e)
        {
            var exePath = TbExePath.Text.Trim();
            if (string.IsNullOrEmpty(exePath)) return;

            try
            {
                var psi = new ProcessStartInfo(exePath)
                {
                    Arguments       = TbExeArg.Text.Trim(),
                    UseShellExecute = true
                };
                _exeProcess = Process.Start(psi);
                _exeProcess!.EnableRaisingEvents = true;
                _exeProcess.Exited += (_, _) =>
                    Dispatcher.Invoke(UpdateLaunchStatus);
                SaveLaunchConfig();
            }
            catch (Exception ex)
            {
                TbLaunchStatus.Text = $"오류: {ex.Message}";
            }
            UpdateLaunchStatus();
        }

        private void StopExe_Click(object sender, RoutedEventArgs e)
        {
            try { _exeProcess?.Kill(entireProcessTree: true); }
            catch { }
            _exeProcess = null;
            UpdateLaunchStatus();
        }

        private void UpdateLaunchStatus()
        {
            bool running = _exeProcess is { HasExited: false };
            TbLaunchStatus.Text      = running ? "실행 중" : "대기 중";
            TbLaunchStatus.Foreground = running
                ? new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x77))
                : new SolidColorBrush(Color.FromArgb(0x88, 0x55, 0x70, 0x80));
            StatusDot.Fill = running
                ? new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x77))
                : new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x55));
            BtnLaunch.IsEnabled = !running;
            BtnStop.IsEnabled   = running;
        }

        // ── 키보드 이벤트 시뮬레이션 (SendInput) ─────────────────

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint       type;
            [FieldOffset(4)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint   dwFlags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD  = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const ushort VK_LCONTROL = 0xA2;
        private const ushort VK_LSHIFT   = 0xA0;

        // modifier(LCtrl/LShift) + vk 를 Down→Up 순서로 주입
        private static void SimulateKey(ushort modifier, ushort vk)
        {
            var inputs = new[]
            {
                new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = modifier } },
                new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk  } },
                new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk,       dwFlags = KEYEVENTF_KEYUP } },
                new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = modifier, dwFlags = KEYEVENTF_KEYUP } },
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        private void RenderBackground()
        {
            Geometry bg = new RectangleGeometry(new Rect(0, 0, 1920, 1080));
            foreach (var box in _boxes)
                bg = new CombinedGeometry(GeometryCombineMode.Exclude, bg,
                        new RectangleGeometry(box));

            var path = new System.Windows.Shapes.Path
            {
                Data             = bg,
                Fill             = new SolidColorBrush(Color.FromRgb(0x19, 0x1A, 0x1C)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(path, 0);
            LayoutCanvas.Children.Add(path);
        }

        private void RenderBoxBorders()
        {
            foreach (var b in _boxes)
            {
                var rect = new Rectangle
                {
                    Width            = b.Width,
                    Height           = b.Height,
                    Fill             = Brushes.Transparent,
                    Stroke           = new SolidColorBrush(Color.FromArgb(60, 150, 150, 150)),
                    StrokeThickness  = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, b.X);
                Canvas.SetTop(rect, b.Y);
                Panel.SetZIndex(rect, 1);
                LayoutCanvas.Children.Add(rect);
            }
        }

        // MainView 개별 오버레이
        private void RenderBoxOverlay(int i, bool selected)
        {
            var b = _boxes[i];
            var c = new Canvas { Width = b.Width, Height = b.Height };
            Canvas.SetLeft(c, b.X);
            Canvas.SetTop(c, b.Y);
            Panel.SetZIndex(c, 2);

            var border = new Border
            {
                Width           = b.Width,
                Height          = b.Height,
                Background      = Brushes.Transparent,
                BorderBrush     = selected
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 200, 50))
                    : new SolidColorBrush(Color.FromArgb(120, 80, 160, 255)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Cursor          = Cursors.SizeAll,
                Tag             = new BoxTag(i, ResizeDir.Move)
            };
            border.MouseLeftButtonDown += OnElementMouseDown;
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            c.Children.Add(border);

            var hc = selected ? Color.FromArgb(240, 255, 200, 50) : Color.FromArgb(200, 80, 160, 255);
            AddHandle(c, i, 0,                   0,                   ResizeDir.NW, Cursors.SizeNWSE, hc);
            AddHandle(c, i, b.Width - HandleSize, 0,                   ResizeDir.NE, Cursors.SizeNESW, hc);
            AddHandle(c, i, 0,                   b.Height - HandleSize, ResizeDir.SW, Cursors.SizeNESW, hc);
            AddHandle(c, i, b.Width - HandleSize, b.Height - HandleSize, ResizeDir.SE, Cursors.SizeNWSE, hc);
            LayoutCanvas.Children.Add(c);
        }

        // SubView 그룹 오버레이 (5개를 하나의 박스로 묶어서 핸들 표시)
        private void RenderGroupOverlay(bool selected)
        {
            var g   = GroupRect();  // 5개를 감싸는 bounding rect
            var hc  = selected ? Color.FromArgb(240, 255, 200, 50) : Color.FromArgb(200, 80, 160, 255);
            var bc  = selected
                ? new SolidColorBrush(Color.FromArgb(255, 255, 200, 50))
                : new SolidColorBrush(Color.FromArgb(120, 80, 160, 255));

            var c = new Canvas { Width = g.Width, Height = g.Height };
            Canvas.SetLeft(c, g.X);
            Canvas.SetTop(c, g.Y);
            Panel.SetZIndex(c, 2);

            // 그룹 테두리 (이동용)
            var border = new Border
            {
                Width           = g.Width,
                Height          = g.Height,
                Background      = Brushes.Transparent,
                BorderBrush     = bc,
                BorderThickness = new Thickness(selected ? 2 : 1),
                Cursor          = Cursors.SizeAll,
                Tag             = new BoxTag(1, ResizeDir.Move)   // 대표 index=1
            };
            border.MouseLeftButtonDown += OnElementMouseDown;
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            c.Children.Add(border);

            // 코너 핸들 (그룹 전체 기준)
            AddHandle(c, 1, 0,                   0,                   ResizeDir.NW, Cursors.SizeNWSE, hc);
            AddHandle(c, 1, g.Width - HandleSize, 0,                   ResizeDir.NE, Cursors.SizeNESW, hc);
            AddHandle(c, 1, 0,                   g.Height - HandleSize, ResizeDir.SW, Cursors.SizeNESW, hc);
            AddHandle(c, 1, g.Width - HandleSize, g.Height - HandleSize, ResizeDir.SE, Cursors.SizeNWSE, hc);
            LayoutCanvas.Children.Add(c);
        }

        private void AddHandle(Canvas c, int i, double x, double y, ResizeDir dir, Cursor cursor, Color color)
        {
            var h = new Rectangle
            {
                Width  = HandleSize,
                Height = HandleSize,
                Fill   = new SolidColorBrush(color),
                Cursor = cursor,
                Tag    = new BoxTag(i, dir)
            };
            h.MouseLeftButtonDown += OnElementMouseDown;
            Canvas.SetLeft(h, x);
            Canvas.SetTop(h, y);
            c.Children.Add(h);
        }

        // SubView 그룹 전체를 감싸는 Rect
        private Rect GroupRect() => new(
            _boxes[1].X, _boxes[1].Y,
            _boxes[1].Width,
            _boxes.Skip(1).Sum(b => b.Height));

        // ── 마우스 이벤트 ────────────────────────────────────────

        private void OnElementMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_editMode) return;
            var tag    = (BoxTag)((FrameworkElement)sender).Tag;
            _dragIndex = tag.Index;
            _dragDir   = tag.Dir;
            _dragStart = e.GetPosition(LayoutCanvas);

            if (IsSubView(_dragIndex))
            {
                _dragGroupOrig = _boxes.Skip(1).ToList();
                _dragOrigRect  = GroupRect();
                _selectedIndex = 1;
            }
            else
            {
                _dragOrigRect  = _boxes[_dragIndex];
                _selectedIndex = _dragIndex;
            }

            RefreshEditPanel();
            EditPanel.Visibility = Visibility.Visible;
            RenderAll();
            LayoutCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!_editMode || _dragIndex < 0) return;

            var pos = e.GetPosition(LayoutCanvas);
            var dx  = pos.X - _dragStart.X;
            var dy  = pos.Y - _dragStart.Y;

            if (IsSubView(_dragIndex))
                ApplyGroupTransform(dx, dy, _dragDir);
            else
                ApplySingleTransform(dx, dy, _dragDir);

            RefreshEditPanel();
            RenderAll();
        }

        private void ApplySingleTransform(double dx, double dy, ResizeDir dir)
        {
            var r = _dragOrigRect;
            _boxes[_dragIndex] = dir switch
            {
                ResizeDir.Move => new Rect(r.X + dx, r.Y + dy, r.Width, r.Height),
                ResizeDir.SE   => RectAR(r.X,      r.Y,       Math.Max(MinSize, r.Width + dx)),
                ResizeDir.SW   => RectAR_R(r.Right, r.Y,       Math.Max(MinSize, r.Width - dx)),
                ResizeDir.NE   => RectAR_B(r.X,     r.Bottom,  Math.Max(MinSize, r.Width + dx)),
                ResizeDir.NW   => RectAR_RB(r.Right, r.Bottom, Math.Max(MinSize, r.Width - dx)),
                _              => _boxes[_dragIndex]
            };
        }

        private void ApplyGroupTransform(double dx, double dy, ResizeDir dir)
        {
            var g      = _dragGroupOrig;
            double origW  = g[0].Width;
            double origX  = g[0].X;
            double origY  = g[0].Y;
            double origR  = origX + origW;
            double origB  = g[4].Bottom;

            switch (dir)
            {
                case ResizeDir.Move:
                    for (int i = 0; i < 5; i++)
                        _boxes[i + 1] = new Rect(g[i].X + dx, g[i].Y + dy, g[i].Width, g[i].Height);
                    break;
                case ResizeDir.SE:
                    { var w = Math.Max(MinSize, origW + dx); var h = w / AspectRatio;
                      for (int i = 0; i < 5; i++) _boxes[i + 1] = new Rect(origX, origY + i * h, w, h); }
                    break;
                case ResizeDir.SW:
                    { var w = Math.Max(MinSize, origW - dx); var h = w / AspectRatio;
                      for (int i = 0; i < 5; i++) _boxes[i + 1] = new Rect(origR - w, origY + i * h, w, h); }
                    break;
                case ResizeDir.NE:
                    { var w = Math.Max(MinSize, origW + dx); var h = w / AspectRatio;
                      for (int i = 0; i < 5; i++) _boxes[i + 1] = new Rect(origX, origB - (5 - i) * h, w, h); }
                    break;
                case ResizeDir.NW:
                    { var w = Math.Max(MinSize, origW - dx); var h = w / AspectRatio;
                      for (int i = 0; i < 5; i++) _boxes[i + 1] = new Rect(origR - w, origB - (5 - i) * h, w, h); }
                    break;
            }
        }

        private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            LayoutCanvas.ReleaseMouseCapture();
            if (_dragIndex >= 0) SaveLayout();
            _dragIndex = -1;
        }

        // 16:9 Rect 헬퍼
        private static Rect RectAR(double l, double t, double w)     => new(l, t, w, w / AspectRatio);
        private static Rect RectAR_R(double r, double t, double w)   => new(r - w, t, w, w / AspectRatio);
        private static Rect RectAR_B(double l, double b, double w)   { var h = w / AspectRatio; return new(l, b - h, w, h); }
        private static Rect RectAR_RB(double r, double b, double w)  { var h = w / AspectRatio; return new(r - w, b - h, w, h); }

        // ── 편집 패널 ────────────────────────────────────────────

        private void RefreshEditPanel()
        {
            if (_selectedIndex < 0) return;
            Rect b = IsSubView(_selectedIndex) ? GroupRect() : _boxes[_selectedIndex];
            EditPanelTitle.Text = IsSubView(_selectedIndex) ? BoxNames[1] : BoxNames[0];
            TbX.Text = ((int)Math.Round(b.X)).ToString();
            TbY.Text = ((int)Math.Round(b.Y)).ToString();
            TbW.Text = ((int)Math.Round(IsSubView(_selectedIndex) ? _boxes[1].Width : b.Width)).ToString();
        }

        private void TbX_TextChanged(object sender, TextChangedEventArgs e) { }
        private void TbY_TextChanged(object sender, TextChangedEventArgs e) { }
        private void TbW_TextChanged(object sender, TextChangedEventArgs e) { }

        private void ApplyEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedIndex < 0) return;
            if (!double.TryParse(TbX.Text, out var x)) return;
            if (!double.TryParse(TbY.Text, out var y)) return;
            if (!double.TryParse(TbW.Text, out var w) || w < MinSize) return;

            if (IsSubView(_selectedIndex))
            {
                var h = w / AspectRatio;
                for (int i = 0; i < 5; i++)
                    _boxes[i + 1] = new Rect(x, y + i * h, w, h);
            }
            else
            {
                _boxes[_selectedIndex] = RectAR(x, y, w);
            }

            RefreshEditPanel();
            RenderAll();
            SaveLayout();
        }

        // ── 네비바 버튼 ──────────────────────────────────────────

        private void EditToggle_Checked(object sender, RoutedEventArgs e)
        {
            _editMode = true;
            RenderAll();
        }

        private void EditToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _editMode      = false;
            _selectedIndex = -1;
            EditPanel.Visibility = Visibility.Collapsed;
            RenderAll();
        }

        private void NavBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) =>
            Close();
    }

    file record BoxTag(int Index, MainWindow.ResizeDir Dir);
    file record RectData(double X, double Y, double W, string Label = "");
    file record LaunchConfig(string ExePath = "", string Arg = "");
}
