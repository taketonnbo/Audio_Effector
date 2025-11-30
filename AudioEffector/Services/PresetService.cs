using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AudioEffector.Models;

namespace AudioEffector.Services
{
    public class PresetService
    {
        private readonly string _filePath;
        private const int MaxPresets = 30;

        public PresetService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "presets.json");
        }

        public List<Preset> LoadPresets()
        {
            if (!File.Exists(_filePath))
            {
                return CreateDefaultPresets();
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<Preset>>(json) ?? CreateDefaultPresets();
            }
            catch
            {
                return CreateDefaultPresets();
            }
        }

        public void SavePresets(List<Preset> presets)
        {
            if (presets.Count > MaxPresets)
            {
                presets = presets.GetRange(0, MaxPresets);
            }
            string json = JsonSerializer.Serialize(presets);
            File.WriteAllText(_filePath, json);
        }

        private List<Preset> CreateDefaultPresets()
        {
            var defaults = new List<Preset>();
            
            // Default Flat
            defaults.Add(new Preset { Name = "フラット (Flat)", Gains = new List<float>(new float[16]) });

            return defaults;
        }
    }
}
