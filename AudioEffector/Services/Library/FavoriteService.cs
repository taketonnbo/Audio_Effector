using AudioEffector.Domain.Entities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AudioEffector.Services
{
    /// <summary>
    /// お気に入りトラックのリストを管理・永続化するサービス。
    /// </summary>
    public class FavoriteService : IFavoriteService
    {
        private readonly string _favoritesFilePath;

        /// <summary>
        /// コンストラクタ。保存先ファイルパスを設定します。
        /// </summary>
        public FavoriteService()
        {
            var appDataPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "AudioEffector");
            Directory.CreateDirectory(appDataPath);
            _favoritesFilePath = Path.Combine(appDataPath, "favorites.json");
        }

        /// <summary>
        /// お気に入りリストをJSONファイルから読み込みます。
        /// </summary>
        /// <returns>ファイルパスのリスト。</returns>
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
