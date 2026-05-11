using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Input;
using HmiViewer.Commands;
using HmiViewer.Models;
using HmiViewer.Services;

namespace HmiViewer.ViewModels;

public class MainViewModel : ViewModelBase
{
    // ─── Virtual Key 상수 (Win32) ────────────────────────────────────────────
    // FUNC1~8 → F1~F8 (VK 0x70~0x77), F1~F20 → 0x70~0x83
    private static readonly int[] FuncVks =
    [
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77  // F1~F8
    ];
    private static readonly int[] SideLeftVks  = [0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D]; // F9~F14 → F1~F6 label
    private static readonly int[] SideRightVks = [0x7E, 0x7F, 0x80, 0x81, 0x82, 0x83]; // F15~F20 → F7~F12 label
    private static readonly int[] BottomVks    =
    [
        0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B  // F13~F20 (확장)
    ];

    // ─── Collections ─────────────────────────────────────────────────────────
    public ObservableCollection<ChannelViewModel>  Channels    { get; } = [];
    public ObservableCollection<HmiButtonModel>    FuncButtons { get; } = [];
    public ObservableCollection<HmiButtonModel>    LeftButtons { get; } = [];
    public ObservableCollection<HmiButtonModel>    RightButtons{ get; } = [];
    public ObservableCollection<HmiButtonModel>    BottomButtons{ get; } = [];

    // ─── State ───────────────────────────────────────────────────────────────
    private ChannelViewModel? _selectedChannel;
    public  ChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (_selectedChannel != null) _selectedChannel.IsSelected = false;
            SetProperty(ref _selectedChannel, value);
            if (_selectedChannel != null) _selectedChannel.IsSelected = true;
            RaisePropertyChanged(nameof(SelectedChannelName));
        }
    }

    public string SelectedChannelName =>
        _selectedChannel is null ? "── NO CHANNEL ──" : _selectedChannel.Name;

    private ConnectionState _connectionState = ConnectionState.Disconnected;
    public  ConnectionState ConnectionState
    {
        get => _connectionState;
        set
        {
            SetProperty(ref _connectionState, value);
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(IsRunning));
        }
    }

    public string StatusText => ConnectionState switch
    {
        ConnectionState.Disconnected => "DISCONNECTED",
        ConnectionState.Ready        => "READY",
        ConnectionState.Running      => "RUNNING",
        _                            => "UNKNOWN"
    };

    public bool IsRunning => ConnectionState == ConnectionState.Running;

    private string _ipAddress = string.Empty;
    public  string IpAddress
    {
        get => _ipAddress;
        set
        {
            SetProperty(ref _ipAddress, value);
            RaisePropertyChanged(nameof(IsIpValid));
        }
    }

    public bool IsIpValid => IPAddress.TryParse(_ipAddress, out _);

    // ─── Commands ────────────────────────────────────────────────────────────
    public ICommand SelectChannelCommand { get; }
    public ICommand RunCommand           { get; }
    public ICommand SendKeyCommand       { get; }

    // ─── Constructor ─────────────────────────────────────────────────────────
    public MainViewModel()
    {
        // 채널 5개 생성
        for (int i = 1; i <= 5; i++)
            Channels.Add(new ChannelViewModel(new ChannelModel(i)));

        // 기본 선택
        SelectedChannel = Channels[0];

        // FUNC1~8
        for (int i = 0; i < 8; i++)
            FuncButtons.Add(new HmiButtonModel($"FUNC{i + 1}", FuncVks[i]));

        // 좌측 F1~F6
        for (int i = 0; i < 6; i++)
            LeftButtons.Add(new HmiButtonModel($"F{i + 1}", SideLeftVks[i]));

        // 우측 F7~F12
        for (int i = 0; i < 6; i++)
            RightButtons.Add(new HmiButtonModel($"F{i + 7}", SideRightVks[i]));

        // 하단 F13~F20
        for (int i = 0; i < 8; i++)
            BottomButtons.Add(new HmiButtonModel($"F{i + 13}", BottomVks[i]));

        // Commands
        SelectChannelCommand = new RelayCommand(ExecuteSelectChannel);

        RunCommand = new RelayCommand(
            _ => ExecuteRun(),
            _ => IsIpValid && !IsRunning);

        SendKeyCommand = new RelayCommand(
            param => ExecuteSendKey(param),
            _ => IsRunning);
    }

    // ─── Command Handlers ────────────────────────────────────────────────────
    private void ExecuteSelectChannel(object? param)
    {
        if (param is ChannelViewModel ch)
            SelectedChannel = ch;
    }

    private void ExecuteRun()
    {
        // 실제 연결 로직 확장 지점 (TCP/UDP 등)
        ConnectionState = ConnectionState.Running;
    }

    private void ExecuteSendKey(object? param)
    {
        if (!IsRunning) return;

        int vk = param switch
        {
            HmiButtonModel btn => btn.VirtualKey,
            int i              => i,
            _                  => 0
        };

        if (vk != 0)
            InputService.SendVirtualKey(vk);
    }

    // ─── 향후 연결 해제 / 상태 전환 ─────────────────────────────────────────
    public void Disconnect()
    {
        ConnectionState = ConnectionState.Disconnected;
    }

    public void SetReady()
    {
        ConnectionState = ConnectionState.Ready;
    }
}
