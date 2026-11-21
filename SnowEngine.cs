using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SnowOverlay
{
    public sealed class Flake
    {
        public double X, Y, BaseX, R, Vy, Vx, Phase, Freq, Amp, Life;
        public int Brush;
        public bool Debris;      // осыпавшийся / брызги: падает с ускорением, без качания
        public bool NoCollide;   // пролетает поверх окон (боке, брызги)
    }

    /// <summary>Верхняя грань окна (или панели задач) со снежным профилем по колонкам.</summary>
    public sealed class Surface
    {
        public IntPtr Handle;
        public bool IsGround, Occluder;
        public double L, T, R, B, MaxH;
        public int N;
        public float[] H, Cap;
        public bool[] Visible;

        public Surface(IntPtr handle, double l, double t, double r, double b, bool ground, double maxH, bool occluder = false)
        {
            Handle = handle; L = l; T = t; R = r; B = b; IsGround = ground; MaxH = maxH; Occluder = occluder;
            if (occluder)
            {
                N = 0; H = new float[0]; Cap = H; Visible = new bool[0];
                return;
            }
            Alloc();
        }

        private void Alloc()
        {
            N = Math.Max(2, (int)Math.Ceiling((R - L) / SnowEngine.ColW));
            H = new float[N]; Cap = new float[N]; Visible = new bool[N];
            for (int i = 0; i < N; i++)
            {
                Visible[i] = true;
                if (IsGround)
                    Cap[i] = (float)(MaxH * (0.75 + 0.25 * Math.Sin(i * 0.021) * Math.Sin(i * 0.0071 + 1)));
                else
                {
                    // скруглённые края: сугроб плавно сходит на нет у углов окна
                    double d = Math.Min(i, N - 1 - i) * SnowEngine.ColW;
                    double f = Math.Min(1.0, d / (MaxH * 3));
                    Cap[i] = (float)(MaxH * Math.Sin(f * Math.PI / 2));
                }
            }
        }

        /// <summary>Изменение ширины окна: снег сохраняется по абсолютным координатам (левый край неподвижен).</summary>
        public void Resize(double newL, double newR)
        {
            float[] oldH = H; double oldL = L; int oldN = N;
            L = newL; R = newR;
            Alloc();
            for (int j = 0; j < N; j++)
            {
                double x = L + (j + 0.5) * SnowEngine.ColW;
                int k = (int)Math.Floor((x - oldL) / SnowEngine.ColW);
                if (k >= 0 && k < oldN) H[j] = Math.Min(oldH[k], Cap[j]);
            }
        }
    }

    /// <summary>Физика + отрисовка. Всё рисуется одним DrawingVisual (векторные круги и полигоны).</summary>
    public sealed class SnowEngine : FrameworkElement
    {
        // ======== НАСТРОЙКИ ========
        public const double ColW = 4.0;              // ширина колонки профиля сугроба, px
        public int Target = 240;                     // число снежинок на экране (в Ультра x1.5)
        public double WindowDriftMax = 18;           // макс. высота сугроба на окне, px
        public double GroundDriftMax = 34;           // макс. высота на панели задач, px
        private const double MeltOn = 0.04;          // таяние при включённом снеге, px/сек
        private const double MeltOff = 9.0;          // таяние после выключения, px/сек
        // ============================

        public bool Enabled = true;
        public bool Ultra;                           // 60 FPS + эффекты
        public bool Paused;                          // полноэкранное приложение — ничего не рисуем
        public bool HasContent { get { return _flakes.Count > 0 || _hasDrift; } }

        private double Fps { get { return Ultra ? 60.0 : 30.0; } }

        private readonly DrawingVisual _visual = new DrawingVisual();
        private readonly List<Flake> _flakes = new List<Flake>(1024);
        private readonly Random _rnd = new Random();
        private readonly Brush[] _brushes = new Brush[6];
        private readonly Brush _driftBrush, _halo, _glint;
        private readonly Pen _glintPen;
        private readonly List<Point> _pts = new List<Point>(1024);
        private List<Surface> _surfaces = new List<Surface>();
        private Surface _ground;
        private double _w, _h, _time, _last, _spawnAcc;
        private int _normal;
        private bool _hasDrift, _cleared, _started;

        public SnowEngine()
        {
            AddVisualChild(_visual);
            for (int i = 0; i < _brushes.Length; i++)
            {
                var b = new SolidColorBrush(Color.FromArgb((byte)(90 + i * 33), 255, 255, 255));
                b.Freeze();
                _brushes[i] = b;
            }
            var g = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(214, 228, 246), 90);
            g.Freeze();
            _driftBrush = g;

            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            halo.Freeze();
            _halo = halo;

            var glint = new RadialGradientBrush();
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(210, 150, 215, 255), 0));
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(0, 150, 215, 255), 1));
            glint.Freeze();
            _glint = glint;

            var pb = new SolidColorBrush(Color.FromArgb(235, 170, 225, 255));
            pb.Freeze();
            _glintPen = new Pen(pb, 1.0);
            _glintPen.Freeze();
        }

        protected override int VisualChildrenCount { get { return 1; } }
        protected override Visual GetVisualChild(int index) { return _visual; }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            _w = info.NewSize.Width; _h = info.NewSize.Height;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            CompositionTarget.Rendering += OnRender;
        }

        // ---------- поверхности ----------
        public void UpdateSurfaces(List<WinInfo> wins, double groundY)
        {
            if (_w <= 0) return;

            bool changed = wins.Count != _surfaces.Count;
            var old = new Dictionary<IntPtr, Surface>(_surfaces.Count);
            foreach (var s0 in _surfaces) old[s0.Handle] = s0;

            var list = new List<Surface>(wins.Count);
            for (int idx = 0; idx < wins.Count; idx++)
            {
                WinInfo w = wins[idx];
                Surface s;
                bool had = old.TryGetValue(w.Handle, out s);
                if (had) old.Remove(w.Handle);
                if (!changed && _surfaces[idx].Handle != w.Handle) changed = true;   // изменился порядок Z

                if (w.Occluder)
                {
                    if (had && !s.Occluder) { Collapse(s); changed = true; }
                    else if (!had || Math.Abs(s.L - w.L) >= 1 || Math.Abs(s.T - w.T) >= 1 ||
                             Math.Abs(s.R - w.R) >= 1 || Math.Abs(s.B - w.B) >= 1) changed = true;
                    list.Add(new Surface(w.Handle, w.L, w.T, w.R, w.B, false, 0, true));
                    continue;
                }

                if (had && !s.Occluder)
                {
                    bool sameT = Math.Abs(s.T - w.T) < 1;
                    bool sameL = Math.Abs(s.L - w.L) < 1;
                    bool sameR = Math.Abs(s.R - w.R) < 1;
                    if (sameT && sameL && sameR)
                    {
                        if (Math.Abs(s.B - w.B) >= 1) changed = true;
                        s.B = w.B;
                        list.Add(s);
                        continue;
                    }
                    changed = true;
                    if (sameT && Math.Abs((s.R - s.L) - (w.R - w.L)) >= 1)
                    {
                        // изменили ширину: снег остаётся на месте, меняется только обрезка
                        s.Resize(w.L, w.R);
                        s.B = w.B;
                        list.Add(s);
                        continue;
                    }
                    Collapse(s);   // окно действительно сдвинули — снег осыпается
                }
                else changed = true;   // новое окно

                list.Add(new Surface(w.Handle, w.L, w.T, w.R, w.B, false, WindowDriftMax));
            }

            foreach (var s in old.Values)   // окно закрыто / свёрнуто
            {
                if (!s.Occluder) Collapse(s);
                changed = true;
            }
            _surfaces = list;

            if (_ground == null || Math.Abs(_ground.T - groundY) > 0.5 || Math.Abs(_ground.R - _w) > 0.5)
            {
                if (_ground != null) Collapse(_ground);
                _ground = new Surface(IntPtr.Zero, 0, groundY, _w, _h, true, GroundDriftMax);
            }

            if (!changed) return;

            // какие участки верхней грани перекрыты окнами выше по Z
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Occluder) continue;
                for (int c = 0; c < s.N; c++)
                {
                    double x = s.L + (c + 0.5) * ColW;
                    bool vis = true;
                    for (int j = 0; j < i; j++)
                    {
                        var o = list[j];
                        if (x >= o.L && x <= o.R && o.T <= s.T && o.B > s.T) { vis = false; break; }
                    }
                    s.Visible[c] = vis;
                }
            }
        }

        private void Collapse(Surface s)
        {
            if (s.Occluder) return;
            int step = s.IsGround ? 3 : 2;
            for (int i = 0; i < s.N; i += step)
            {
                if (s.H[i] < 1.0f || _flakes.Count > 1500) continue;
                _flakes.Add(new Flake
                {
                    X = s.L + (i + 0.5) * ColW,
                    Y = s.T - s.H[i] * _rnd.NextDouble(),
                    R = 1 + _rnd.NextDouble() * 1.5,
                    Vy = 15 + _rnd.NextDouble() * 40,
                    Vx = (_rnd.NextDouble() - 0.5) * 24,
                    Debris = true,
                    Brush = 3 + _rnd.Next(3)
                });
            }
            for (int i = 0; i < s.N; i++) s.H[i] = 0;
        }

        // ---------- шаг симуляции ----------
        private void OnRender(object sender, EventArgs e)
        {
            double now = ((RenderingEventArgs)e).RenderingTime.TotalSeconds;
            double dt = now - _last;
            if (dt < 0.9 / Fps) return;
            _last = now;
            if (dt > 0.1) dt = 0.1;
            if (_w <= 0) return;

            bool idle = Paused || (!Enabled && _flakes.Count == 0 && !_hasDrift);
            if (idle)
            {
                if (!_cleared) { using (_visual.RenderOpen()) { } _cleared = true; }
                return;   // полный простой: 0% CPU
            }
            _cleared = false;
            Step(dt);
            Draw();
        }

        private void Step(double dt)
        {
            _time += dt;
            double wind = 14 * Math.Sin(_time * 0.17) + 5 * Math.Sin(_time * 0.51);
            if (Ultra) wind += 30 * Math.Max(0, Math.Sin(_time * 0.23) * Math.Sin(_time * 0.11 + 1.0));   // порывы

            int target = Ultra ? Target * 3 / 2 : Target;
            if (Enabled)
            {
                _spawnAcc += (Ultra ? 60 : 35) * dt;
                while (_spawnAcc >= 1) { _spawnAcc -= 1; if (_normal < target) Spawn(); }
            }
            else _spawnAcc = 0;

            int normal = 0;
            for (int i = _flakes.Count - 1; i >= 0; i--)
            {
                Flake f = _flakes[i];
                double y0 = f.Y;
                bool kill = false;
                if (f.Debris)
                {
                    f.Vy += 380 * dt;
                    f.Y += f.Vy * dt;
                    f.X += f.Vx * dt;
                    if (f.Life > 0) { f.Life -= dt; if (f.Life <= 0) kill = true; }
                }
                else
                {
                    f.Y += f.Vy * dt;
                    f.Phase += f.Freq * dt;
                    f.BaseX += wind * dt;
                    f.X = f.BaseX + Math.Sin(f.Phase) * f.Amp;   // покачивание по синусоиде
                }

                if (!kill) kill = f.Y > _h + 10 || f.X < -60 || f.X > _w + 60 || (!f.NoCollide && Collide(f, y0));
                if (kill)
                {
                    int last = _flakes.Count - 1;
                    _flakes[i] = _flakes[last];
                    _flakes.RemoveAt(last);
                }
                else if (!f.Debris) normal++;
            }
            _normal = normal;

            bool any = false;
            foreach (var s in _surfaces) any |= UpdateHeights(s, dt);
            if (_ground != null) any |= UpdateHeights(_ground, dt);
            _hasDrift = any;
        }

        private void Spawn()
        {
            if (Ultra && _rnd.NextDouble() < 0.05)
            {
                // боке: крупная размытая снежинка на переднем плане, пролетает поверх окон
                _flakes.Add(new Flake
                {
                    R = 3.5 + _rnd.NextDouble() * 1.5,
                    BaseX = -40 + _rnd.NextDouble() * (_w + 80),
                    Y = -20 - _rnd.NextDouble() * 40,
                    Vy = 110 + _rnd.NextDouble() * 40,
                    Phase = _rnd.NextDouble() * Math.PI * 2,
                    Freq = 0.4 + _rnd.NextDouble() * 0.5,
                    Amp = 20 + _rnd.NextDouble() * 20,
                    Brush = 0,
                    NoCollide = true
                });
                return;
            }

            double r = 1 + _rnd.NextDouble() * 2;   // радиус 1..3 => диаметр 2..6 px
            int bi = (int)(((r - 1) / 2.0 * 0.5 + _rnd.NextDouble() * 0.5) * 5.999);
            _flakes.Add(new Flake
            {
                R = r,
                BaseX = -40 + _rnd.NextDouble() * (_w + 80),
                Y = -r * 2 - _rnd.NextDouble() * 40,
                Vy = 18 + r * 14 + _rnd.NextDouble() * 14,
                Phase = _rnd.NextDouble() * Math.PI * 2,
                Freq = 0.5 + _rnd.NextDouble() * 1.1,
                Amp = 6 + _rnd.NextDouble() * 18,
                Brush = bi
            });
        }

        private void Splash(double x, double y)
        {
            if (_flakes.Count > 1800) return;
            for (int i = 0; i < 2; i++)
            {
                _flakes.Add(new Flake
                {
                    X = x, Y = y - 1,
                    R = 0.7 + _rnd.NextDouble() * 0.6,
                    Vx = (_rnd.NextDouble() - 0.5) * 50,
                    Vy = -(25 + _rnd.NextDouble() * 35),
                    Debris = true, NoCollide = true,
                    Life = 0.35 + _rnd.NextDouble() * 0.2,
                    Brush = 2 + _rnd.Next(3)
                });
            }
        }

        // true => снежинку нужно удалить (села на сугроб / оказалась внутри окна)
        private bool Collide(Flake f, double y0)
        {
            double x = f.X;
            for (int k = 0; k <= _surfaces.Count; k++)
            {
                Surface s = k < _surfaces.Count ? _surfaces[k] : _ground;
                if (s == null || s.Occluder || x < s.L || x > s.R) continue;
                if (f.Y + f.R < s.T - s.MaxH - 2) continue;       // ещё высоко
                if (y0 >= s.T + 4)
                {
                    if (y0 < s.B) return true;                    // оказалась «внутри» окна — убираем
                    continue;
                }
                int c = (int)((x - s.L) / ColW);
                if (c < 0) c = 0; else if (c >= s.N) c = s.N - 1;
                if (!s.Visible[c]) continue;
                if (f.Y + f.R >= s.T - s.H[c])
                {
                    Deposit(s, c, f.R);
                    if (Ultra) Splash(x, s.T - s.H[c]);
                    return true;
                }
            }
            return false;
        }

        private static readonly float[] Kernel = { 0.08f, 0.22f, 0.40f, 0.22f, 0.08f };

        private static void Deposit(Surface s, int c, double r)
        {
            float amt = (float)(r * 7.0);
            for (int k = -2; k <= 2; k++)
            {
                int i = c + k;
                if (i < 0 || i >= s.N) continue;
                float v = s.H[i] + amt * Kernel[k + 2];
                if (v > s.Cap[i]) v = s.Cap[i];      // предел высоты: лишний снег просто тает
                s.H[i] = v;
            }
        }

        private bool UpdateHeights(Surface s, double dt)
        {
            if (s.Occluder) return false;
            float melt = (float)((Enabled ? MeltOn : MeltOff) * dt);
            bool any = false;
            float[] H = s.H;
            for (int i = 0; i < s.N; i++)
            {
                if (H[i] <= 0) continue;
                H[i] -= melt;
                if (H[i] < 0.01f) H[i] = 0; else any = true;
            }
            if (!any) return false;

            // «осыпание» крутых склонов — получаются плавные холмики
            const float ms = 1.4f;
            for (int i = 0; i < s.N - 1; i++)
            {
                float d = H[i] - H[i + 1];
                if (d > ms) { float m = (d - ms) * 0.5f; H[i] -= m; H[i + 1] += m; }
                else if (d < -ms) { float m = (-d - ms) * 0.5f; H[i] += m; H[i + 1] -= m; }
            }
            for (int i = 0; i < s.N; i++) if (H[i] > s.Cap[i]) H[i] = s.Cap[i];
            return true;
        }

        // ---------- отрисовка ----------
        private void Draw()
        {
            using (DrawingContext dc = _visual.RenderOpen())
            {
                foreach (var s in _surfaces) DrawDrift(dc, s);
                if (_ground != null) DrawDrift(dc, _ground);

                for (int i = 0; i < _flakes.Count; i++)
                {
                    Flake f = _flakes[i];
                    var p = new Point(f.X, f.Y);
                    if (Ultra && !f.Debris && f.R >= 2.0)
                        dc.DrawEllipse(_halo, null, p, f.R * 3, f.R * 3);   // мягкое свечение
                    dc.DrawEllipse(_brushes[f.Brush], null, p, f.R, f.R);
                }
            }
        }

        private void DrawDrift(DrawingContext dc, Surface s)
        {
            if (s.Occluder) return;
            int n = s.N, i = 0;
            double baseY = s.T + 2;
            while (i < n)
            {
                while (i < n && (!s.Visible[i] || s.H[i] < 0.4f)) i++;
                if (i >= n) break;
                int start = i;
                while (i < n && s.Visible[i] && s.H[i] >= 0.4f) i++;
                int end = i - 1;
                if (end - start < 1) continue;

                _pts.Clear();
                for (int k = start; k <= end; k++)
                    _pts.Add(new Point(s.L + (k + 0.5) * ColW, s.T - s.H[k]));
                _pts.Add(new Point(s.L + (end + 1) * ColW, baseY));

                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(s.L + start * ColW, baseY), true, true);
                    ctx.PolyLineTo(_pts, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(_driftBrush, null, g);

                if (Ultra)
                {
                    // ледяные блики на кромке сугроба
                    for (int k = start; k <= end; k += 5)
                    {
                        double ph = Math.Sin(_time * 2.2 + k * 2.399 + s.L * 0.37);
                        if (ph <= 0.93) continue;
                        double a = (ph - 0.93) / 0.07;
                        var p = new Point(s.L + (k + 0.5) * ColW, s.T - s.H[k] - 0.5);
                        double len = 2 + 3 * a;
                        dc.DrawEllipse(_glint, null, p, 4 + 4 * a, 4 + 4 * a);
                        dc.DrawLine(_glintPen, new Point(p.X - len, p.Y), new Point(p.X + len, p.Y));
                        dc.DrawLine(_glintPen, new Point(p.X, p.Y - len), new Point(p.X, p.Y + len));
                    }
                }
            }
        }
    }
}
