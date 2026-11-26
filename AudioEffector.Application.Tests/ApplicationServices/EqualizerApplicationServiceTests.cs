using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AudioEffector.Application.Tests.ApplicationServices;

/// <summary>
/// EqualizerApplicationServiceのイコライザー適用、バンドゲイン調整、カスタムプリセット永続化、イベント発行を検証するテストクラス
/// </summary>
public sealed class EqualizerApplicationServiceTests
{
    private static EqualizerPreset CreateSamplePreset(string name = "MyPreset", bool isCustom = true)
    {
        var bands = EqualizerPreset.STANDARD_10_BAND_FREQUENCIES
            .Select(f => new FrequencyBand(f, Gain.FromDecibels(2.0f)))
            .ToList();
        return new EqualizerPreset(name, bands, isCustom);
    }

    /// <summary>
    /// コンストラクタ引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EqualizerApplicationService(null!, mockSettings.Object, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new EqualizerApplicationService(mockEngine.Object, null!, mockEventBus.Object));
        Assert.Throws<ArgumentNullException>(() => new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, null!));
    }

    /// <summary>
    /// ApplyPresetAsync呼び出し時、オーディオエンジンへ全バンドゲインが設定され、EqualizerPresetChangedEventが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task ApplyPresetAsync_プリセット適用_DSPエンジンへ全ゲインが設定されEqualizerPresetChangedEventが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);

        var preset = CreateSamplePreset("RockDrive");

        // Act
        await sut.ApplyPresetAsync(preset);

        // Assert
        Assert.Equal(preset, sut.CurrentPreset);
        mockEngine.Verify(e => e.SetEqualizerAllGainsAsync(It.Is<float[]>(g => g.Length == 10 && g.All(v => Math.Abs(v - 2.0f) < 0.01f)), It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.Is<EqualizerPresetChangedEvent>(ev => ev.Preset == preset), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ApplyPresetAsyncにnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public async Task ApplyPresetAsync_nullプリセット指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ApplyPresetAsync(null!));
    }

    /// <summary>
    /// UpdateBandGainAsync呼び出し時、オーディオエンジンへ単一バンドのゲインが設定され、EqualizerPresetChangedEventが発行されるかを検証します。
    /// </summary>
    [Fact]
    public async Task UpdateBandGainAsync_特定バンドゲイン変更_DSPエンジンへ単一ゲインが設定されイベントが発行されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();
        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);

        var gain = Gain.FromDecibels(4.5f);

        // Act
        await sut.UpdateBandGainAsync(2, gain);

        // Assert
        Assert.Equal(4.5f, sut.CurrentPreset.Bands[2].Gain.Value);
        mockEngine.Verify(e => e.SetEqualizerBandGainAsync(2, 4.5f, It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(b => b.PublishAsync(It.IsAny<EqualizerPresetChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// GetPresetsAsync呼び出し時、9つの標準プリセットと設定リポジトリから取得したカスタムプリセットが結合されて返るかを検証します。
    /// </summary>
    [Fact]
    public async Task GetPresetsAsync_呼び出し時_標準9プリセットとリポジトリのカスタムプリセットが結合されて返ること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();

        string customJson = """
            [
              {
                "Name": "CustomBass",
                "GainsDb": [5, 4, 3, 2, 1, 0, 0, 0, 0, 0]
              }
            ]
            """;
        mockSettings.Setup(r => r.GetValueAsync("Equalizer_CustomPresets", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customJson);

        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);

        // Act
        var presets = await sut.GetPresetsAsync();

        // Assert
        Assert.Equal(10, presets.Count); // 9標準 + 1カスタム
        Assert.Contains(presets, p => p.Name == "Flat");
        Assert.Contains(presets, p => p.Name == "Rock");
        Assert.Contains(presets, p => p.Name == "CustomBass" && p.IsCustom);
    }

    /// <summary>同じ設定ストアを共有する2サービスの並行保存・削除で別名のプリセットが残ることを検証します</summary>
    [Fact]
    public async Task カスタムプリセット_別インスタンスの並行保存と削除で他の名前の変更が残る()
    {
        // Arrange
        var sharedStore = new SharedInMemorySettingsStore();
        var firstRepo = new InMemorySettingsRepository(sharedStore);
        var secondRepo = new InMemorySettingsRepository(sharedStore);
        var first = new EqualizerApplicationService(Mock.Of<IAudioEngine>(), firstRepo, new InMemoryEventBus());
        var second = new EqualizerApplicationService(Mock.Of<IAudioEngine>(), secondRepo, new InMemoryEventBus());

        // Act
        await Task.WhenAll(first.SaveCustomPresetAsync(CreateSamplePreset("A")), second.SaveCustomPresetAsync(CreateSamplePreset("B")));
        var both = await first.GetPresetsAsync();
        var deletion = second.DeleteCustomPresetAsync("a");
        await Task.WhenAll(deletion, first.SaveCustomPresetAsync(CreateSamplePreset("C")));

        // Assert
        Assert.Equal(new[] { "A", "B" }, both.Where(p => p.IsCustom).Select(p => p.Name).OrderBy(n => n));
        Assert.True(await deletion);
        Assert.False(await second.DeleteCustomPresetAsync("a"));
        var remaining = await second.GetPresetsAsync();
        Assert.Equal(new[] { "B", "C" }, remaining.Where(p => p.IsCustom).Select(p => p.Name).OrderBy(n => n));
    }

    /// <summary>
    /// 新規カスタムプリセットをSaveCustomPresetAsyncで保存した際、リポジトリへJSONシリアライズされて保存されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveCustomPresetAsync_新規カスタムプリセット_リポジトリへJSONシリアライズして保存されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();

        var readSaved = CapturePresetUpdates(mockSettings, null);

        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);
        var custom = CreateSamplePreset("MyStudio");

        // Act
        await sut.SaveCustomPresetAsync(custom);

        // Assert
        Assert.Contains("MyStudio", readSaved());
    }

    /// <summary>
    /// 既存の同名カスタムプリセットが存在する場合、SaveCustomPresetAsyncで上書き保存されるかを検証します。
    /// </summary>
    [Fact]
    public async Task SaveCustomPresetAsync_既存同名カスタムプリセット_上書き更新されて保存されること()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();

        string existingJson = """
            [
              {
                "Name": "MyStudio",
                "GainsDb": [1, 1, 1, 1, 1, 1, 1, 1, 1, 1]
              }
            ]
            """;
        var readSaved = CapturePresetUpdates(mockSettings, existingJson);

        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);
        var updated = CreateSamplePreset("MyStudio"); // 全ゲイン 2.0f

        // Act
        await sut.SaveCustomPresetAsync(updated);

        // Assert
        using var document = JsonDocument.Parse(readSaved()!);
        var stored = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("MyStudio", stored.GetProperty("Name").GetString());
        Assert.All(stored.GetProperty("GainsDb").EnumerateArray(), gain => Assert.Equal(2f, gain.GetSingle()));
    }

    /// <summary>
    /// 登録済みのカスタムプリセットをDeleteCustomPresetAsyncで削除した際、リポジトリが更新されtrueが返るかを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteCustomPresetAsync_存在するプリセット名_リポジトリから削除されtrueを返すこと()
    {
        // Arrange
        var mockEngine = new Mock<IAudioEngine>();
        var mockSettings = new Mock<ISettingsRepository>();
        var mockEventBus = new Mock<IEventBus>();

        string existingJson = """
            [
              {
                "Name": "ToDelete",
                "GainsDb": [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
              }
            ]
            """;
        var readSaved = CapturePresetUpdates(mockSettings, existingJson);

        var sut = new EqualizerApplicationService(mockEngine.Object, mockSettings.Object, mockEventBus.Object);

        // Act
        bool result = await sut.DeleteCustomPresetAsync("ToDelete");

        // Assert
        Assert.True(result);
        Assert.Equal("[]", readSaved());
    }

    private static Func<string?> CapturePresetUpdates(Mock<ISettingsRepository> mockSettings, string? value)
    {
        mockSettings.Setup(r => r.UpdateValueAsync("Equalizer_CustomPresets", It.IsAny<Func<string?, string?>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<string?, string?>, CancellationToken>((_, update, _) =>
            {
                var previous = value;
                value = update(previous);
                return Task.FromResult(value != previous);
            });
        return () => value;
    }

    private sealed class SharedInMemorySettingsStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? GetValue(string key, string? defaultValue)
        {
            lock (_sync)
            {
                return _values.TryGetValue(key, out var val) ? val : defaultValue;
            }
        }

        public void SetValue(string key, string? value)
        {
            lock (_sync)
            {
                if (value is null)
                {
                    _values.Remove(key);
                }
                else
                {
                    _values[key] = value;
                }
            }
        }

        public Dictionary<string, string> GetAll()
        {
            lock (_sync)
            {
                return new Dictionary<string, string>(_values, StringComparer.Ordinal);
            }
        }

        public void Remove(string key)
        {
            lock (_sync)
            {
                _values.Remove(key);
            }
        }

        public bool UpdateValue(string key, Func<string?, string?> update)
        {
            lock (_sync)
            {
                _values.TryGetValue(key, out var previous);
                var updated = update(previous);
                if (string.Equals(previous, updated, StringComparison.Ordinal))
                {
                    return false;
                }

                if (updated is null)
                {
                    _values.Remove(key);
                }
                else
                {
                    _values[key] = updated;
                }

                return true;
            }
        }
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly SharedInMemorySettingsStore _store;

        public InMemorySettingsRepository(SharedInMemorySettingsStore store)
        {
            _store = store;
        }

        public Task<string?> GetValueAsync(string key, string? defaultValue = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.GetValue(key, defaultValue));

        public Task SetValueAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            _store.SetValue(key, value);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(_store.GetAll());

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateValueAsync(string key, Func<string?, string?> update, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.UpdateValue(key, update));
    }
}
