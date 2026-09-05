using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="SpectrumBarItem"/> のプロパティおよびプロパティ変更通知の検証を行うテストクラス。
/// </summary>
public class SpectrumBarItemTests
{
    /// <summary>
    /// Valueプロパティの変更時にPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void Value_変更時_PropertyChangedイベントが発火する()
    {
        // Arrange
        var sut = new SpectrumBarItem();
        string? changedProp = null;
        sut.PropertyChanged += (s, e) => changedProp = e.PropertyName;

        // Act
        sut.Value = 42.5;

        // Assert
        Assert.Equal(42.5, sut.Value);
        Assert.Equal(nameof(SpectrumBarItem.Value), changedProp);
    }

    /// <summary>
    /// PeakValueプロパティの変更時にPropertyChangedイベントが発火することを検証します。
    /// </summary>
    [Fact]
    public void PeakValue_変更時_PropertyChangedイベントが発火する()
    {
        // Arrange
        var sut = new SpectrumBarItem();
        string? changedProp = null;
        sut.PropertyChanged += (s, e) => changedProp = e.PropertyName;

        // Act
        sut.PeakValue = 65.0;

        // Assert
        Assert.Equal(65.0, sut.PeakValue);
        Assert.Equal(nameof(SpectrumBarItem.PeakValue), changedProp);
    }

    /// <summary>
    /// PeakHoldCountプロパティに値を代入・取得できることを検証します。
    /// </summary>
    [Fact]
    public void PeakHoldCount_値の設定と取得ができる()
    {
        // Arrange
        var sut = new SpectrumBarItem();

        // Act
        sut.PeakHoldCount = 15;

        // Assert
        Assert.Equal(15, sut.PeakHoldCount);
    }
}
