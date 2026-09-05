using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

public class VolumeTests
{
    /// <summary>
    /// 許容範囲（0.0〜1.0）内の音量値を指定した場合、そのままの値が保持されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void FromFloat_許容範囲内の音量値_指定値が設定されること(float volume)
    {
        // Arrange & Act
        var sut = Volume.FromFloat(volume);

        // Assert
        Assert.Equal(volume, sut.Value);
        Assert.False(sut.IsMuted);
    }

    /// <summary>
    /// 最小音量値（0.0）未満の値を指定した場合、MIN_VOLUME（0.0）にクランプされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(-1.0f)]
    public void FromFloat_最小値未満の値_MIN_VOLUMEにクランプされること(float volume)
    {
        // Arrange & Act
        var sut = Volume.FromFloat(volume);

        // Assert
        Assert.Equal(Volume.MIN_VOLUME, sut.Value);
    }

    /// <summary>
    /// 最大音量値（1.0）を超える値を指定した場合、MAX_VOLUME（1.0）にクランプされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(1.05f)]
    [InlineData(2.0f)]
    public void FromFloat_最大値超の値_MAX_VOLUMEにクランプされること(float volume)
    {
        // Arrange & Act
        var sut = Volume.FromFloat(volume);

        // Assert
        Assert.Equal(Volume.MAX_VOLUME, sut.Value);
    }

    /// <summary>
    /// パーセント値（0〜100）を指定して生成した場合、正しいfloat値（0.0〜1.0）に変換されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0.0f)]
    [InlineData(50, 0.5f)]
    [InlineData(100, 1.0f)]
    public void FromPercent_0から100のパーセント値_正しいfloat値に変換されること(int percent, float expectedFloat)
    {
        // Arrange & Act
        var sut = Volume.FromPercent(percent);

        // Assert
        Assert.Equal(expectedFloat, sut.Value, precision: 2);
    }

    /// <summary>
    /// ミュートが有効な場合、設定音量に関わらずEffectiveVolumeが0.0を返すかを検証します。
    /// </summary>
    [Fact]
    public void EffectiveVolume_ミュート時_0を返すこと()
    {
        // Arrange
        var sut = Volume.FromFloat(0.8f, isMuted: true);

        // Act
        var effective = sut.EffectiveVolume;

        // Assert
        Assert.Equal(0.0f, effective);
        Assert.True(sut.IsMuted);
        Assert.Equal(0.8f, sut.Value);
    }

    /// <summary>
    /// ミュートが無効な場合、EffectiveVolumeが設定音量（Value）を返すかを検証します。
    /// </summary>
    [Fact]
    public void EffectiveVolume_ミュート解除時_Valueを返すこと()
    {
        // Arrange
        var sut = Volume.FromFloat(0.65f, isMuted: false);

        // Act
        var effective = sut.EffectiveVolume;

        // Assert
        Assert.Equal(0.65f, effective);
    }

    /// <summary>
    /// WithMuteを呼び出した場合、音量値を維持したまま新しいミュート状態のVolumeインスタンスが生成されるかを検証します。
    /// </summary>
    [Fact]
    public void WithMute_ミュート状態の変更_新しいVolumeインスタンスを返すこと()
    {
        // Arrange
        var sut = Volume.FromFloat(0.7f, isMuted: false);

        // Act
        var muted = sut.WithMute(true);
        var unmuted = muted.WithMute(false);

        // Assert
        Assert.True(muted.IsMuted);
        Assert.Equal(0.7f, muted.Value);
        Assert.False(unmuted.IsMuted);
        Assert.Equal(0.7f, unmuted.Value);
    }

    /// <summary>
    /// WithValueを呼び出した場合、ミュート状態を維持したまま新しい音量値のVolumeインスタンスが生成されるかを検証します。
    /// </summary>
    [Fact]
    public void WithValue_音量値の変更_新しいVolumeインスタンスを返すこと()
    {
        // Arrange
        var sut = Volume.FromFloat(0.5f, isMuted: true);

        // Act
        var updated = sut.WithValue(0.85f);

        // Assert
        Assert.Equal(0.85f, updated.Value);
        Assert.True(updated.IsMuted);
    }

    /// <summary>
    /// Percentプロパティが四捨五入された整数パーセント（0〜100）を正しく計算するかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0.554f, 55)]
    [InlineData(0.555f, 56)]
    [InlineData(0.0f, 0)]
    [InlineData(1.0f, 100)]
    public void Percent_四捨五入計算_整数パーセント値を返すこと(float volume, int expectedPercent)
    {
        // Arrange
        var sut = Volume.FromFloat(volume);

        // Act & Assert
        Assert.Equal(expectedPercent, sut.Percent);
    }

    /// <summary>
    /// 通常時およびミュート時に応じた文字列表現（例: "75%", "Muted (75%)"）が返されるかを検証します。
    /// </summary>
    [Theory]
    [InlineData(0.75f, false, "75%")]
    [InlineData(0.75f, true, "Muted (75%)")]
    public void ToString_通常時およびミュート時_適切なフォーマット文字列を返すこと(float volume, bool isMuted, string expected)
    {
        // Arrange
        var sut = Volume.FromFloat(volume, isMuted);

        // Act
        var actual = sut.ToString();

        // Assert
        Assert.Equal(expected, actual);
    }
}
