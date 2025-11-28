using AudioEffector.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AudioEffector.Services
{
    public class FavoriteService
    {
        private const string FavoritesFileName = "favorites.json";

        public List<string> LoadFavorites()
        {
            if (File.Exists(FavoritesFileName))
            {
                try
                {
                    string json = File.ReadAllText(FavoritesFileName);
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
                File.WriteAllText(FavoritesFileName, json);
            }
            catch { }
        }
    }
}
