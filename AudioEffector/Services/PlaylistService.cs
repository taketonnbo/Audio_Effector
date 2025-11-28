using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AudioEffector.Models;

namespace AudioEffector.Services
{
    public class PlaylistService
    {
        private readonly string _playlistsFilePath;

        public PlaylistService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioEffector");
            Directory.CreateDirectory(appDataPath);
            _playlistsFilePath = Path.Combine(appDataPath, "playlists.json");
        }

        public List<UserPlaylist> LoadPlaylists()
        {
            if (!File.Exists(_playlistsFilePath))
                return new List<UserPlaylist>();

            try
            {
                var json = File.ReadAllText(_playlistsFilePath);
                return JsonSerializer.Deserialize<List<UserPlaylist>>(json) ?? new List<UserPlaylist>();
            }
            catch
            {
                return new List<UserPlaylist>();
            }
        }

        public void SavePlaylists(List<UserPlaylist> playlists)
        {
            try
            {
                var json = JsonSerializer.Serialize(playlists, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_playlistsFilePath, json);
            }
            catch
            {
                // Silent fail for now
            }
        }
    }
}
