using System.Diagnostics;
using System.Windows;

namespace Progress.Util.CoreLoading.Views;

public partial class CoreLoadingWindow : Window
{
    public CoreLoadingWindow() => InitializeComponent();

    public Task<Dictionary<string, Process>> Start(List<string> appNames) => LoadingView.Start(appNames);
    public void Notify(string appName, int stage) => LoadingView.Notify(appName, stage);
    public void NotifyCoreReady()                       => LoadingView.NotifyCoreReady();
}
