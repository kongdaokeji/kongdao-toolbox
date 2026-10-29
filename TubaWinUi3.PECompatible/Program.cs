using Aprillz.MewUI;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.PECompatible;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ThemeManager.Default = AppSettings.GetBool("ThemeLight", false)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        ThemeManager.DefaultAccent = Accent.Blue;

        Win32Platform.Register();
        GdiBackend.Register();

        try
        {
            ToolIconService.CleanExpiredCache();
        }
        catch
        {
        }

        Application.Run(new MainWindow().CreateWindow());
    }
}
