using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WpfOverlayApp
{
    public partial class MainWindow : Window
    {
        private bool _isClickThrough = false;
        private bool _isPlaying = false;
        private bool _isMuted = false;

        // Win32 API for click-through support
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                Width = 1920;
                Height = 1080;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = !_isPlaying;
            PlayPauseButton.Content = _isPlaying ? "⏸" : "▶";
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            // 이전 트랙/챕터로 이동
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // 다음 트랙/챕터로 이동
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            MuteButton.Content = _isMuted ? "🔈" : "🔇";
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("설정 기능은 추후 구현 예정입니다.", "설정", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("스크린샷 기능은 추후 구현 예정입니다.", "스크린샷", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("북마크 기능은 추후 구현 예정입니다.", "북마크", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Opacity = e.NewValue;
        }

        private void ClickThroughToggle_Checked(object sender, RoutedEventArgs e)
        {
            SetClickThrough(true);
        }

        private void ClickThroughToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SetClickThrough(false);
        }

        private void SetClickThrough(bool enable)
        {
            _isClickThrough = enable;
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enable)
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            else
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
    }
}
