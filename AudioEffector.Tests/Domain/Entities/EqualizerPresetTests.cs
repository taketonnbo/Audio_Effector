using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.Entities;

public class EqualizerPresetTests
{
    /// <summary>
    /// EqualizerPreset.CreateFlatで生成した場合、10バンドすべてが0dBで非カスタムプリセットとして初期化されるかを検証します。
    /// </summary>
    [Fact]
    public void CreateFlat_デフォルト生成_10バンドすべて0dBのプリセットが生成されること()
    {
        // Arrange & Act
        var sut = EqualizerPreset.CreateFlat("Default Flat");

        // Assert
        Assert.Equal("Default Flat", sut.Name);
        Assert.Equal(10, sut.BandCount);
        Assert.False(sut.IsCustom);
        Assert.All(sut.Bands, band => Assert.Equal(0.0f, band.Gain.Value));
    }

    /// <summary>
    /// 10要素のfloat配列を指定してCreate10Bandを呼び出した場合、各バンドに指定値が反映されるかを検証します。
    /// </summary>
    [Fact]
    public void Create10Band_10要素のゲイン配列_各バンドのゲインが正しく設定されること()
    {
        // Arrange
        float[] gains = [-2.0f, -1.0f, 0.0f, 1.0f, 2.0f, 3.0f, 2.0f, 1.0f, 0.0f, -1.0f];

        // Act
        var sut = EqualizerPreset.Create10Band("Rock", gains, isCustom: true);

        // Assert
        Assert.Equal("Rock", sut.Name);
        Assert.True(sut.IsCustom);
        Assert.Equal(10, sut.BandCount);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(gains[i], sut.Bands[i].Gain.Value);
        }
    }

    /// <summary>
    /// 要素数が10個ではないゲイン配列を指定した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(11)]
    public void Create10Band_要素数が10個以外_ArgumentExceptionをスローすること(int count)
    {
        // Arrange
        var invalidGains = new float[count];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => EqualizerPreset.Create10Band("Invalid", invalidGains));
    }

    /// <summary>
    /// UpdateBandGainを呼び出した場合、指定したバンドのゲインのみが更新されるかを検証します。
    /// </summary>
    [Fact]
    public void UpdateBandGain_有効なバンドインデックス_指定バンドのゲインが更新されること()
    {
        // Arrange
        var sut = EqualizerPreset.CreateFlat();
        var newGain = Gain.FromDecibels(5.5f);

        // Act
        sut.UpdateBandGain(4, newGain);

        // Assert
        Assert.Equal(5.5f, sut.Bands[4].Gain.Value);
        Assert.Equal(0.0f, sut.Bands[3].Gain.Value); // 他のバンドは影響を受けない
    }

    /// <summary>
    /// 範囲外のバンドインデックスを指定してUpdateBandGainを呼び出した場合、ArgumentOutOfRangeExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void UpdateBandGain_範囲外インデックス_ArgumentOutOfRangeExceptionをスローすること(int invalidIndex)
    {
        // Arrange
        var sut = EqualizerPreset.CreateFlat();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.UpdateBandGain(invalidIndex, Gain.Zero));
    }

    /// <summary>
    /// Gainsプロパティを通じてリストでの値取得および一括更新が正しくバンド設定に反映されるかを検証します。
    /// </summary>
    [Fact]
    public void Gainsプロパティ_リストでの値取得と一括更新_各バンドのゲイン値と連動すること()
    {
        // Arrange
        var sut = EqualizerPreset.CreateFlat();
        var newGains = new List<float> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // Act
        sut.Gains = newGains;
        var actualGains = sut.Gains;

        // Assert
        Assert.Equal(newGains, actualGains);
        Assert.Equal(1.0f, sut.Bands[0].Gain.Value);
        Assert.Equal(10.0f, sut.Bands[9].Gain.Value);
    }

    /// <summary>
    /// 同一の名称（大文字小文字不問）を持つEqualizerPreset同士が等価と判定されるかを検証します。
    /// </summary>
    [Fact]
    public void Equals_同一名称_大文字小文字問わず等価と判定されること()
    {
        // Arrange
        var sut1 = EqualizerPreset.CreateFlat("Jazz");
        var sut2 = EqualizerPreset.CreateFlat("JAZZ");

        // Act & Assert
        Assert.True(sut1.Equals(sut2));
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
    }
}
