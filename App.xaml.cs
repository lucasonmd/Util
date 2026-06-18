using Progress.Util.CoreLoading.Views;
using System.Diagnostics;
using System.Windows;

namespace Progress.Util;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new CoreLoadingWindow();
        MainWindow = window;
        window.Show();

        List<string> appNames = ["AppAlpha", "AppBeta", "AppGamma", "AppDelta", "AppEpsilon"];
        var task = window.Start(appNames);

        // 데모용 시뮬레이션 — 실제 배포 시 각 앱/Core 모듈이 직접 Notify를 호출합니다.
        _ = SimulateAsync(window, appNames);

        Dictionary<string, Process> result = await task;
    }

    private static async Task SimulateAsync(CoreLoadingWindow w, List<string> names)
    {
        var rng = new Random();
        var perApp = names.Select(name => Task.Run(async () =>
        {
            await Task.Delay(rng.Next(500, 2000));
            w.Notify(name, 0);

            await Task.Delay(rng.Next(800, 3000));
            w.Notify(name, 1);

            await Task.Delay(rng.Next(500, 2000));
            w.Notify(name, 2);
        })).ToList();

        await Task.WhenAll(perApp);

        await Task.Delay(rng.Next(500, 1500));
        w.NotifyCoreReady();
    }
}
