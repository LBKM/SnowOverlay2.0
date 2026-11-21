using System;
using System.Windows;
using System.Windows.Interop;

namespace SnowOverlay
{
    public partial class App : Application
    {
        private MainWindow _overlay;
        private TrayWidget _widget;
        private System.Windows.Forms.NotifyIcon _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _overlay = new MainWindow();
            _overlay.Show();

            _widget = new TrayWidget();
            _widget.ToggleChanged += on => _overlay.Snow.Enabled = on;
            _widget.ExitRequested += () => Shutdown();
            _widget.QualityChanged += ultra => _overlay.SetQuality(ultra);
            IntPtr wh = new WindowInteropHelper(_widget).EnsureHandle();
            _overlay.Tracker.Exclude(wh);   // на виджете снег не копится

            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = CreateIcon(),
                Text = "Snow",
                Visible = true
            };
            _tray.MouseUp += (s, args) =>
            {
                if (args.Button == System.Windows.Forms.MouseButtons.Left) ToggleWidget();
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Выход", null, (s, args) => Shutdown());
            _tray.ContextMenuStrip = menu;
        }

        private void ToggleWidget()
        {
            if (_widget.IsVisible) { _widget.HideWidget(); return; }
            // клик по иконке сам деактивирует виджет — не открываем заново сразу
            if ((DateTime.UtcNow - _widget.LastHidden).TotalMilliseconds < 250) return;
            _widget.ShowAtTray();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnExit(e);
        }

        private static System.Drawing.Icon CreateIcon()
        {
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 76, 194, 255), 2.4f))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        float c = 16f;
                        for (int i = 0; i < 6; i++)
                        {
                            double a = i * Math.PI / 3;
                            g.DrawLine(pen, c, c, c + (float)Math.Cos(a) * 13f, c + (float)Math.Sin(a) * 13f);
                            float bx = c + (float)Math.Cos(a) * 8f, by = c + (float)Math.Sin(a) * 8f;
                            for (int s = -1; s <= 1; s += 2)
                            {
                                double b = a + s * Math.PI / 4;
                                g.DrawLine(pen, bx, by, bx + (float)Math.Cos(b) * 4.5f, by + (float)Math.Sin(b) * 4.5f);
                            }
                        }
                    }
                }
                IntPtr h = bmp.GetHicon();
                var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }
    }
}
