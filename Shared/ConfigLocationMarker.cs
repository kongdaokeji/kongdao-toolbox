using System.Security.Cryptography;
using System.Text;

namespace KongdaoToolbox.Storage;

internal static class ConfigLocationMarker
{
    internal static string GetPath(string localAppData, string appDirectory)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(appDirectory).TrimEnd('\\', '/').ToUpperInvariant())))[..24];
        return Path.Combine(localAppData, "KongdaoToolbox.ConfigLocations", id + ".txt");
    }
}
