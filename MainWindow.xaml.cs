using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SnowOverlay
{
    public partial class MainWindow : Window
    {
        private readonly WindowTracker _tracker = new WindowTracker();
        private DispatcherTimer _timer;
        private IntPtr _hwnd;
        private int _tick;
        private bool _fullscreen;

        public SnowEngine Snow { get { return Engine; } }
        public WindowTracker Tracker { get { return _tracker; } }

        public MainWindow()
        {
            InitializeComponent();
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
            _tracker.Exclude(_hwnd);
            _tracker.Scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Engine.Start();
            _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// <summary>Обычно: 30 FPS и опрос окон раз в 50 мс. Ультра: 60 FPS, опрос раз в 16 мс + эффекты.</summary>
        public void SetQuality(bool ultra)
        {
            Engine.Ultra = ultra;
            if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(ultra ? 16 : 50);
        }

        private void OnTick(object sender, EventArgs e)
        {
            // Полноэкранное приложение (игра, видео, F11): снег скрыт, ничего не опрашиваем
            bool fs = _tracker.IsFullscreenAppActive();
            if (fs != _fullscreen) { _fullscreen = fs; Engine.Paused = fs; }
            if (fs) return;

            // Когда снег выключен и всё растаяло — окна не опрашиваем (0% CPU)
            if (Engine.Enabled || Engine.HasContent)
            {
                double ground;
                var wins = _tracker.Snapshot(Engine.ActualWidth, Engine.ActualHeight, out ground);
                Engine.UpdateSurfaces(wins, ground);
            }

            // раз в ~2 секунды возвращаем оверлей поверх остальных Topmost-окон
            if (++_tick % 40 == 0)
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }
}
