using RuntimeMonitor.Viewer.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace RuntimeMonitor.Viewer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(Dispatcher);
        DataContext = _viewModel;

        // 자동 스크롤: 패킷 추가 시 마지막 항목으로 스크롤
        _viewModel.Packets.CollectionChanged += (_, _) =>
        {
            if (_viewModel.AutoScroll && PacketListView.Items.Count > 0)
            {
                PacketListView.ScrollIntoView(
                    PacketListView.Items[PacketListView.Items.Count - 1]);
            }
        };
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _viewModel.DisposeAsync();
        base.OnClosed(e);
    }
}
