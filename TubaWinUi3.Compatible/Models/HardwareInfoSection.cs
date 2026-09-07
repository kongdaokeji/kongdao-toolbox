using System.Collections.Generic;

namespace TubaWinUi3.Compatible.Models
{
    public sealed class HardwareInfoSection
    {
        public string Title { get; set; }
        public string Glyph { get; set; }
        public List<HardwareInfoItem> Items { get; } = new List<HardwareInfoItem>();

        public HardwareInfoSection()
        {
            Title = "";
            Glyph = "";
        }
    }
}
