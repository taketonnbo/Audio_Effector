using AudioEffector.Infrastructure.Logging;
using AudioEffector.Models;
using System.Collections.Generic;

namespace AudioEffector.Services
{
    public interface IPresetService
    {
        [LogDescription("イコライザープリセットを読み込みます")]
        List<Preset> LoadPresets();

        [LogDescription("イコライザープリセットを保存します")]
        void SavePresets(List<Preset> presets);
    }
}
