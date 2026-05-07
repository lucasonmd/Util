using RuntimeMonitor.Core.DTOs;
using RuntimeMonitor.Viewer.Models;
using RuntimeMonitor.Viewer.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace RuntimeMonitor.Viewer.ViewModels;

/// <summary>
/// 메인 화면 ViewModel (MVVM 패턴)
///
/// 설계 원칙:
///   - View는 ViewModel만 알고, ViewModel은 View를 모름
///   - UI 업데이트는 항상 Dispatcher를 통해 (UI 스레드 안전)
///   - ObservableCollection 성능: MaxDisplayItems 제한으로 DOM 과부하 방지
///   - 전체 패킷은 _allPackets 리스트에 보관, UI는 필터링된 부분만 표시
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly MonitorServer _server;
    private readonly Dispatcher _dispatcher;
    private readonly FilterSettings _filter = new();

    // UI 바인딩 컬렉션 - 항상 Dispatcher 스레드에서만 수정
    private readonly ObservableCollection<PacketItemViewModel> _packets = new();

    // 전체 수신 패킷 보관 (필터 재적용 시 사용)
    private readonly List<PacketItem> _allPackets = new();

    private string _statusText = "대기 중";
    private string _typeFilter = string.Empty;
    private string _methodFilter = string.Empty;
    private string _processFilter = string.Empty;
    private bool _isRunning;
    private int _port = 9000;
    private int _totalCount;
    private int _connectedClients;
    private PacketItemViewModel? _selectedPacket;
    private bool _autoScroll = true;

    // UI 표시 최대 개수 제한 (ObservableCollection은 항목 수에 민감)
    private const int MaxDisplayItems = 2_000;

    #region 바인딩 프로퍼티

    public ObservableCollection<PacketItemViewModel> Packets => _packets;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    public int Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetField(ref _totalCount, value);
    }

    public int ConnectedClients
    {
        get => _connectedClients;
        private set => SetField(ref _connectedClients, value);
    }

    public PacketItemViewModel? SelectedPacket
    {
        get => _selectedPacket;
        set => SetField(ref _selectedPacket, value);
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetField(ref _autoScroll, value);
    }

    public string TypeFilter
    {
        get => _typeFilter;
        set
        {
            if (!SetField(ref _typeFilter, value)) return;
            _filter.TypeNameFilter = value;
            ApplyFilter();
        }
    }

    public string MethodFilter
    {
        get => _methodFilter;
        set
        {
            if (!SetField(ref _methodFilter, value)) return;
            _filter.MethodNameFilter = value;
            ApplyFilter();
        }
    }

    public string ProcessFilter
    {
        get => _processFilter;
        set
        {
            if (!SetField(ref _processFilter, value)) return;
            _filter.ProcessNameFilter = value;
            ApplyFilter();
        }
    }

    #endregion

    #region 커맨드

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ClearFilterCommand { get; }

    #endregion

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _server = new MonitorServer();

        // 서버 이벤트를 Dispatcher를 통해 UI에 전달
        _server.PacketReceived += OnPacketReceived;

        _server.ClientConnected += (id, endpoint) =>
            _dispatcher.InvokeAsync(() => UpdateConnectionStatus(), DispatcherPriority.Normal);

        _server.ClientDisconnected += id =>
            _dispatcher.InvokeAsync(() => UpdateConnectionStatus(), DispatcherPriority.Normal);

        StartCommand = new RelayCommand(
            async () => await StartServerAsync(),
            () => !IsRunning);

        StopCommand = new RelayCommand(
            async () => await StopServerAsync(),
            () => IsRunning);

        ClearCommand = new RelayCommand(ClearPackets);

        ClearFilterCommand = new RelayCommand(() =>
        {
            TypeFilter = string.Empty;
            MethodFilter = string.Empty;
            ProcessFilter = string.Empty;
        });
    }

    private async Task StartServerAsync()
    {
        try
        {
            await _server.StartAsync(Port);
            IsRunning = true;
            StatusText = $"포트 {Port}에서 수신 대기 중";
        }
        catch (Exception ex)
        {
            StatusText = $"시작 실패: {ex.Message}";
        }
    }

    private async Task StopServerAsync()
    {
        await _server.DisposeAsync();
        IsRunning = false;
        StatusText = "중지됨";
    }

    private void OnPacketReceived(MonitorPacket packet)
    {
        var item = PacketItem.FromPacket(packet);

        // Background 우선순위: UI 프레임 렌더링을 방해하지 않고 업데이트
        _dispatcher.InvokeAsync(() =>
        {
            _allPackets.Add(item);
            TotalCount = _allPackets.Count;

            if (!_filter.Matches(item)) return;

            // 표시 한도 초과 시 가장 오래된 항목 제거 (O(1) Remove at 0)
            if (_packets.Count >= MaxDisplayItems)
                _packets.RemoveAt(0);

            _packets.Add(new PacketItemViewModel(item));

        }, DispatcherPriority.Background);
    }

    private void ApplyFilter()
    {
        _dispatcher.InvokeAsync(() =>
        {
            _packets.Clear();

            // LINQ로 필터링 후 TakeLast로 최신 항목 우선
            var filtered = _allPackets
                .Where(_filter.Matches)
                .TakeLast(MaxDisplayItems)
                .Select(p => new PacketItemViewModel(p));

            foreach (var vm in filtered)
                _packets.Add(vm);

        }, DispatcherPriority.Background);
    }

    private void ClearPackets()
    {
        _allPackets.Clear();
        _packets.Clear();
        TotalCount = 0;
    }

    private void UpdateConnectionStatus()
    {
        ConnectedClients = _server.ConnectedCount;
        if (IsRunning)
            StatusText = $"포트 {Port}에서 수신 대기 중 | 연결: {ConnectedClients}개";
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    #endregion
}
