using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 10バンドイコライザーのプリセット適用、バンドゲイン調整、カスタムプリセット永続化を統括するアプリケーションサービス
/// </summary>
public class EqualizerApplicationService
{
    private const string CUSTOM_PRESETS_SETTINGS_KEY = "Equalizer_CustomPresets";
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IEventBus _eventBus;
    private readonly string _presetsFilePath;
    private readonly object _lock = new();

    private EqualizerPreset _currentPreset;

    /// <summary>
    /// 現在適用されているイコライザープリセット
    /// </summary>
    public EqualizerPreset CurrentPreset
    {
        get
        {
            lock (_lock) return _currentPreset;
        }
    }

    /// <summary>
    /// EqualizerApplicationServiceを初期化します
    /// </summary>
    /// <param name="audioEngine">オーディオ再生エンジン</param>
    /// <param name="settingsRepository">設定リポジトリ</param>
    /// <param name="eventBus">イベントバス</param>
    public EqualizerApplicationService(
        IAudioEngine audioEngine,
        ISettingsRepository settingsRepository,
        IEventBus eventBus)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _currentPreset = EqualizerPreset.CreateFlat();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = System.IO.Path.Combine(appData, "AudioEffector");
        System.IO.Directory.CreateDirectory(folder);
        _presetsFilePath = System.IO.Path.Combine(folder, "presets.json");
    }

    /// <summary>
    /// イコライザープリセット一覧をローカルファイル（presets.json）から読み込みます
    /// </summary>
    public List<EqualizerPreset> LoadPresets()
    {
        if (!System.IO.File.Exists(_presetsFilePath))
        {
            return CreateDefaultPresets();
        }

        try
        {
            string json = System.IO.File.ReadAllText(_presetsFilePath);
            return JsonSerializer.Deserialize<List<EqualizerPreset>>(json) ?? CreateDefaultPresets();
        }
        catch
        {
            return CreateDefaultPresets();
        }
    }

    /// <summary>
    /// イコライザープリセット一覧をローカルファイル（presets.json）へ保存します
    /// </summary>
    public void SavePresets(List<EqualizerPreset> presets)
    {
        if (presets == null) return;
        const int maxPresets = 30;
        if (presets.Count > maxPresets)
        {
            presets = presets.GetRange(0, maxPresets);
        }
        string json = JsonSerializer.Serialize(presets);
        System.IO.File.WriteAllText(_presetsFilePath, json);
    }

    private static List<EqualizerPreset> CreateDefaultPresets()
    {
        return [new EqualizerPreset { Name = "フラット (Flat)", Gains = new List<float>(new float[16]) }];
    }

    /// <summary>
    /// 指定されたイコライザープリセットを適用します
    /// </summary>
    /// <param name="preset">適用対象のプリセット</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task ApplyPresetAsync(EqualizerPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (_lock)
        {
            _currentPreset = preset;
        }

        var gains = preset.Bands.Select(b => b.Gain.Value).ToArray();
        await _audioEngine.SetEqualizerAllGainsAsync(gains, cancellationToken);
        await _eventBus.PublishAsync(new EqualizerPresetChangedEvent(preset), cancellationToken);
    }

    /// <summary>
    /// 特定周波数バンドのゲインを更新します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス（0〜9）</param>
    /// <param name="gain">設定するゲイン値</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task UpdateBandGainAsync(int bandIndex, Gain gain, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _currentPreset.UpdateBandGain(bandIndex, gain);
        }

        await _audioEngine.SetEqualizerBandGainAsync(bandIndex, gain.Value, cancellationToken);
        await _eventBus.PublishAsync(new EqualizerPresetChangedEvent(_currentPreset), cancellationToken);
    }

    /// <summary>
    /// 利用可能なすべてのプリセット一覧（標準プリセット + カスタムプリセット）を取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>プリセット一覧</returns>
    public async Task<IReadOnlyList<EqualizerPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
    {
        var presets = new List<EqualizerPreset>
        {
            EqualizerPreset.CreateFlat(),
            CreatePreset("Rock", 4.0f, 3.0f, 2.0f, 0.0f, -1.0f, -1.0f, 0.0f, 2.0f, 3.5f, 4.5f),
            CreatePreset("Pop", -1.0f, 1.0f, 3.0f, 4.0f, 3.0f, 1.0f, -1.0f, -1.5f, -1.0f, -1.0f),
            CreatePreset("Jazz", 3.0f, 2.0f, 1.0f, 2.0f, -1.0f, -1.0f, 0.0f, 1.5f, 2.5f, 3.5f),
            CreatePreset("Classic", 4.5f, 3.5f, 2.5f, 1.5f, -1.0f, -1.0f, 0.0f, 2.0f, 3.0f, 4.0f),
            CreatePreset("Club", 0.0f, 0.0f, 2.0f, 3.5f, 3.5f, 3.5f, 2.0f, 0.0f, 0.0f, 0.0f),
            CreatePreset("Vocal", -2.0f, -3.0f, -2.0f, 1.0f, 3.5f, 4.0f, 3.5f, 2.0f, 0.0f, -2.0f),
            CreatePreset("Treble Boost", 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 2.5f, 4.0f, 5.5f, 7.0f),
            CreatePreset("Bass Boost", 6.0f, 5.0f, 4.0f, 2.5f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f)
        };

        // カスタムプリセットの読み込み
        string? json = await _settingsRepository.GetValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, null, cancellationToken);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var dtos = JsonSerializer.Deserialize<List<PresetDto>>(json);
                if (dtos != null)
                {
                    foreach (var dto in dtos)
                    {
                        presets.Add(dto.ToEntity());
                    }
                }
            }
            catch
            {
                // デシリアライズ失敗時はスキップ
            }
        }

        return presets;
    }

    /// <summary>
    /// カスタムプリセットを保存します
    /// </summary>
    /// <param name="preset">保存対象のプリセット</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveCustomPresetAsync(EqualizerPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var presets = (await GetPresetsAsync(cancellationToken)).Where(p => p.IsCustom).ToList();
        int existingIndex = presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            presets[existingIndex] = preset;
        }
        else
        {
            presets.Add(preset);
        }

        var dtos = presets.Select(PresetDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos);
        await _settingsRepository.SetValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, json, cancellationToken);
    }

    private static EqualizerPreset CreatePreset(string name, params float[] gainsDb)
    {
        var bands = new List<FrequencyBand>();
        for (int i = 0; i < EqualizerPreset.STANDARD_10_BAND_FREQUENCIES.Length; i++)
        {
            float gain = i < gainsDb.Length ? gainsDb[i] : 0.0f;
            bands.Add(new FrequencyBand(EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[i], Gain.FromDecibels(gain)));
        }

        return new EqualizerPreset(name, bands, isCustom: false);
    }

    private sealed class PresetDto
    {
        public string Name { get; set; } = string.Empty;
        public List<float> GainsDb { get; set; } = [];

        public static PresetDto FromEntity(EqualizerPreset preset) => new()
        {
            Name = preset.Name,
            GainsDb = preset.Bands.Select(b => b.Gain.Value).ToList()
        };

        public EqualizerPreset ToEntity()
        {
            var bands = new List<FrequencyBand>();
            for (int i = 0; i < EqualizerPreset.STANDARD_10_BAND_FREQUENCIES.Length; i++)
            {
                float gain = i < GainsDb.Count ? GainsDb[i] : 0.0f;
                bands.Add(new FrequencyBand(EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[i], Gain.FromDecibels(gain)));
            }

            return new EqualizerPreset(Name, bands, isCustom: true);
        }
    }
}
