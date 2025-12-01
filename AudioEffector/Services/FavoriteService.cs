using AudioEffector.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AudioEffector.Services
{
    public class FavoriteService
    {
        private readonly string _favoritesFilePath;

        public FavoriteService()
        {
            var appDataPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "AudioEffector");
            Directory.CreateDirectory(appDataPath);
            _favoritesFilePath = Path.Combine(appDataPath, "favorites.json");
        }

        public List<string> LoadFavorites()
        {
            if (File.Exists(_favoritesFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_favoritesFilePath);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
                catch { }
            }
            return new List<string>();
        }

        public void SaveFavorites(List<string> favorites)
        {
            try
            {
                string json = JsonSerializer.Serialize(favorites);
                File.WriteAllText(_favoritesFilePath, json);
            }
            catch { }
        }
    }
}
