using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TRA.Models;
using WpfPath = System.Windows.Shapes.Path;

namespace TRA
{
    public partial class MainView : UserControl
    {
        // ── 색상 팔레트 ────────────────────────────────────────────
        private static readonly Color PalBright = Color.FromRgb(180, 212, 238);
        private static readonly Color PalMid    = Color.FromRgb( 68, 108, 145);
        private static readonly Color PalDim    = Color.FromRgb( 22,  44,  66);
        private static readonly Color PalDark   = Color.FromRgb(  7,  14,  24);
        private static readonly Color PalZone   = Color.FromRgb(220,  50,  40);

        private static readonly SolidColorBrush ItemNormalBrush   = new(Color.FromRgb( 0,  0,  0));
        private static readonly SolidColorBrush ItemSelectedBrush = new(Color.FromRgb( 4, 12, 22));
        private static readonly SolidColorBrush ItemHoverBrush    = new(Color.FromRgb( 2,  7, 14));

        // ── 언어 ──────────────────────────────────────────────────
        private bool   _isKorean = false;
        private string UiFont    => _isKorean ? "Microsoft Sans Serif" : "Microsoft Sans Serif";

        private static readonly Dictionary<string, (string ko, string en)> Strings = new()
        {
            ["header"] = ("구동 제한 구역 설정", "DRIVE LIMIT ZONE CONFIG"),
            ["zone"]   = ("구역",                "ZONE"),
            ["az"]     = ("방위",                "AZ"),
            ["el"]     = ("고각",                "EL"),
        };

        private string T(string key) => _isKorean ? Strings[key].ko : Strings[key].en;

        // ── 공개 언어 전환 API ────────────────────────────────────
        public void ToggleLanguage()         => SetLanguage(!_isKorean);
        public void SetLanguage(bool korean)
        {
            _isKorean = korean;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            var uf = new FontFamily(UiFont);
            HeaderText.Text = T("header"); HeaderText.FontFamily = uf;
            EditPanelTitle.FontFamily = uf;
            if (_selectedZone != null)
                EditPanelTitle.Text = $"{T("zone")}  {_selectedZone.Id:00}";
            Keypad.ApplyLanguage(_isKorean);
            RefreshZoneList();
            DrawAll();
        }

        // ── 앱 상태 ───────────────────────────────────────────────
        private readonly List<DriveLimitZone>                  _zones        = new();
        private readonly Dictionary<DriveLimitZone, TextBlock> _detailLabels = new();
        private DriveLimitZone? _selectedZone;
        private BitmapImage?    _tankImage;

        // ── 외부 연동 콜백 ────────────────────────────────────────
        // 이름 형식: "zone{id:00}_{field}"  예) "zone01_azmin", "zone03_elmax"
        public Action<string, double>? OnTransmit;

        // ── 초기화 ────────────────────────────────────────────────
        public MainView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                OverlayCanvas.SizeChanged += (_, _) => DrawAll();

                Keypad.ValueCommitted += (field, value) =>
                {
                    if (_selectedZone == null) return;
                    string name = $"zone{_selectedZone.Id:00}_{field}";
                    ApplyValue(name, value);
                    OnTransmit?.Invoke(name, Math.Truncate(value / 0.006));
                };

