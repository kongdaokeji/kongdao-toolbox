using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class StressTestPage : Page
{
    public StressTestPage()
    {
        InitializeComponent();

        Unloaded += (_, _) => StressControl.Cleanup();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();
}
