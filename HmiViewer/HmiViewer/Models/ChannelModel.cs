using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HmiViewer.Models;

public class ChannelModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public int    Index      { get; }
    public string Name       { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ChannelModel(int index)
    {
        Index = index;
        Name  = $"CH {index:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
