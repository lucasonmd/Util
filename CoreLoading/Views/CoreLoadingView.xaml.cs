using Progress.Util.CoreLoading;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Progress.Util.CoreLoading.Views;

internal partial class CoreLoadingView : UserControl
{
    private CoreLoadingViewModel? _vm;
    private TaskCompletionSource<Dictionary<string, Process>>? _tcs;
    private List<string>? _pendingNames;

    public CoreLoadingView()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += (_, _) => _vm?.Cancel();
    }

    internal Task<Dictionary<string, Process>> Start(List<string> appNames)
    {
        _tcs          = new TaskCompletionSource<Dictionary<string, Process>>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingNames = appNames;

        if (IsLoaded)
            _ = BeginAsync();

        return _tcs.Task;
    }

    internal void Notify(string appName) => _vm?.Notify(appName);

    internal void NotifyCoreReady() => _vm?.NotifyCoreReady();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_pendingNames is not null)
            await BeginAsync();
    }

    private async Task BeginAsync()
    {
        var names     = _pendingNames!;
        _pendingNames = null;

        _vm         = new CoreLoadingViewModel(names);
        DataContext  = _vm;
        ContentRoot.Visibility = Visibility.Visible;

        try
        {
            var result = await _vm.RunAsync();
            _tcs?.TrySetResult(result);
        }
        catch (OperationCanceledException ex) { _tcs?.TrySetCanceled(ex.CancellationToken); }
        catch (Exception ex)                  { _tcs?.TrySetException(ex); }
    }

    private void OpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.IsOverlayVisible = true;
    }

    private void DimBackground_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm is not null) { _vm.IsOverlayVisible = false; e.Handled = true; }
    }

    private void DimBackground_TouchDown(object sender, TouchEventArgs e)
    {
        if (_vm is not null) { _vm.IsOverlayVisible = false; e.Handled = true; }
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.IsOverlayVisible = false;
    }

    private void Panel_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;
    private void Panel_TouchDown(object sender, TouchEventArgs e)                             => e.Handled = true;
}
