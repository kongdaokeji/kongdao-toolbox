using Microsoft.UI.Xaml.Controls;
using KongdaoToolbox.Services;

namespace KongdaoToolbox.Pages;

public sealed partial class StressTestPage : Page
{
    public StressTestPage()
    {
        InitializeComponent();

        Unloaded += (_, _) => StressControl.Cleanup();
    }
}
