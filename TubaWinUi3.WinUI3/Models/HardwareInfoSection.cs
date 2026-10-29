using System.Collections.ObjectModel;

namespace TubaWinUi3.Models;

public sealed class HardwareInfoSection
{
    public required string Title { get; init; }

    public required string Glyph { get; init; }

    public ObservableCollection<HardwareInfoItem> Items { get; } = [];
}
