using System.Windows;

namespace DdsScope.App.Views;

/// <summary>
/// The display-filter cheat sheet, opened by the "?" beside the filter box.
///
/// It is shown non-modally on purpose: the reason to open it is to write a filter, and a
/// dialog would lock the box the user is trying to type into.
/// </summary>
public partial class FilterHelpWindow : Window
{
    public FilterHelpWindow()
    {
        InitializeComponent();
    }
}
