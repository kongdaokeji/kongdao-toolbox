using System;
using System.Collections.Generic;
using System.IO;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    public enum ConfigLocation
    {
        AppData,
        AppRoot
    }

    public static class ConfigManager
    {
        private static readonly object _lock = new object();
        private static string _cachedDataDir;

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3");

        private static string AppRootDir
        {
            get { return Path.Combine(ToolCatalog.AppDirectory, "Data"); }
        }

        public static string GetDataDir()
        {
            lock (_lock)
            {
                if (_cachedDataDir != null) return _cachedDataDir;
                _cachedDataDir = GetConfigLocation() == ConfigLocation.AppRoot ? AppRootDir : AppDataDir;
                return _cachedDataDir;
            }
        }

        public static ConfigLocation GetConfigLocation()
        {
            try
            {
                var markerPath = Path.Combine(AppRootDir, ".config_location");
                if (File.Exists(markerPath)) return ConfigLocation.AppRoot;
            }
            catch { }
            return ConfigLocation.AppData;
        }

        public static bool SetConfigLocation(ConfigLocation location)
        {
            try
            {
                if (location == ConfigLocation.AppRoot)
                {
                    Directory.CreateDirectory(AppRootDir);
                    File.WriteAllText(Path.Combine(AppRootDir, ".config_location"), "AppRoot");
                }
                else
                {
                    var markerPath = Path.Combine(AppRootDir, ".config_location");
                    if (File.Exists(markerPath)) File.Delete(markerPath);
                }
                lock (_lock) { _cachedDataDir = null; }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetSettingsPath() { return Path.Combine(GetDataDir(), "settings.json"); }
        public static string GetFavoritesPath() { return Path.Combine(GetDataDir(), "favorites.json"); }
        public static string GetIconCacheDir() { return Path.Combine(GetDataDir(), "IconCache"); }

        public static void InvalidateAllCaches()
        {
            AppSettings.InvalidateCache();
            FavoritesService.InvalidateCache();
        }
    }
}
