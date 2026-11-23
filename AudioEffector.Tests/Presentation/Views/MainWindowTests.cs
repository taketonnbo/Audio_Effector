using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="MainWindow"/> の最小化時コンパクトプレイヤー表示判定ロジックを検証するテストクラス。
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>
    /// DisableMiniPlayerOnMinimize が true の場合、最小化時にコンパクトプレイヤーを表示しない（false を返す）ことを検証します。
    /// </summary>
    [Fact]
    public void ShouldShowMiniPlayerOnMinimize_DisableMiniPlayerOnMinimizeがTrueの場合_Falseを返す()
    {
        // Arrange
        var sut = new AppSettings { DisableMiniPlayerOnMinimize = true };

        // Act
        bool result = MainWindow.ShouldShowMiniPlayerOnMinimize(sut);

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// DisableMiniPlayerOnMinimize が false（既定値）の場合、最小化時にコンパクトプレイヤーを表示する（true を返す）ことを検証します。
    /// </summary>
    [Fact]
    public void ShouldShowMiniPlayerOnMinimize_DisableMiniPlayerOnMinimizeがFalseの場合_Trueを返す()
    {
        // Arrange
        var sut = new AppSettings { DisableMiniPlayerOnMinimize = false };

        // Act
        bool result = MainWindow.ShouldShowMiniPlayerOnMinimize(sut);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// 設定オブジェクトが null の場合でも例外を発生させず、既定動作としてコンパクトプレイヤーを表示する（true を返す）ことを検証します。
    /// </summary>
    [Fact]
    public void ShouldShowMiniPlayerOnMinimize_設定がNullの場合_既定動作としてTrueを返す()
    {
        // Arrange
        AppSettings? sut = null;

        // Act
        bool result = MainWindow.ShouldShowMiniPlayerOnMinimize(sut);

        // Assert
        Assert.True(result);
    }
}
