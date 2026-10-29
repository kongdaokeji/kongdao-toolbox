namespace TubaWinUi3.Models;

public sealed class CertBlockVendor
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<CertBlockEntry> Certificates { get; set; } = [];
    public bool IsBlocked => Certificates.Count > 0 && Certificates.All(c => c.IsBlocked);
    public bool IsPartiallyBlocked => Certificates.Any(c => c.IsBlocked) && !IsBlocked;
    public int TotalCount => Certificates.Count;
    public int BlockedCount => Certificates.Count(c => c.IsBlocked);
}

public sealed class CertBlockEntry
{
    public string FileName { get; set; } = "";
    public string CommonName { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public bool IsBlocked { get; set; }
    public string Thumbprint { get; set; } = "";
}
