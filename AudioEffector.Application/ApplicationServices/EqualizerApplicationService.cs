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
    private const string PRESETS_FILE_NAME = "presets.json";
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly IAudioEngine _audioEngine;
    private readonly IAudioService? _legacyAudioService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IEventBus _eventBus;
    private readonly IDataDirectoryProvider? _dataDirectoryProvider;
    private readonly IJsonFileLockHelper? _fileLockHelper;
    private readonly string _fallbackPresetsFilePath;
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
    /// <param name="legacyAudioService">移行期間中の既存再生サービス</param>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ（指定時はデータディレクトリ内の presets.json を使用）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー</param>
    public EqualizerApplicationService(
        IAudioEngine audioEngine,
        ISettingsRepository settingsRepository,
        IEventBus eventBus,
        IAudioService? legacyAudioService = null,
        IDataDirectoryProvider? dataDirectoryProvider = null,
        IJsonFileLockHelper? fileLockHelper = null)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _legacyAudioService = legacyAudioService;
        _dataDirectoryProvider = dataDirectoryProvider;
        _fileLockHelper = fileLockHelper;
        _currentPreset = EqualizerPreset.CreateFlat();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = System.IO.Path.Combine(appData, "AudioEffector");
        System.IO.Directory.CreateDirectory(folder);
        _fallbackPresetsFilePath = System.IO.Path.Combine(folder, PRESETS_FILE_NAME);
    }

    private string ResolvePresetsFilePath()
    {
        return _dataDirectoryProvider != null
            ? _dataDirectoryProvider.GetFilePath(PRESETS_FILE_NAME)
            : _fallbackPresetsFilePath;
    }

    /// <summary>
    /// イコライザープリセット一覧をローカルファイル（presets.json）から読み込みます
    /// </summary>
    /// <returns></returns>
    public List<EqualizerPreset> LoadPresets()
    {
        string filePath = ResolvePresetsFilePath();
        if (!System.IO.File.Exists(filePath))
        {
            return CreateDefaultPresets();
        }

        try
        {
            string json = System.IO.File.ReadAllText(filePath);
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
    /// <param name="presets">保存するイコライザープリセット一覧</param>
    public void SavePresets(List<EqualizerPreset> presets)
    {
        if (presets == null) return;
        const int maxPresets = 30;
        if (presets.Count > maxPresets)
        {
            presets = presets.GetRange(0, maxPresets);
        }

        string filePath = ResolvePresetsFilePath();
        string? directory = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(presets);
        if (_fileLockHelper != null)
        {
            using IDisposable fileLock = _fileLockHelper.AcquireLock(filePath);
            _fileLockHelper.WriteAllTextAtomic(filePath, json);
        }
        else
        {
            System.IO.File.WriteAllText(filePath, json);
        }
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
        if (_legacyAudioService != null)
        {
            for (int i = 0; i < gains.Length; i++)
            {
                _legacyAudioService.SetGain(i, gains[i]);
            }
        }

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
        _legacyAudioService?.SetGain(bandIndex, gain.Value);
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
        List<PresetDto> customDtos = await ReadCustomPresetsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var dto in customDtos)
        {
            presets.Add(dto.ToEntity());
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

        if (_dataDirectoryProvider != null)
        {
            await UpdatePresetsFileAsync(presets =>
            {
                int index = presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
                var dto = PresetDto.FromEntity(preset);
                if (index >= 0)
                {
                    presets[index] = dto;
                }
                else
                {
                    presets.Add(dto);
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _settingsRepository.UpdateValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, json =>
        {
            var presets = ReadCustomPresets(json);
            int index = presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            var dto = PresetDto.FromEntity(preset);
            if (index >= 0)
            {
                presets[index] = dto;
            }
            else
            {
                presets.Add(dto);
            }

            return JsonSerializer.Serialize(presets);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// カスタムプリセットを削除します。
    /// </summary>
    /// <param name="presetName">削除対象のプリセット名</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除対象が存在した場合はtrue</returns>
    public async Task<bool> DeleteCustomPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return false;
        }

        if (_dataDirectoryProvider != null)
        {
            return await UpdatePresetsFileAsync(presets =>
            {
                int removed = presets.RemoveAll(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
                return removed > 0;
            }, cancellationToken).ConfigureAwait(false);
        }

        return await _settingsRepository.UpdateValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, json =>
        {
            var presets = ReadCustomPresets(json);
            int removed = presets.RemoveAll(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
            return removed > 0 ? JsonSerializer.Serialize(presets) : json;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<PresetDto>> ReadCustomPresetsAsync(CancellationToken cancellationToken)
    {
        if (_dataDirectoryProvider != null)
        {
            string filePath = _dataDirectoryProvider.GetFilePath(PRESETS_FILE_NAME);
            if (System.IO.File.Exists(filePath))
            {
                try
                {
                    if (_fileLockHelper != null)
                    {
                        using IDisposable fileLock = await _fileLockHelper.AcquireLockAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (!System.IO.File.Exists(filePath))
                        {
                            return [];
                        }

                        string fileJson = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                        return ReadCustomPresetsSafe(fileJson);
                    }

                    string rawJson = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                    return ReadCustomPresetsSafe(rawJson);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return [];
                }
            }

            // presets.json が未作成の場合のみ、旧 settings.json 保存のカスタムプリセットを後方互換として読み込む
            string? legacyJson = await _settingsRepository.GetValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, null, cancellationToken).ConfigureAwait(false);
            return ReadCustomPresetsSafe(legacyJson);
        }

        string? json = await _settingsRepository.GetValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, null, cancellationToken).ConfigureAwait(false);
        return ReadCustomPresetsSafe(json);
    }

    private async Task<bool> UpdatePresetsFileAsync(Func<List<PresetDto>, bool> mutate, CancellationToken cancellationToken)
    {
        string filePath = _dataDirectoryProvider!.GetFilePath(PRESETS_FILE_NAME);
        string? directory = System.IO.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        if (_fileLockHelper != null)
        {
            using IDisposable fileLock = await _fileLockHelper.AcquireLockAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            List<PresetDto> presets = await LoadForMutationAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (!mutate(presets))
            {
                return false;
            }

            string serialized = JsonSerializer.Serialize(presets, _jsonOptions);
            await _fileLockHelper.WriteAllTextAtomicAsync(filePath, serialized, cancellationToken).ConfigureAwait(false);
            return true;
        }
        else
        {
            List<PresetDto> presets = await LoadForMutationAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (!mutate(presets))
            {
                return false;
            }

            string serialized = JsonSerializer.Serialize(presets, _jsonOptions);
            await System.IO.File.WriteAllTextAsync(filePath, serialized, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private async Task<List<PresetDto>> LoadForMutationAsync(string filePath, CancellationToken cancellationToken)
    {
        if (System.IO.File.Exists(filePath))
        {
            string fileJson = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return ReadCustomPresetsSafe(fileJson);
        }

        string? legacyJson = await _settingsRepository.GetValueAsync(CUSTOM_PRESETS_SETTINGS_KEY, null, cancellationToken).ConfigureAwait(false);
        return ReadCustomPresetsSafe(legacyJson);
    }

    private static List<PresetDto> ReadCustomPresetsSafe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PresetDto>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<PresetDto> ReadCustomPresets(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PresetDto>>(json) ?? [];

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
