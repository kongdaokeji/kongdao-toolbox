namespace TubaWinUi3.Compatible.Models
{
    public sealed class HardwareInfoItem
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string BrandKey { get; set; }
        public bool IsVerified { get; set; }

        public HardwareInfoItem()
        {
            Label = "";
            Value = "";
        }
    }
}
