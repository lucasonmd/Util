using System.Windows;

namespace RuntimeMonitor.Viewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 처리되지 않은 예외 핸들러 등록
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"처리되지 않은 예외:\n{args.Exception.Message}",
                "RuntimeMonitor Viewer - 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
