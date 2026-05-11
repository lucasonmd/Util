using System.Windows;
using System.Windows.Controls;

namespace HmiViewer.Controls;

public partial class VideoSlotControl : UserControl
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(VideoSlotControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ChannelLabelProperty =
        DependencyProperty.Register(nameof(ChannelLabel), typeof(string), typeof(VideoSlotControl),
            new PropertyMetadata(string.Empty));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public string ChannelLabel
    {
        get => (string)GetValue(ChannelLabelProperty);
        set => SetValue(ChannelLabelProperty, value);
    }

    // 향후 실제 영상 삽입을 위한 확장 지점.
    // D3DImage, MediaElement, 또는 Win32 HWND를 VideoHost에 주입한다.
    public UIElement? VideoContent
    {
        get => VideoHost?.Child;
        set { if (VideoHost != null) VideoHost.Child = value; }
    }

    private System.Windows.Controls.Border? VideoHost =>
        (System.Windows.Controls.Border?)Template?.FindName("VideoHost", this);

    public VideoSlotControl()
    {
        InitializeComponent();
    }
}
