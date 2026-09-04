using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 10バンドイコライザーの帯域ゲインスライダー、プリセット選択、カスタム保存、
/// およびスペクトラムアナライザーのリアルタイム解析・描画を担当するViewModel
/// </summary>
public class EqualizerViewModel : ViewModelBase, IHandle<EqualizerPresetChangedEvent>
{
    private readonly EqualizerApplicationService? _equalizerService;
    private readonly IEventBus? _eventBus;

    #region Spectrum Constants (.editorconfig UPPER_SNAKE_CASE)

    /// <summary>スペクトラムアナライザーのバー数</summary>
    public const int SPECTRUM_BAR_COUNT = 64;

    /// <summary>スペクトラムアナライザー: 低音域（〜250Hz）のスケーリング係数</summary>
    public const double SPECTRUM_BASS_SCALE = 0.55;

    /// <summary>スペクトラムアナライザー: 中音域（250Hz〜2.5kHz）のスケーリング係数</summary>
    public const double SPECTRUM_MID_SCALE = 0.90;

    /// <summary>スペクトラムアナライザー: 高音域（2.5kHz〜18kHz）のスケーリング係数</summary>
    public const double SPECTRUM_TREBLE_SCALE = 2.90;

    /// <summary>スペクトラムアナライザー: 高音域のオクターブ当たりdB補正係数</summary>
    public const double SPECTRUM_TREBLE_TILT_DB = 8.5;

    /// <summary>スペクトラムアナライザー: 全体の感度係数</summary>
    public const double SPECTRUM_SENSITIVITY = 1.65;

    #endregion

    #region Private Fields

