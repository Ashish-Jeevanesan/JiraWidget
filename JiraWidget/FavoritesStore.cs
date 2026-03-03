using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JiraWidget
{
    public sealed class FavoritesStore
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public FavoritesStore()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JiraWidget");
            _filePath = Path.Combine(directory, "favorites.json");
        }

        public IReadOnlyList<string> LoadFavorites()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return Array.Empty<string>();
                }

                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<FavoritesData>(json);
                if (data?.IssueKeys == null)
                {
                    return Array.Empty<string>();
                }

                return data.IssueKeys
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => key.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load favorites from '{_filePath}'.", ex);
                return Array.Empty<string>();
            }
        }

        public void SaveFavorites(IEnumerable<string> issueKeys)
        {
            try
            {
                var uniqueKeys = issueKeys
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => key.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var data = new FavoritesData { IssueKeys = uniqueKeys };
                var directory = Path.GetDirectoryName(_filePath)!;
                Directory.CreateDirectory(directory);

                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(data, _jsonOptions));
                File.Move(tempPath, _filePath, true);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to save favorites to '{_filePath}'.", ex);
            }
        }

        private sealed class FavoritesData
        {
            public List<string> IssueKeys { get; set; } = new();
        }
    }
}
