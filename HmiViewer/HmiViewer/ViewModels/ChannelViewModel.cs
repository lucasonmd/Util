using HmiViewer.Models;

namespace HmiViewer.ViewModels;

public class ChannelViewModel : ViewModelBase
{
    private bool _isSelected;

    public ChannelModel Model { get; }

    public int    Index      => Model.Index;
    public string Name       => Model.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // 향후 실제 영상 소스(HWND, D3D surface 등) 연동 확장 지점
    public object? VideoSource { get; set; }

    public ChannelViewModel(ChannelModel model)
    {
        Model = model;
    }
}
