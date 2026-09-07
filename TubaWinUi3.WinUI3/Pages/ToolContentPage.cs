using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace TubaWinUi3.Pages;

public sealed class ToolContentPageParam
{
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public required UIElement Content { get; init; }
    public Action? OnClose { get; init; }
}

/// <summary>
/// Hosts tool UIs that are built in code (no dedicated XAML page).
/// The tool content fills the page below a standard header; the app title bar
/// back button returns to the previous page.
/// </summary>
public sealed partial class ToolContentPage : Page
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _subtitleText;
    private readonly Grid _contentHost;
    private Action? _onClose;

    public ToolContentPage()
    {
        InitializeComponent();

        _titleText = new TextBlock
        {
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };

        _subtitleText = new TextBlock
        {
            FontSize = 14,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var header = new StackPanel
        {
            Padding = new Thickness(24, 16, 24, 8),
            Spacing = 4,
            Children = { _titleText, _subtitleText }
        };

        _contentHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(_contentHost, 1);
        root.Children.Add(header);
        root.Children.Add(_contentHost);

        Content = root;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ToolContentPageParam param)
        {
            _onClose = param.OnClose;
            _titleText.Text = param.Title;
            _subtitleText.Text = param.Description;
            _contentHost.Children.Add(param.Content);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Detach();
    }

    /// <summary>
    /// 执行与 OnNavigatedFrom 相同的清理逻辑。宿主（如独立工具窗口）在窗口关闭时
    /// 调用它，因为关闭窗口不会触发 Frame 的导航事件。
    /// </summary>
    public void Detach()
    {
        _onClose?.Invoke();
        _onClose = null;
        _contentHost.Children.Clear();
    }
}
