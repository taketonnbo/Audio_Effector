using System;
using System.IO;
using System.Xml.Linq;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="MainWindow"/> の最小化時コンパクトプレイヤー表示判定および画面切替定義を検証するテストクラス。
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

    /// <summary>
    /// MainWindow.xaml のメイン ContentControl に ViewType.AlbumDetail 用の DataTrigger と AlbumDetailView が定義されていることを検証します。
    /// </summary>
    [Fact]
    public void MainWindowXaml_ViewTypeAlbumDetail切替定義_AlbumDetailViewのDataTriggerが存在すること()
    {
        // Arrange
        string? repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);
        string mainWindowXamlPath = Path.Combine(repoRoot!, "AudioEffector", "Presentation", "Views", "MainWindow.xaml");
        var sut = XDocument.Load(mainWindowXamlPath);
        XNamespace wpfn = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace viewsNs = "clr-namespace:AudioEffector.Presentation.Views";

        // Act
        var albumDetailTrigger = System.Linq.Enumerable.Single(
            sut.Descendants(wpfn + "DataTrigger"),
            dt => string.Equals((string?)dt.Attribute("Value"), "{x:Static vm:ViewType.AlbumDetail}", StringComparison.Ordinal));
        var albumDetailViewElement = System.Linq.Enumerable.Single(
            albumDetailTrigger.Descendants(viewsNs + "AlbumDetailView"));

        // Assert
        Assert.Equal("{Binding AlbumDetail}", (string?)albumDetailViewElement.Attribute("DataContext"));
    }

    private static string? FindRepositoryRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AudioEffector.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
