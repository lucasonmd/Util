using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Progress.Util.CoreLoading;

internal enum StageState { Waiting, Running, Completed, TimedOut }

internal abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void RaisePropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? _) => true;
    public void Execute(object? _)    => execute();
}

internal sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
{
    private bool _busy;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => !_busy;
    public async void Execute(object? _)
    {
        if (_busy) return;
        _busy = true;  CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try   { await execute(); }
        finally { _busy = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}

[ValueConversion(typeof(StageState), typeof(Brush))]
internal sealed class StateToColorConverter : IValueConverter
{
    public static readonly StateToColorConverter Instance = new();
    private static readonly SolidColorBrush BWaiting   = F(0x3C, 0x3C, 0x3C);
    private static readonly SolidColorBrush BRunning   = F(0xFF, 0xBF, 0x00);
    private static readonly SolidColorBrush BCompleted = FA(0x55, 0x2C, 0x72, 0x40);
    private static readonly SolidColorBrush BTimedOut  = F(0xC0, 0x22, 0x22);
    public object Convert(object v, Type t, object p, CultureInfo c) =>
        v is StageState s ? s switch {
            StageState.Running   => BRunning,
            StageState.Completed => BCompleted,
            StageState.TimedOut  => BTimedOut,
            _                    => BWaiting,
        } : BWaiting;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
    private static SolidColorBrush F(byte r, byte g, byte b)
        { var b2 = new SolidColorBrush(Color.FromRgb(r, g, b)); b2.Freeze(); return b2; }
    private static SolidColorBrush FA(byte a, byte r, byte g, byte b)
        { var b2 = new SolidColorBrush(Color.FromArgb(a, r, g, b)); b2.Freeze(); return b2; }
}

internal sealed class StageDisplayConverter : IMultiValueConverter
{
    public static readonly StageDisplayConverter Instance = new();
    public object Convert(object[] v, Type t, object p, CultureInfo c) =>
        v is [StageState state, string name] ? state switch {
            StageState.Running   => name,
            StageState.Completed => "✓",
            StageState.TimedOut  => "✗",
            _                    => string.Empty,
        } : string.Empty;
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

internal class StageViewModel(int stageNumber, string stageName) : ViewModelBase
{
    private StageState _state = StageState.Waiting;
    public StageState State
    {
        get => _state;
        set { if (Set(ref _state, value)) RaisePropertyChanged(nameof(IsRunning)); }
    }
    public int    StageNumber => stageNumber;
    public string StageName   => stageName;
    public bool   IsRunning   => State == StageState.Running;
}

internal class AppInitViewModel : ViewModelBase
{
    private bool _isTimedOut;
    public bool IsTimedOut
    {
        get => _isTimedOut;
        set { if (Set(ref _isTimedOut, value)) RaisePropertyChanged(nameof(OverallState)); }
    }

    private bool _showRestartButton;
    public bool ShowRestartButton { get => _showRestartButton; set => Set(ref _showRestartButton, value); }

    public int    AppId   { get; }
    public string AppName { get; }
    public IReadOnlyList<StageViewModel> Stages { get; }
    public ICommand RestartCommand { get; }

    public int CompletedStageCount => Stages.Count(s => s.State == StageState.Completed);

    public StageState OverallState
    {
        get
        {
            if (IsTimedOut)                                        return StageState.TimedOut;
            if (Stages.All(s => s.State == StageState.Completed)) return StageState.Completed;
            if (Stages.Any(s => s.State == StageState.Running))   return StageState.Running;
            return StageState.Waiting;
        }
    }

    public AppInitViewModel(int appId, string appName, Func<AppInitViewModel, Task> restartHandler)
    {
        AppId = appId; AppName = appName;
        Stages = [new(1, "Launch"), new(2, "Registry"), new(3, "TopLabel")];
        RestartCommand = new AsyncRelayCommand(() => restartHandler(this));
    }

    internal void NotifyOverallStateChanged() => RaisePropertyChanged(nameof(OverallState));
}

internal class CoreLoadingViewModel : ViewModelBase
{
    private CancellationTokenSource _cts = new();
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, TaskCompletionSource[]> _stageSignals = new();
    private readonly Dictionary<string, Process> _processes = new();
    private readonly TaskCompletionSource _coreReadySignal = new();

    public ObservableCollection<AppInitViewModel> Apps { get; } = [];
    public StageViewModel CoreReadyStage { get; } = new(0, "StartApp Ready");
    public ICommand ToggleOverlayCommand { get; }

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; private set => Set(ref _progressPercent, value); }

    private string _progressText = "0%";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    private bool _isOverlayVisible;
    public bool IsOverlayVisible { get => _isOverlayVisible; set => Set(ref _isOverlayVisible, value); }

    private bool _isComplete;
    public bool IsComplete { get => _isComplete; private set => Set(ref _isComplete, value); }

    internal CoreLoadingViewModel(List<string> appNames)
    {
        for (int i = 0; i < appNames.Count; i++)
        {
            _stageSignals[appNames[i]] = [new(), new(), new()];
            Apps.Add(new AppInitViewModel(i + 1, appNames[i], RestartAppAsync));
        }
        ToggleOverlayCommand = new RelayCommand(() => IsOverlayVisible = !IsOverlayVisible);
        UpdateProgress();
    }

    internal void Notify(string appName, int stage)
    {
        if (!_stageSignals.TryGetValue(appName, out var signals)) return;
        signals[stage].TrySetResult();
    }

    internal void NotifyCoreReady() => _coreReadySignal.TrySetResult();

    internal async Task<Dictionary<string, Process>> RunAsync()
    {
        _cts = new CancellationTokenSource();

        CoreReadyStage.State = StageState.Running;
        UpdateProgress();

        await Task.WhenAll(
            Apps.Select(app => RunAppAsync(app, _cts.Token))
                .Append(WaitCoreReadyAsync(_cts.Token)));

        IsComplete = CoreReadyStage.State == StageState.Completed;

        return Apps.Where(a => a.CompletedStageCount == 3 && _processes.ContainsKey(a.AppName))
                   .ToDictionary(a => a.AppName, a => _processes[a.AppName]);
    }

    private async Task WaitCoreReadyAsync(CancellationToken ct)
    {
        try
        {
            await _coreReadySignal.Task.WaitAsync(ct);
            CoreReadyStage.State = StageState.Completed;
        }
        catch (OperationCanceledException)
        {
            CoreReadyStage.State = StageState.TimedOut;
        }
        UpdateProgress();
    }

    internal void Cancel() => _cts.Cancel();

    private void LaunchAndNotify(AppInitViewModel app)
    {
        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, app.AppName + ".exe");
        try
        {
            var process = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            if (process is not null)
            {
                _processes[app.AppName] = process;
                Notify(app.AppName, 0);
            }
        }
        catch { }
    }

    private async Task RunAppAsync(AppInitViewModel app, CancellationToken ct)
    {
        LaunchAndNotify(app);

        for (int i = 0; i < app.Stages.Count; i++)
        {
            app.Stages[i].State = StageState.Running;
            UpdateProgress();

            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stageCts.CancelAfter(StageTimeout);
            try
            {
                await _stageSignals[app.AppName][i].Task.WaitAsync(stageCts.Token);
                app.Stages[i].State = StageState.Completed;
                app.NotifyOverallStateChanged();
                UpdateProgress();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                app.Stages[i].State = StageState.TimedOut;
                app.IsTimedOut = true;
                app.ShowRestartButton = true;
                app.NotifyOverallStateChanged();
                UpdateProgress();
                return;
            }
        }
    }

    private async Task RestartAppAsync(AppInitViewModel app)
    {
        _stageSignals[app.AppName] = [new(), new(), new()];
        foreach (var s in app.Stages) s.State = StageState.Waiting;
        app.IsTimedOut = false;
        app.NotifyOverallStateChanged();
        UpdateProgress();
        await RunAppAsync(app, _cts.Token);
    }

    private void UpdateProgress()
    {
        int total = Apps.Count * 3 + 1;
        int done  = Apps.Sum(a => a.CompletedStageCount)
                  + (CoreReadyStage.State == StageState.Completed ? 1 : 0);
        double pct = Math.Round((double)done / total * 100.0);
        ProgressPercent = pct;
        ProgressText    = $"{(int)pct}%";
    }
}
