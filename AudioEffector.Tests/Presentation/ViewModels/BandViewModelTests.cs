using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="BandViewModel"/> のゲイン設定、変更通知、コールバック呼び出しおよび表示ラベルを検証するテストクラス。
/// </summary>
public class BandViewModelTests
{
    /// <summary>
    /// ゲイン値が0.01f超変化した際、PropertyChangedが発火しOnGainChangedコールバックが呼ばれることを検証します。
    /// </summary>
    [Fact]
    public void Gain_値が変化した時_PropertyChangedが発火しOnGainChangedが呼ばれる()
    {
        // Arrange
        var sut = new BandViewModel
        {
            Index = 3,
            Frequency = 1000f,
            Gain = 0f
        };
        string? changedProp = null;
        sut.PropertyChanged += (s, e) => changedProp = e.PropertyName;

        int callbackIndex = -1;
        float callbackGain = 0f;
        sut.OnGainChanged = (idx, g) =>
        {
            callbackIndex = idx;
            callbackGain = g;
        };

        // Act
        sut.Gain = 3.5f;

        // Assert
        Assert.Equal(3.5f, sut.Gain);
        Assert.Equal(nameof(BandViewModel.Gain), changedProp);
        Assert.Equal(3, callbackIndex);
        Assert.Equal(3.5f, callbackGain);
    }

    /// <summary>
    /// ゲイン値の変化量が0.01f以下の場合、イベントもコールバックも呼ばれないことを検証します。
    /// </summary>
    [Fact]
    public void Gain_変化量が0点01f以下の場合_イベントもコールバックも呼ばれない()
    {
        // Arrange
        var sut = new BandViewModel
        {
            Index = 0,
            Gain = 1.0f
        };
        bool eventFired = false;
        sut.PropertyChanged += (s, e) => eventFired = true;

        bool callbackFired = false;
        sut.OnGainChanged = (_, _) => callbackFired = true;

        // Act (0.005f の変化)
        sut.Gain = 1.005f;

        // Assert
        Assert.False(eventFired);
        Assert.False(callbackFired);
    }

    /// <summary>
    /// 1000Hz未満の周波数の場合、数値をそのままラベルとして返すことを検証します。
    /// </summary>
    /// <param name="frequency">周波数（Hz）</param>
    /// <param name="expectedLabel">期待されるラベル文字列</param>
    [Theory]
    [InlineData(60f, "60")]
    [InlineData(250f, "250")]
    [InlineData(500f, "500")]
    public void Label_1000Hz未満_周波数の数値をそのまま返す(float frequency, string expectedLabel)
    {
        // Arrange
        var sut = new BandViewModel { Frequency = frequency };

        // Act
        var label = sut.Label;

        // Assert
        Assert.Equal(expectedLabel, label);
    }

    /// <summary>
    /// 1000Hz以上の周波数の場合、k単位の表記でラベルを返すことを検証します。
    /// </summary>
    /// <param name="frequency">周波数（Hz）</param>
    /// <param name="expectedLabel">期待されるラベル文字列</param>
    [Theory]
    [InlineData(1000f, "1k")]
    [InlineData(2500f, "2.5k")]
    [InlineData(4000f, "4k")]
    [InlineData(16000f, "16k")]
    public void Label_1000Hz以上_k単位の表記を返す(float frequency, string expectedLabel)
    {
        // Arrange
        var sut = new BandViewModel { Frequency = frequency };

        // Act
        var label = sut.Label;

        // Assert
        Assert.Equal(expectedLabel, label);
    }
}
