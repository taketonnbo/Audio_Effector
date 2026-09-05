using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Presentation.Views;
using VolumeValue = AudioEffector.Domain.ValueObjects.Volume;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 10バンドイコライザーの帯域ゲインスライダー、プリセット選択、カスタム保存を担当するViewModel
/// </summary>
public class EqualizerViewModel : ViewModelBase, IDisposable,
    IHandle<EqualizerPresetChangedEvent>,
    IHandle<VolumeChangedEvent>
{
    private readonly EqualizerApplicationService _equalizerService;
    private readonly AudioApplicationService _audioService;
    private readonly IEventBus _eventBus;
    private readonly ISettingsService _settingsService;
    private readonly IAudioService _legacyAudioService;

    private EqualizerPreset? _selectedPreset;
    private bool _isCustom;
    private bool _isApplyingPreset;
    private double _volume;
    private bool _disposed;

    /// <summary>
    /// 10バンドのゲインスライダーViewModelコレクション
    /// </summary>
    public ObservableCollection<BandViewModel> Bands { get; } = new();

    /// <summary>
    /// 利用可能なプリセットコレクション
    /// </summary>
    public ObservableCollection<EqualizerPreset> Presets { get; } = new();

    /// <summary>
    /// 選択中のプリセット
    /// </summary>
    public EqualizerPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value) && value != null)
            {
                _ = ApplyPresetAsync(value);
                var settings = _settingsService.LoadSettings();
                settings.LastUsedEffectPreset = value.Name;
                _settingsService.SaveSettings(settings);
            }
        }
    }

    /// <summary>
    /// カスタム設定かどうか
    /// </summary>
    public bool IsCustom
    {
        get => _isCustom;
        set => SetProperty(ref _isCustom, value);
    }

    /// <summary>
    /// 音量値（0.0〜1.0）
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            double clampedValue = Math.Clamp(value, VolumeValue.MIN_VOLUME, VolumeValue.MAX_VOLUME);
            if (SetProperty(ref _volume, clampedValue))
            {
                _ = _audioService.SetVolumeAsync(VolumeValue.FromFloat((float)clampedValue));
                var settings = _settingsService.LoadSettings();
                settings.Volume = (float)clampedValue;
                _settingsService.SaveSettings(settings);
            }
        }
    }

    /// <summary>
    /// プリセット保存コマンド
    /// </summary>
    public ICommand SavePresetCommand { get; }

    /// <summary>
    /// 選択中のカスタムプリセット削除コマンド
    /// </summary>
    public ICommand DeletePresetCommand { get; }

    /// <summary>
    /// フラットリセットコマンド
    /// </summary>
    public ICommand ResetPresetCommand { get; }

    /// <summary>
    /// 音量を5%上げるコマンド
    /// </summary>
    public ICommand IncreaseVolumeCommand { get; }

    /// <summary>
    /// 音量を5%下げるコマンド
    /// </summary>
    public ICommand DecreaseVolumeCommand { get; }

    /// <summary>
    /// EqualizerViewModelを初期化します
    /// </summary>
    /// <param name="equalizerService">イコライザーアプリケーションサービス</param>
    /// <param name="audioService">オーディオアプリケーションサービス</param>
    /// <param name="eventBus">イベントバス</param>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="legacyAudioService">移行期間中の既存再生サービス</param>
    public EqualizerViewModel(
        EqualizerApplicationService equalizerService,
        AudioApplicationService audioService,
        IEventBus eventBus,
        ISettingsService settingsService,
        IAudioService legacyAudioService)
    {
        _equalizerService = equalizerService ?? throw new ArgumentNullException(nameof(equalizerService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _legacyAudioService = legacyAudioService ?? throw new ArgumentNullException(nameof(legacyAudioService));
        _volume = settingsService.LoadSettings().Volume;

        // 10バンドの初期化
        for (int i = 0; i < EqualizerPreset.STANDARD_10_BAND_FREQUENCIES.Length; i++)
        {
            int bandIndex = i;
            float freq = EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[i];
            var bandVm = new BandViewModel
            {
                Index = bandIndex,
                Frequency = freq,
                Gain = 0.0f
            };
            bandVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(BandViewModel.Gain))
                {
                    if (_isApplyingPreset)
                    {
                        return;
                    }

                    _ = _equalizerService.UpdateBandGainAsync(bandIndex, Gain.FromDecibels(bandVm.Gain));
                    IsCustom = true;
                }
            };
            Bands.Add(bandVm);
        }

        ResetPresetCommand = new RelayCommand(async _ =>
        {
            var flat = EqualizerPreset.CreateFlat();
            await ApplyPresetAsync(flat);
            SelectedPreset = Presets.FirstOrDefault(p => !p.IsCustom && p.Name.Contains("Flat", StringComparison.OrdinalIgnoreCase));
        });

        SavePresetCommand = new RelayCommand(async name => await SavePresetAsync(name as string));
        DeletePresetCommand = new RelayCommand(async _ => await DeleteSelectedPresetAsync());
        IncreaseVolumeCommand = new RelayCommand(_ => Volume += 0.05);
        DecreaseVolumeCommand = new RelayCommand(_ => Volume -= 0.05);

        _eventBus.Subscribe<EqualizerPresetChangedEvent>(HandleAsync);
        _eventBus.Subscribe<VolumeChangedEvent>(HandleAsync);
        _legacyAudioService.VolumeChanged += OnLegacyVolumeChanged;
        _ = LoadPresetsAsync();
    }

    /// <summary>
    /// プリセット一覧を読み込みます
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task LoadPresetsAsync()
    {
        var presets = await _equalizerService.GetPresetsAsync();
        string? lastUsedPreset = _settingsService.LoadSettings().LastUsedEffectPreset;
        RunOnUiThread(() =>
        {
            Presets.Clear();
            foreach (var p in presets)
            {
                Presets.Add(p);
            }

            SelectedPreset = Presets.FirstOrDefault(p => p.Name == lastUsedPreset) ?? Presets.FirstOrDefault();
        });
    }

    private async Task ApplyPresetAsync(EqualizerPreset preset)
    {
        await _equalizerService.ApplyPresetAsync(preset);
        RunOnUiThread(() =>
        {
            _isApplyingPreset = true;
            try
            {
                for (int i = 0; i < Math.Min(Bands.Count, preset.Bands.Count); i++)
                {
                    Bands[i].Gain = preset.Bands[i].Gain.Value;
                }

                IsCustom = preset.IsCustom;
            }
            finally
            {
                _isApplyingPreset = false;
            }
        });
    }

    private async Task SavePresetAsync(string? requestedName)
    {
        string? name = requestedName;
        if (string.IsNullOrWhiteSpace(name))
        {
            var inputBox = new InputBox("Enter Preset Name:", $"User Preset {DateTime.Now:MM-dd HH:mm}");
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                inputBox.Owner = System.Windows.Application.Current.MainWindow;
            }

            if (inputBox.ShowDialog() != true)
            {
                return;
            }

            name = inputBox.InputText;
        }

        name = string.IsNullOrWhiteSpace(name) ? "Untitled Preset" : name.Trim();
        var customPreset = new EqualizerPreset(
            name,
            Bands.Select((b, idx) => new FrequencyBand(
                EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[idx],
                Gain.FromDecibels(b.Gain))),
            isCustom: true);

        await _equalizerService.SaveCustomPresetAsync(customPreset);
        var existing = Presets.FirstOrDefault(p => p.IsCustom && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Presets[Presets.IndexOf(existing)] = customPreset;
        }
        else
        {
            Presets.Add(customPreset);
        }

        SelectedPreset = customPreset;
    }

    private async Task DeleteSelectedPresetAsync()
    {
        var preset = SelectedPreset;
        if (preset == null)
        {
            return;
        }

        if (!preset.IsCustom)
        {
            MessageBox.Show($"'{preset.Name}' is a default preset and cannot be deleted.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Are you sure you want to delete '{preset.Name}'?", "Delete Preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (await _equalizerService.DeleteCustomPresetAsync(preset.Name))
        {
            Presets.Remove(preset);
            SelectedPreset = Presets.FirstOrDefault();
        }
    }

    /// <summary>
    /// イコライザープリセット変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">イコライザー変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(EqualizerPresetChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        RunOnUiThread(() =>
        {
            IsCustom = domainEvent.Preset.IsCustom;
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 音量変更イベントを受信してスライダー表示を更新します。
    /// </summary>
    /// <param name="domainEvent">音量変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(VolumeChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        UpdateVolumeFromService(domainEvent.Volume);
        return Task.CompletedTask;
    }

    private void OnLegacyVolumeChanged(float volume)
    {
        UpdateVolumeFromService(volume);
    }

    private void UpdateVolumeFromService(double volume)
    {
        void UpdateVolume()
        {
            _volume = volume;
            OnPropertyChanged(nameof(Volume));
        }

        RunOnUiThread(UpdateVolume);
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    /// <summary>
    /// イベント購読を解除します。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _legacyAudioService.VolumeChanged -= OnLegacyVolumeChanged;
        _eventBus.Unsubscribe<EqualizerPresetChangedEvent>(HandleAsync);
        _eventBus.Unsubscribe<VolumeChangedEvent>(HandleAsync);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
