using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 10バンドイコライザーの帯域ゲインスライダー、プリセット選択、カスタム保存を担当するViewModel
/// </summary>
public class EqualizerViewModel : ViewModelBase, IHandle<EqualizerPresetChangedEvent>
{
    private readonly EqualizerApplicationService _equalizerService;
    private readonly IEventBus _eventBus;

    private EqualizerPreset? _selectedPreset;
    private bool _isCustom;

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
    /// フラットリセットコマンド
    /// </summary>
    public ICommand ResetFlatCommand { get; }

    /// <summary>
    /// カスタムプリセット保存コマンド
    /// </summary>
    public ICommand SaveCustomPresetCommand { get; }

    /// <summary>
    /// EqualizerViewModelを初期化します
    /// </summary>
    /// <param name="equalizerService">イコライザーアプリケーションサービス</param>
    /// <param name="eventBus">イベントバス</param>
    public EqualizerViewModel(
        EqualizerApplicationService equalizerService,
        IEventBus eventBus)
    {
        _equalizerService = equalizerService ?? throw new ArgumentNullException(nameof(equalizerService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

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
                    _ = _equalizerService.UpdateBandGainAsync(bandIndex, Gain.FromDecibels(bandVm.Gain));
                    IsCustom = true;
                }
            };
            Bands.Add(bandVm);
        }

        ResetFlatCommand = new RelayCommand(async _ =>
        {
            var flat = EqualizerPreset.CreateFlat();
            await ApplyPresetAsync(flat);
        });

        SaveCustomPresetCommand = new RelayCommand(async name =>
        {
            if (name is string n && !string.IsNullOrWhiteSpace(n))
            {
                var customPreset = new EqualizerPreset(
                    n,
                    Bands.Select((b, idx) => new FrequencyBand(EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[idx], Gain.FromDecibels(b.Gain))),
                    isCustom: true);
                await _equalizerService.SaveCustomPresetAsync(customPreset);
                Presets.Add(customPreset);
                SelectedPreset = customPreset;
            }
        });

        _eventBus.Subscribe<EqualizerPresetChangedEvent>(HandleAsync);
        _ = LoadPresetsAsync();
    }

    /// <summary>
    /// プリセット一覧を読み込みます
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task LoadPresetsAsync()
    {
        var presets = await _equalizerService.GetPresetsAsync();
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Presets.Clear();
            foreach (var p in presets)
            {
                Presets.Add(p);
            }

            SelectedPreset = Presets.FirstOrDefault();
        });
    }

    private async Task ApplyPresetAsync(EqualizerPreset preset)
    {
        await _equalizerService.ApplyPresetAsync(preset);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < Math.Min(Bands.Count, preset.Bands.Count); i++)
            {
                Bands[i].Gain = preset.Bands[i].Gain.Value;
            }

            IsCustom = preset.IsCustom;
        });
    }

    /// <summary>
    /// イコライザープリセット変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">イコライザー変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public Task HandleAsync(EqualizerPresetChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsCustom = domainEvent.Preset.IsCustom;
        });
        return Task.CompletedTask;
    }
}
