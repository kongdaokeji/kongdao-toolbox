using System.ComponentModel;
using System.Runtime.CompilerServices;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>游戏选择网格里的一张卡片（内置档案或自定义游戏）。</summary>
public sealed class TunnelGameCard : TunnelObservable
{
    public GamePreset? Preset { get; init; }
    public CustomGame? Custom { get; init; }

    /// <summary>末尾的「自定义游戏」入口卡片。</summary>
    public bool IsAddCard { get; init; }

    public string Id => Preset?.Id ?? Custom?.Id ?? "";

    /// <summary>x:Bind 的按钮 Tag 用：把整张卡片传回事件处理器。</summary>
    public TunnelGameCard Self => this;

    public string Name => IsAddCard ? "自定义游戏" : Preset?.Name ?? Custom?.Name ?? "";

    public string Glyph => IsAddCard ? "\uE710" : Preset?.Glyph ?? "\uE7FC";

    /// <summary>游戏的真实 Logo；还没拿到（或拿不到）时界面显示 <see cref="Glyph"/>。</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? Logo
    {
        get => _logo;
        set
        {
            if (!Set(ref _logo, value)) return;
            Raise(nameof(GlyphVisibility));
        }
    }

    private Microsoft.UI.Xaml.Media.ImageSource? _logo;

    public Microsoft.UI.Xaml.Visibility GlyphVisibility
        => _logo is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>官方 Logo 的候选地址（自定义游戏为空，用字形）。</summary>
    public IReadOnlyList<string> LogoUrls => Preset?.LogoUrls ?? [];

    /// <summary>补齐真实 Logo：有本地缓存立即返回，没有就下载一次；失败保持原样。</summary>
    public async Task LoadLogoAsync()
    {
        if (IsAddCard || LogoUrls.Count == 0) return;
        Logo = await GameLogoService.GetAsync(Id, LogoUrls);
    }

    public string PortText => IsAddCard ? "" : $"端口 {(Preset?.DefaultPort ?? Custom?.Port ?? 0)}";

    public string ProtocolText
    {
        get
        {
            if (IsAddCard) return "";
            var protocol = Preset?.Protocol ?? Custom?.Protocol ?? GameTunnelProtocol.Tcp;
            return protocol.Describe();
        }
    }

    public string Tagline => IsAddCard ? "没有你的游戏？自己填端口" : Preset?.Tagline ?? Custom?.Note ?? "自定义游戏";

    public int Port => Preset?.DefaultPort ?? Custom?.Port ?? 0;

    public GameTunnelProtocol Protocol => Preset?.Protocol ?? Custom?.Protocol ?? GameTunnelProtocol.Tcp;

    /// <summary>卡片右侧的小标签：TCP / UDP。</summary>
    public string ProtocolBadge => ProtocolText;

    public bool HasProtocolBadge => !IsAddCard;

    public static TunnelGameCard FromPreset(GamePreset preset) => new() { Preset = preset };

    public static TunnelGameCard FromCustom(CustomGame game) => new() { Custom = game };

    public static TunnelGameCard AddCard() => new() { IsAddCard = true };
}

/// <summary>带属性通知的简易基类（仅用于需要就地刷新的少量属性）。</summary>
public abstract class TunnelObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}