    private readonly TimeSpan _spectrumUpdateInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0); // 約33ms (30fps)
    private EqualizerPreset? _selectedPreset;
    private bool _isCustom;
    private bool _isSpectrumVisible = true;
    private int _spectrumGeneration;
    private DateTime _lastSpectrumUpdateTime = DateTime.MinValue;

    private BitmapImage? _defaultSpectrumImage;
    private ImageSource? _spectrumBackgroundImage;
    private ImageSource? _spectrumBackgroundImageGray;
    private bool _isDefaultSpectrumImage = true;

    private Brush? _spectrumBarBrush;
    private Brush _spectrumBorderBrush = new SolidColorBrush(Color.FromArgb(230, 0, 229, 255));
    private Color _spectrumShadowColor = Color.FromRgb(0, 229, 255);

    #endregion

    #region Public Properties - Equalizer

    /// <summary>
    /// 10バンドのゲインスライダーViewModelコレクション
    /// </summary>
    public ObservableCollection<BandViewModel> Bands { get; } = [];

    /// <summary>
    /// 利用可能なプリセットコレクション
    /// </summary>
    public ObservableCollection<EqualizerPreset> Presets { get; } = [];

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
    /// プリセット保存コマンド（ダイアログ付き）
    /// </summary>
    public ICommand SavePresetCommand { get; }

    /// <summary>
    /// プリセット削除コマンド
    /// </summary>
    public ICommand DeletePresetCommand { get; }

    /// <summary>
    /// プリセットリセットコマンド
    /// </summary>
    public ICommand ResetPresetCommand { get; }

    #endregion

    #region Public Properties - Spectrum Analyzer

    /// <summary>
    /// スペクトラムアナライザーが表示されているかどうかを示す値を取得または設定します
    /// </summary>
    public bool IsSpectrumVisible
    {
        get => _isSpectrumVisible;
        set => SetProperty(ref _isSpectrumVisible, value);
    }

    /// <summary>
    /// スペクトラムアナライザー表示へ切り替えるコマンドを取得します
    /// </summary>
    public ICommand SwitchToSpectrumCommand { get; }

    /// <summary>
    /// スペクトラムアナライザーの表示/非表示を切り替えるコマンドを取得します
    /// </summary>
    public ICommand ToggleSpectrumCommand { get; }

    /// <summary>
    /// スペクトラムアナライザーの各周波数バーのViewModelコレクションを取得します
    /// </summary>
    public ObservableCollection<SpectrumBarItem> SpectrumValues { get; } = [];

    /// <summary>
    /// スペクトラムアナライザーの背景画像
    /// </summary>
    public ImageSource? SpectrumBackgroundImage
    {
        get => _spectrumBackgroundImage;
        set => SetProperty(ref _spectrumBackgroundImage, value);
    }

    /// <summary>
    /// スペクトラムアナライザーのグレースケール背景画像
    /// </summary>
    public ImageSource? SpectrumBackgroundImageGray
    {
        get => _spectrumBackgroundImageGray;
        set => SetProperty(ref _spectrumBackgroundImageGray, value);
    }

    /// <summary>
    /// 背景画像がデフォルト画像かどうか
    /// </summary>
    public bool IsDefaultSpectrumImage
    {
        get => _isDefaultSpectrumImage;
        set => SetProperty(ref _isDefaultSpectrumImage, value);
    }

    /// <summary>
    /// スペクトラムバーのブラシ
    /// </summary>
    public Brush SpectrumBarBrush
    {
        get
        {
            if (_spectrumBarBrush == null)
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 1),
                    EndPoint = new Point(0, 0)
                };
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(50, 0, 229, 255), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(180, 0, 229, 255), 0.6));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0));
                brush.Freeze();
                _spectrumBarBrush = brush;
            }
            return _spectrumBarBrush;
        }
        set => SetProperty(ref _spectrumBarBrush, value);
    }

    /// <summary>
    /// スペクトラムバーのボーダーブラシ
    /// </summary>
    public Brush SpectrumBorderBrush
    {
        get => _spectrumBorderBrush;
        set => SetProperty(ref _spectrumBorderBrush, value);
    }

    /// <summary>
    /// スペクトラムバーのシャドウカラー
    /// </summary>
    public Color SpectrumShadowColor
    {
        get => _spectrumShadowColor;
        set => SetProperty(ref _spectrumShadowColor, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// EqualizerViewModelを初期化します
    /// </summary>
    /// <param name="equalizerService">イコライザーアプリケーションサービス（null許容）</param>
    /// <param name="eventBus">イベントバス（null許容）</param>
    public EqualizerViewModel(
        EqualizerApplicationService? equalizerService = null,
        IEventBus? eventBus = null)
    {
        _equalizerService = equalizerService;
        _eventBus = eventBus;

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
                    if (_equalizerService != null)
                    {
                        _ = _equalizerService.UpdateBandGainAsync(bandIndex, Gain.FromDecibels(bandVm.Gain));
                    }
                    IsCustom = true;
                }
            };
            Bands.Add(bandVm);
        }

        // スペクトラムバー初期化 (64本)
        for (int i = 0; i < SPECTRUM_BAR_COUNT; i++)
        {
            SpectrumValues.Add(new SpectrumBarItem { Value = 0 });
        }

        // デフォルト背景画像読み込み
        LoadDefaultSpectrumImage();

        // コマンド初期化
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
                if (_equalizerService != null)
                {
                    await _equalizerService.SaveCustomPresetAsync(customPreset);
                }
                Presets.Add(customPreset);
                SelectedPreset = customPreset;
            }
        });

        SavePresetCommand = new RelayCommand(SavePreset);
        DeletePresetCommand = new RelayCommand(DeletePreset);
        ResetPresetCommand = new RelayCommand(Reset);
        SwitchToSpectrumCommand = new RelayCommand(_ => IsSpectrumVisible = true);
        ToggleSpectrumCommand = new RelayCommand(_ => IsSpectrumVisible = !IsSpectrumVisible);

        _eventBus?.Subscribe<EqualizerPresetChangedEvent>(HandleAsync);
        _ = LoadPresetsAsync();
    }

    #endregion

    #region Preset Methods

    /// <summary>
    /// プリセット一覧を読み込みます
    /// </summary>
    /// <returns>非同期タスク</returns>
    public async Task LoadPresetsAsync()
    {
        if (_equalizerService == null) return;
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
        if (_equalizerService != null)
        {
            await _equalizerService.ApplyPresetAsync(preset);
        }
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < Math.Min(Bands.Count, preset.Bands.Count); i++)
            {
                Bands[i].Gain = preset.Bands[i].Gain.Value;
            }

            IsCustom = preset.IsCustom;
        });
    }

    private void SavePreset(object? obj)
    {
        try
        {
            var inputBox = new Views.InputBox("Enter Preset Name:", $"User Preset {DateTime.Now:MM-dd HH:mm}");
            if (inputBox.ShowDialog() == true)
            {
                string name = inputBox.InputText;
                if (string.IsNullOrWhiteSpace(name)) name = "Untitled Preset";

                var newPreset = new EqualizerPreset(
                    name,
                    Bands.Select((b, idx) => new FrequencyBand(EqualizerPreset.STANDARD_10_BAND_FREQUENCIES[idx], Gain.FromDecibels(b.Gain))),
                    isCustom: true);

                Presets.Add(newPreset);
                _equalizerService?.SavePresets(Presets.ToList());
                SelectedPreset = newPreset;
                MessageBox.Show("プリセットを保存しました。\nPreset Saved.", "保存完了");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving preset: {ex.Message}", "Error");
        }
    }

    private void DeletePreset(object? obj)
    {
        if (SelectedPreset != null && Presets.Contains(SelectedPreset))
        {
            var defaultPresets = new[] { "フラット (Flat)", "ロック (Rock)", "ポップ (Pop)" };
            if (defaultPresets.Contains(SelectedPreset.Name) || SelectedPreset.Name.Contains("Flat") || SelectedPreset.Name.Contains("Rock") || SelectedPreset.Name.Contains("Pop"))
            {
                MessageBox.Show($"'{SelectedPreset.Name}' is a default preset and cannot be deleted.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Are you sure you want to delete '{SelectedPreset.Name}'?", "Delete Preset", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    Presets.Remove(SelectedPreset);
                    _equalizerService?.SavePresets(Presets.ToList());
                    SelectedPreset = Presets.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving presets: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void Reset(object? obj)
    {
        foreach (var band in Bands)
        {
            band.Gain = 0;
        }
        SelectedPreset = Presets.FirstOrDefault(p => p.Name.Contains("Flat"));
    }

    #endregion

    #region Spectrum Analyzer Methods

    private void LoadDefaultSpectrumImage()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/Images/default_spectrum_bg.png");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _defaultSpectrumImage = bmp;
            SpectrumBackgroundImage = bmp;
            IsDefaultSpectrumImage = true;
        }
        catch
        {
            // Resource not found in unit tests or headless environment
        }
    }

    /// <summary>
    /// スペクトラムアナライザーのバーを即座にリセットします
    /// </summary>
    public void ResetSpectrum()
    {
        Interlocked.Increment(ref _spectrumGeneration);
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            foreach (var item in SpectrumValues)
            {
                item.Value = 0;
                item.PeakValue = 0;
                item.PeakHoldCount = 0;
            }
        });
    }

    /// <summary>
    /// 再生中の楽曲アート画像に基づいてスペクトラム背景およびバーの配色を更新します
    /// </summary>
    /// <param name="bitmap">アルバムアート画像（null時はデフォルト画像に復元）</param>
    public void UpdateSpectrumArt(BitmapSource? bitmap)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (bitmap != null)
            {
                SpectrumBackgroundImage = bitmap;

                var grayImage = new FormatConvertedBitmap();
                grayImage.BeginInit();
                grayImage.Source = bitmap;
                grayImage.DestinationFormat = PixelFormats.Gray8;
                grayImage.EndInit();
                grayImage.Freeze();

                SpectrumBackgroundImageGray = grayImage;
                IsDefaultSpectrumImage = false;
                UpdateSpectrumBrush(bitmap);
            }
            else
            {
                ResetSpectrumArt();
            }
        });
    }

    /// <summary>
    /// スペクトラムアナライザーの背景およびバー色をデフォルトに戻します
    /// </summary>
    public void ResetSpectrumArt()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            SpectrumBackgroundImage = _defaultSpectrumImage;
            SpectrumBackgroundImageGray = null;

            var borderColor = Color.FromRgb(204, 249, 255);
            var solidBorderBrush = new SolidColorBrush(borderColor);
            solidBorderBrush.Freeze();
            SpectrumBorderBrush = solidBorderBrush;

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 229, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(230, 0, 229, 255), 0.6));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0));
            brush.Freeze();

            SpectrumBarBrush = brush;
            SpectrumShadowColor = Color.FromRgb(0, 229, 255);
            IsDefaultSpectrumImage = true;
        });
    }

    /// <summary>
    /// FFT計算結果を受け取り、周波数バーの値を更新します
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">FFT結果引数</param>
    public void OnFftCalculated(object? sender, FftEventArgs e)
    {
        if (!IsSpectrumVisible) return;
        if (DateTime.Now - _lastSpectrumUpdateTime < _spectrumUpdateInterval) return;
        _lastSpectrumUpdateTime = DateTime.Now;

        int currentGen = _spectrumGeneration;
        int barCount = SPECTRUM_BAR_COUNT;
        var newValues = new double[barCount];

        double minFreq = 30;
        double maxFreq = 18000;
        double logMin = Math.Log10(minFreq);
        double logMax = Math.Log10(maxFreq);
        double logStep = (logMax - logMin) / barCount;

        for (int i = 0; i < barCount; i++)
        {
            double fStart = Math.Pow(10, logMin + i * logStep);
            double fEnd = Math.Pow(10, logMin + (i + 1) * logStep);

            int iStart = (int)(fStart * 512 / 22050);
            int iEnd = (int)(fEnd * 512 / 22050);

            if (iStart < 0) iStart = 0;
            if (iEnd >= 512) iEnd = 511;
            if (iEnd < iStart) iEnd = iStart;

            double sum = 0;
            int count = 0;

            for (int index = iStart; index <= iEnd; index++)
            {
                if (index < 1) continue;
                if (index < e.Result.Length)
                {
                    var c = e.Result[index];
                    double mag = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                    sum += mag;
                    count++;
                }
            }

            double avg = count > 0 ? sum / count : 0;
            double db = (avg > 1e-6) ? 20 * Math.Log10(avg) : -120;
            double centerFreq = Math.Sqrt(fStart * fEnd);

            double trebleTilt = (centerFreq > 250) ? Math.Log2(centerFreq / 250.0) * SPECTRUM_TREBLE_TILT_DB : 0.0;
            double adjustedDb = db + 65 + trebleTilt;
            double val = Math.Max(0, adjustedDb) * SPECTRUM_SENSITIVITY;

            if (centerFreq < 250)
            {
                double bassRatio = Math.Min(1.0, centerFreq / 250.0);
                double bassMultiplier = 0.45 + (SPECTRUM_BASS_SCALE - 0.45) * bassRatio;
                val *= bassMultiplier;
            }
            else if (centerFreq < 2500)
            {
                val *= SPECTRUM_MID_SCALE;
            }
            else
            {
                double trebleRatio = Math.Min(1.0, (centerFreq - 2500.0) / 12000.0);
                double trebleMultiplier = SPECTRUM_MID_SCALE + (SPECTRUM_TREBLE_SCALE - SPECTRUM_MID_SCALE) * Math.Pow(trebleRatio, 0.85);
                val *= trebleMultiplier;
            }

            if (double.IsNaN(val) || double.IsInfinity(val)) val = 0;
            newValues[i] = val;
        }

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (currentGen != _spectrumGeneration) return;

            int targetCount = SPECTRUM_BAR_COUNT;
            int currentCount = SpectrumValues.Count;

            if (currentCount < targetCount)
            {
                for (int i = currentCount; i < targetCount; i++)
                {
                    SpectrumValues.Add(new SpectrumBarItem { Value = 0 });
                }
            }
            else if (currentCount > targetCount)
            {
                for (int i = currentCount; i > targetCount; i--)
                {
                    SpectrumValues.RemoveAt(SpectrumValues.Count - 1);
                }
            }

            for (int i = 0; i < targetCount; i++)
            {
                var item = SpectrumValues[i];
                double current = item.Value;
                double target = Math.Min(78, newValues[i]);

                if (target > current)
                {
                    item.Value = current + (target - current) * 0.45;
                }
                else
                {
                    item.Value = current - (current - target) * 0.075;
                }

                if (item.Value >= item.PeakValue)
                {
                    item.PeakValue = item.Value;
                    item.PeakHoldCount = 14;
                }
                else
                {
                    if (item.PeakHoldCount > 0)
                    {
                        item.PeakHoldCount--;
                    }
                    else
                    {
                        item.PeakValue = Math.Max(item.Value, item.PeakValue - 1.3);
                    }
                }
            }
        });
    }

    private void UpdateSpectrumBrush(BitmapSource bitmap)
    {
        try
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();

            var resized = new TransformedBitmap(converted, new ScaleTransform(100.0 / converted.PixelWidth, 100.0 / converted.PixelHeight));
            resized.Freeze();
            int width = resized.PixelWidth;
            int height = resized.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            resized.CopyPixels(pixels, stride, 0);

            long[] bucketR = new long[36];
            long[] bucketG = new long[36];
            long[] bucketB = new long[36];
            int[] bucketCount = new int[36];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                Color c = Color.FromRgb(r, g, b);
                ColorToHsv(c, out double h, out double s, out double v);

                if (s < 0.2 || v < 0.2) continue;

                int bucketIndex = (int)(h / 10.0);
                if (bucketIndex >= 36) bucketIndex = 35;

                bucketR[bucketIndex] += r;
                bucketG[bucketIndex] += g;
                bucketB[bucketIndex] += b;
                bucketCount[bucketIndex]++;
            }

            int bestBucket = -1;
            int maxCount = 0;
            for (int i = 0; i < 36; i++)
            {
                if (bucketCount[i] > maxCount)
                {
                    maxCount = bucketCount[i];
                    bestBucket = i;
                }
            }

            if (bestBucket != -1 && maxCount > 0)
            {
                byte avgR = (byte)(bucketR[bestBucket] / maxCount);
                byte avgG = (byte)(bucketG[bestBucket] / maxCount);
                byte avgB = (byte)(bucketB[bestBucket] / maxCount);

                Color dominantColor = Color.FromRgb(avgR, avgG, avgB);
                ColorToHsv(dominantColor, out double dh, out _, out _);

                double fh = dh;
                Color topColor = HsvToColor(fh, 0.40, 1.0);
                Color midColor = HsvToColor(fh, 0.90, 1.0);
                Color botColor = HsvToColor(fh, 0.95, 0.50);

                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 1),
                    EndPoint = new Point(0, 0)
                };
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, botColor.R, botColor.G, botColor.B), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(230, midColor.R, midColor.G, midColor.B), 0.6));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, topColor.R, topColor.G, topColor.B), 1.0));
                brush.Freeze();

                var borderColor = HsvToColor(fh, 0.25, 1.0);
                borderColor.A = 255;
                var solidBorderBrush = new SolidColorBrush(borderColor);
                solidBorderBrush.Freeze();

                SpectrumBarBrush = brush;
                SpectrumShadowColor = HsvToColor(fh, 1.0, 1.0);
                SpectrumBorderBrush = solidBorderBrush;
            }
            else
            {
                ResetSpectrumArt();
            }
        }
        catch
        {
            ResetSpectrumArt();
        }
    }

    private static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        value = max;
        saturation = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0) hue += 360;
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60.0 % 2) - 1));
        double m = value - c;

        double r = 0, g = 0, b = 0;
        if (hue < 60)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (hue < 120)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (hue < 180)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (hue < 240)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (hue < 300)
        {
            r = x;
            g = 0;
            b = c;
        }
        else
        {
            r = c;
            g = 0;
            b = x;
        }

        return Color.FromRgb(
            (byte)Math.Clamp((r + m) * 255, 0, 255),
            (byte)Math.Clamp((g + m) * 255, 0, 255),
            (byte)Math.Clamp((b + m) * 255, 0, 255));
    }

    #endregion

    #region Event Handler

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

    #endregion
}
