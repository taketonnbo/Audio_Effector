using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Logging;
using System.Collections.Generic;

namespace AudioEffector.Services
{
    public interface IPresetService
    {
        [LogDescription("イコライザープリセットを読み込みます")]
        List<EqualizerPreset> LoadPresets();

        [LogDescription("イコライザープリセットを保存します")]
        void SavePresets(List<EqualizerPreset> presets);
    }
}