                InitZones();
                ApplyLanguage();
            };
        }

        private void InitZones()
        {
            for (int i = 0; i < 8; i++)
            {
                _zones.Add(new DriveLimitZone
                {
                    Id           = i + 1,
                    AzimuthMin   = 0, AzimuthMax   = 0,
                    ElevationMin = 0, ElevationMax = 0,
                    Color        = PalBright,
                });
            }
            RefreshZoneList();
            SelectZone(_zones[0]);
        }

        // ── 구역 목록 ─────────────────────────────────────────────
        private void RefreshZoneList()
        {
            _detailLabels.Clear();
            ZoneListPanel.Children.Clear();
            foreach (var zone in _zones)
                ZoneListPanel.Children.Add(BuildZoneItem(zone));
        }

        private Border BuildZoneItem(DriveLimitZone zone)
        {
            bool isSel = zone == _selectedZone;
            var  uf    = new FontFamily(UiFont);

            var idText = new TextBlock
            {
                Text       = $"{T("zone")}  {zone.Id:00}",
                Foreground = new SolidColorBrush(isSel ? Colors.White : PalMid),
                FontFamily = uf,
                FontSize   = 22,
                FontWeight = FontWeights.Bold,
            };

            var detail = new TextBlock
            {
                Text       = FormatDetail(zone),
                Foreground = new SolidColorBrush(isSel ? PalMid : PalDim),
                FontFamily = new FontFamily("Microsoft Sans Serif"),
                FontSize   = 20,
                Margin     = new Thickness(0, 4, 0, 0),
            };
            _detailLabels[zone] = detail;

            var stack = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            stack.Children.Add(idText);
            stack.Children.Add(detail);

            var inner = new Border
            {
                Background      = isSel ? ItemSelectedBrush : ItemNormalBrush,
                BorderBrush     = new SolidColorBrush(PalDark),
                BorderThickness = new Thickness(isSel ? 2 : 0, 0, 0, 0),
                Child           = stack,
                Tag             = zone,
                Cursor          = Cursors.Hand,
            };

            var wrapper = new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromRgb(5, 10, 18)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child           = inner,
                Tag             = zone,
            };

            wrapper.MouseLeftButtonDown += (s, _) =>
            {
                if ((s as Border)?.Tag is DriveLimitZone z) SelectZone(z);
            };
            wrapper.MouseEnter += (s, _) =>
            {
                if ((s as Border) is Border wb && wb.Tag is DriveLimitZone z && z != _selectedZone)
                    inner.Background = ItemHoverBrush;
            };
            wrapper.MouseLeave += (s, _) =>
            {
                if ((s as Border) is Border wb && wb.Tag is DriveLimitZone z && z != _selectedZone)
                    inner.Background = ItemNormalBrush;
            };

            return wrapper;
        }

        // ── 구역 선택 ─────────────────────────────────────────────
        private void SelectZone(DriveLimitZone zone)
        {
            _selectedZone = zone;

            RefreshZoneList();
            EditPanelTitle.Text       = $"{T("zone")}  {zone.Id:00}";
            EditPanelTitle.FontFamily = new FontFamily(UiFont);
            Keypad.SetFieldValues(zone.AzimuthMin, zone.AzimuthMax, zone.ElevationMin, zone.ElevationMax);
            Keypad.Reset();

            DrawAll();
        }

        // ── 외부 연동 ─────────────────────────────────────────────

        // 공통 값 적용 + 화면 갱신 (로컬 및 ReceiveValue 양쪽에서 사용)
        private void ApplyValue(string name, double value)
        {
            int sep = name.IndexOf('_');
            if (sep < 5 || !int.TryParse(name[4..sep], out int id)) return;
            var zone = _zones.Find(z => z.Id == id);
            if (zone == null) return;

            switch (name[(sep + 1)..])
            {
                case "azmin": zone.AzimuthMin   = value; break;
                case "azmax": zone.AzimuthMax   = value; break;
                case "elmin": zone.ElevationMin = value; break;
                case "elmax": zone.ElevationMax = value; break;
                default: return;
            }

            if (_detailLabels.TryGetValue(zone, out var tb))
                tb.Text = FormatDetail(zone);
            if (zone == _selectedZone)
                Keypad.SetFieldValues(zone.AzimuthMin, zone.AzimuthMax, zone.ElevationMin, zone.ElevationMax);
            DrawAll();
        }

        // 외부에서 값을 받아 화면에 반영 (string name, double value)
        public void ReceiveValue(string name, double value) =>
            Dispatcher.Invoke(() => ApplyValue(name, Math.Truncate(value * 0.006)));

        private string FormatDetail(DriveLimitZone z) =>
            $"{T("az")} {AzStr(z.AzimuthMin)}~{AzStr(z.AzimuthMax)}  {T("el")} {ElStr(z.ElevationMin)}~{ElStr(z.ElevationMax)}";

        private static string AzStr(double v) =>
            v >= 0 ? $"+{(int)v:0000}" : $"-{(int)Math.Abs(v):0000}";

        private static string ElStr(double v) =>
            v >= 0 ? $"+{(int)v:00}" : $"-{(int)Math.Abs(v):00}";

        // ══════════════════════════════════════════════════════════
        //  드로잉  (방위각 내부 단위: MIL → 그리기 전 도로 변환)
        // ══════════════════════════════════════════════════════════

        private void DrawAll()
        {
            OverlayCanvas.Children.Clear();
            double cx = OverlayCanvas.ActualWidth  / 2;
            double cy = OverlayCanvas.ActualHeight / 2;
            if (cx < 10 || cy < 10) return;

            double minDim = Math.Min(cx, cy);
            double tankS  = minDim * 0.46;
            double innerR = minDim * 0.24;
            double zoneR  = minDim * 0.65;
            double ringR  = minDim * 0.84;

            DrawGrid(cx, cy, ringR, zoneR, innerR);
            DrawZones(cx, cy, innerR, zoneR);
            DrawTankImage(cx, cy, tankS);
            DrawCompassRing(cx, cy, ringR);
        }

        // ── 배경 격자 ─────────────────────────────────────────────
        private void DrawGrid(double cx, double cy, double ringR, double zoneR, double innerR)
        {
            double ext = ringR * 1.05;
            AddLine(cx, cy - ext, cx, cy + ext, Color.FromRgb(18, 36, 54), 1);
            AddLine(cx - ext, cy, cx + ext, cy, Color.FromRgb(18, 36, 54), 1);

            double d = ext * 0.707;
            AddLine(cx - d, cy - d, cx + d, cy + d, Color.FromRgb(10, 20, 32), 1);
            AddLine(cx + d, cy - d, cx - d, cy + d, Color.FromRgb(10, 20, 32), 1);

            foreach (var r in new[] { innerR * 1.5, zoneR * 0.70, zoneR, zoneR * 1.30 })
                AddRing(cx, cy, r, Color.FromRgb(18, 36, 54), 0.8, dashed: true);
        }

        // ── 구역 섹터 (방위 = MIL) ────────────────────────────────
        private void DrawZones(double cx, double cy, double innerR, double outerR)
        {
            foreach (var zone in _zones)
            {
                if (zone.AzimuthMin == zone.AzimuthMax) continue;

                bool   isSel    = zone == _selectedZone;
                double sweepMil = NormalizeSweep(zone.AzimuthMin, zone.AzimuthMax, 6400.0);
                double azMinDeg = MilToDeg(zone.AzimuthMin);
                double sweepDeg = MilToDeg(sweepMil);

                if (isSel)
                {
                    var glow = MakeSector(cx, cy, innerR - 4, outerR + 8,
                                          azMinDeg, sweepDeg, PalZone, 18);
                    glow.StrokeThickness = 0;
                    OverlayCanvas.Children.Add(glow);
                }

                byte fill   = isSel ? (byte)80 : (byte)30;
                byte stroke = isSel ? (byte)210 : (byte)100;
                var sec = MakeSector(cx, cy, innerR, outerR, azMinDeg, sweepDeg, PalZone, fill);
                sec.Stroke          = new SolidColorBrush(Color.FromArgb(stroke, PalZone.R, PalZone.G, PalZone.B));
                sec.StrokeThickness = isSel ? 1.5 : 1;
                OverlayCanvas.Children.Add(sec);

                if (sweepDeg >= 8)
                {
                    double midMil = zone.AzimuthMin + sweepMil / 2.0;
                    double midDeg = MilToDeg(midMil);
                    double midR   = (innerR + outerR) / 2;
                    double rad    = ToRad(midDeg - 90);
                    AddText($"{zone.Id:00}",
                        cx + midR * Math.Cos(rad),
                        cy + midR * Math.Sin(rad),
                        isSel ? Colors.White : Color.FromRgb(200, 100, 90),
                        isSel ? 14 : 12,
                        isSel ? FontWeights.Bold : FontWeights.Normal);
                }
            }
        }

        // ── 탱크 이미지 ───────────────────────────────────────────
        private void DrawTankImage(double cx, double cy, double s)
        {
            _tankImage ??= TryLoadTankImage();
            if (_tankImage == null) return;

            double imgW = s * 2.0;
            double imgH = s * 3.0;
            double left = cx - imgW / 2;
            double top  = cy - imgH * 0.34 - 30;

            var img = new Image
            {
                Source  = _tankImage,
                Width   = imgW,
                Height  = imgH,
                Opacity = 0.5,
                Stretch = Stretch.Uniform,
            };
            Canvas.SetLeft(img, left);
            Canvas.SetTop (img, top);
            OverlayCanvas.Children.Add(img);

            AddEllipse(cx, cy, 3.5, Colors.White, Colors.White, 0);
        }

        private static BitmapImage? TryLoadTankImage()
        {
            try { return new BitmapImage(new Uri("pack://application:,,,/Assets/Views/bird.png")); }
            catch { return null; }
        }

        // ── 나침반 링 (MIL 눈금, N/E/S/W 고정) ───────────────────
        private void DrawCompassRing(double cx, double cy, double ringR)
        {
            AddRing(cx, cy, ringR, Color.FromRgb(50, 95, 140), 1.5);

            for (int mil = 0; mil < 6400; mil += 100)
            {
                bool   large = mil % 800 == 0;
                bool   mid   = mil % 400 == 0;
                double len   = large ? 16 : (mid ? 8 : 4);
                double deg   = MilToDeg(mil);
                double rad   = ToRad(deg - 90);
                double cos   = Math.Cos(rad), sin = Math.Sin(rad);
                var    col   = large ? Color.FromRgb(60, 115, 165) : (mid ? Color.FromRgb(30, 62, 92) : Color.FromRgb(18, 38, 58));
                double thk   = large ? 1.6 : (mid ? 1.0 : 0.7);
                AddLine2(cx + (ringR - len) * cos, cy + (ringR - len) * sin,
                         cx +  ringR       * cos,  cy +  ringR       * sin,
                         col, thk);
            }

            for (int mil = 0; mil < 6400; mil += 800)
            {
                bool   isCard = mil % 1600 == 0;
                int    disp   = mil > 3200 ? mil - 6400 : mil;
                string label  = mil switch
                {
                    0    => "N",
                    1600 => "E",
                    3200 => "S",
                    4800 => "W",
                    _    => disp.ToString(),
                };
                double deg = MilToDeg(mil);
                double rad = ToRad(deg - 90);
                double lr  = ringR + (isCard ? 28 : 22);
                AddText(label,
                    cx + lr * Math.Cos(rad),
                    cy + lr * Math.Sin(rad),
                    isCard ? Colors.White : Color.FromRgb(110, 165, 210),
                    isCard ? 15 : 12,
                    isCard ? FontWeights.Bold : FontWeights.Normal,
                    "Microsoft Sans Serif");
            }
        }

        // ══════════════════════════════════════════════════════════
        //  지오메트리 (내부: 도 단위)
        // ══════════════════════════════════════════════════════════

        private static WpfPath MakeSector(double cx, double cy,
                                           double innerR, double outerR,
                                           double azStartDeg, double sweepDeg,
                                           Color color, byte fillAlpha)
        {
            if (sweepDeg >= 360) sweepDeg = 359.9;
            double azEndDeg = azStartDeg + sweepDeg;
            bool   large    = sweepDeg > 180;

            var oSt = AzPt(cx, cy, outerR, azStartDeg);
            var oEn = AzPt(cx, cy, outerR, azEndDeg);
            var iSt = AzPt(cx, cy, innerR, azStartDeg);
            var iEn = AzPt(cx, cy, innerR, azEndDeg);

            var fig = new PathFigure { StartPoint = oSt, IsClosed = true };
            fig.Segments.Add(new ArcSegment(oEn, new Size(outerR, outerR), 0, large, SweepDirection.Clockwise,        true));
            fig.Segments.Add(new LineSegment(iEn, true));
            fig.Segments.Add(new ArcSegment(iSt, new Size(innerR, innerR), 0, large, SweepDirection.Counterclockwise, true));

            var geo = new PathGeometry();
            geo.Figures.Add(fig);

            return new WpfPath
            {
                Data            = geo,
                Fill            = new SolidColorBrush(Color.FromArgb(fillAlpha, color.R, color.G, color.B)),
                Stroke          = Brushes.Transparent,
                StrokeThickness = 1,
            };
        }

        private static Point AzPt(double cx, double cy, double r, double azDeg)
        {
            double rad = ToRad(azDeg - 90);
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        // ══════════════════════════════════════════════════════════
        //  캔버스 헬퍼
        // ══════════════════════════════════════════════════════════

        private void AddLine(double x1, double y1, double x2, double y2,
                              Color color, double thk, bool dashed = false)
        {
            var l = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = new SolidColorBrush(color), StrokeThickness = thk,
            };
            if (dashed) l.StrokeDashArray = new DoubleCollection { 5, 5 };
            OverlayCanvas.Children.Add(l);
        }

        private void AddLine2(double x1, double y1, double x2, double y2, Color color, double thk)
            => AddLine(x1, y1, x2, y2, color, thk);

        private void AddRing(double cx, double cy, double r,
                              Color color, double thk, bool dashed = false)
        {
            var e = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Stroke = new SolidColorBrush(color), StrokeThickness = thk,
                Fill = Brushes.Transparent,
            };
            if (dashed) e.StrokeDashArray = new DoubleCollection { 6, 6 };
            Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r);
            OverlayCanvas.Children.Add(e);
        }

        private void AddEllipse(double cx, double cy, double r,
                                 Color fill, Color stroke, double thk)
        {
            var e = new Ellipse
            {
                Width  = r * 2, Height = r * 2,
                Fill   = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(stroke), StrokeThickness = thk,
            };
            Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r);
            OverlayCanvas.Children.Add(e);
        }

        private void AddText(string text, double x, double y, Color color,
                              double size, FontWeight weight, string family = "Microsoft Sans Serif")
        {
            var tb = new TextBlock
            {
                Text       = text,
                Foreground = new SolidColorBrush(color),
                FontSize   = size,
                FontWeight = weight,
                FontFamily = new FontFamily(family),
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x - tb.DesiredSize.Width  / 2);
            Canvas.SetTop (tb, y - tb.DesiredSize.Height / 2);
            OverlayCanvas.Children.Add(tb);
        }

        // ── 유틸리티 ──────────────────────────────────────────────
        private static double NormalizeSweep(double start, double end, double fullCircle = 360.0)
        {
            double s = end - start;
            return s <= 0 ? s + fullCircle : s;
        }

        private static double ToRad(double deg)    => deg * Math.PI / 180.0;
        private static double MilToDeg(double mil) => mil * 360.0 / 6400.0;
    }
}
