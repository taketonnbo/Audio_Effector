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

            // Rock (V-shape)
            var rock = new List<float>();
            for(int i=0; i<16; i++) 
            {
                if(i < 4) rock.Add(4); // Bass boost
                else if(i > 11) rock.Add(3); // Treble boost
                else rock.Add(-2); // Mid cut
            }
            defaults.Add(new Preset { Name = "ロック (Rock)", Gains = rock });

            // Pop
            var pop = new List<float>();
            for(int i=0; i<16; i++) 
            {
                if(i < 3) pop.Add(-1);
                else if(i > 10) pop.Add(-1);
                else pop.Add(3); // Mid boost
            }
            defaults.Add(new Preset { Name = "ポップ (Pop)", Gains = pop });

            return defaults;
        }
    }
}
