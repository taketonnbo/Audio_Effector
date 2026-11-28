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

    /// <summary>
    /// MainWindow.xaml の右側タブ右上に拡大／縮小の単一ボタン、コンパクト上部情報行へ干渉防止マージン、
    /// 下部拡張領域へ 3 タブバーおよび LyricsView / ListeningStatsView が仕様通り定義されていることを検証します。
    /// </summary>
    [Fact]
    public void MainWindowXaml_右側タブ3タブ統合_単一サイズ切替ボタンとマージンと3タブおよびビューが仕様通り定義されていること()
    {
        // Arrange
        string? repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        Assert.NotNull(repoRoot);
        string mainWindowXamlPath = Path.Combine(repoRoot!, "AudioEffector", "Presentation", "Views", "MainWindow.xaml");
        var sut = XDocument.Load(mainWindowXamlPath);
        XNamespace wpfn = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace viewsNs = "clr-namespace:AudioEffector.Presentation.Views";

        // Act
        var legacyLyricsToggle = System.Linq.Enumerable.FirstOrDefault(
            sut.Descendants(wpfn + "ToggleButton"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "LyricsToggleButton", StringComparison.Ordinal));
        var sizeModeButton = System.Linq.Enumerable.Single(
            sut.Descendants(wpfn + "Button"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "AlbumViewSizeModeButton", StringComparison.Ordinal));
        var compactInfoGrid = System.Linq.Enumerable.Single(
            sut.Descendants(wpfn + "Grid"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "RightPanelCompactInfoGrid", StringComparison.Ordinal));
        var lyricsTabButton = System.Linq.Enumerable.Single(
            sut.Descendants(wpfn + "Button"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "RightExtensionLyricsTabButton", StringComparison.Ordinal));
        var dashboardTabButton = System.Linq.Enumerable.Single(
            sut.Descendants(wpfn + "Button"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "RightExtensionDashboardTabButton", StringComparison.Ordinal));
        var tagsTabButton = System.Linq.Enumerable.Single(
            sut.Descendants(wpfn + "Button"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "RightExtensionTagsTabButton", StringComparison.Ordinal));
        var lyricsView = System.Linq.Enumerable.Single(
            sut.Descendants(viewsNs + "LyricsView"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "RightPanelLyricsView", StringComparison.Ordinal));
        var dashboardView = System.Linq.Enumerable.Single(
            sut.Descendants(viewsNs + "ListeningStatsView"),
            el => string.Equals((string?)el.Attribute(xNs + "Name"), "RightPanelDashboardView", StringComparison.Ordinal));

        // Assert
        Assert.Null(legacyLyricsToggle);
        Assert.Equal("{Binding ToggleAlbumViewSizeModeCommand}", (string?)sizeModeButton.Attribute("Command"));
        Assert.Equal("28", (string?)sizeModeButton.Attribute("Width"));
        Assert.Equal("28", (string?)sizeModeButton.Attribute("Height"));
        Assert.Equal("0,0,38,8", (string?)compactInfoGrid.Attribute("Margin"));
        Assert.Equal("{Binding SelectRightExtensionTabCommand}", (string?)lyricsTabButton.Attribute("Command"));
        Assert.Equal("{Binding SelectRightExtensionTabCommand}", (string?)dashboardTabButton.Attribute("Command"));
        Assert.Equal("{Binding SelectRightExtensionTabCommand}", (string?)tagsTabButton.Attribute("Command"));
        Assert.Equal("{Binding Lyrics}", (string?)lyricsView.Attribute("DataContext"));
        Assert.Equal("{Binding ListeningStats}", (string?)dashboardView.Attribute("DataContext"));
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
