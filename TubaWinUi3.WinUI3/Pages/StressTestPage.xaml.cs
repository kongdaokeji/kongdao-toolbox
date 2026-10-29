using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class StressTestPage : Page
{
    public StressTestPage()
    {
        InitializeComponent();

        Unloaded += (_, _) => StressControl.Cleanup();
    }
}
