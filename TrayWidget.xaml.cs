using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace SnowOverlay
{
    public partial class TrayWidget : Window
    {
        public event Action<bool> ToggleChanged;
        public event Action<bool> QualityChanged;   // true = Ультра
        public event Action ExitRequested;
        public DateTime LastHidden { get; private set; }

        public TrayWidget()
        {
            InitializeComponent();
            LastHidden = DateTime.MinValue;
            Deactivated += (s, e) => HideWidget();
        }

        public void ShowAtTray()
        {
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 6;
            Top = wa.Bottom - Height - 6;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Show();
            Activate();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        public void HideWidget()
        {
            if (!IsVisible) return;
            LastHidden = DateTime.UtcNow;
            Hide();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            bool on = Toggle.IsChecked == true;
            StatusText.Text = on ? "Включено" : "Выключено";
            if (ToggleChanged != null) ToggleChanged(on);
        }

        private void Quality_Click(object sender, RoutedEventArgs e)
        {
            bool ultra = UltraRadio.IsChecked == true;
            QualityHint.Text = ultra ? "60 FPS, свечение, блики, брызги" : "30 FPS, экономно";
            if (QualityChanged != null) QualityChanged(ultra);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (ExitRequested != null) ExitRequested();
        }
    }
}
