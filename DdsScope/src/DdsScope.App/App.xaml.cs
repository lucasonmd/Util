using System.Windows;
using System.Windows.Threading;

namespace DdsScope.App;

public partial class App : Application
{
    /// <summary>Domain id from the command line, if one was given.</summary>
    public static int? StartupDomainId { get; private set; }

    /// <summary>True when --connect was passed, so the session starts already capturing.</summary>
    public static bool StartupAutoConnect { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ParseArguments(e.Args);

        // A failure in one view must not take the capture session down with it.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>
    /// Usage: DdsScope [--domain N] [--connect]
    /// Handy for launching straight into a known domain from a script or a shortcut.
    /// </summary>
    private static void ParseArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--connect", StringComparison.OrdinalIgnoreCase))
            {
                StartupAutoConnect = true;
            }
            else if (string.Equals(args[i], "--domain", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length &&
                     int.TryParse(args[i + 1], out var domain))
            {
                StartupDomainId = domain;
                i++;
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "DdsScope - unexpected UI error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }
}
