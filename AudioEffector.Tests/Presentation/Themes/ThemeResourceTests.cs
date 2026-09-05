using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace AudioEffector.Tests.Presentation.Themes;

/// <summary>
/// テーマリソース（DarkTheme / LightTheme）のXAMLロード、主要ブラシ定義、およびビューのリソース参照検証を行うテストクラス。
/// </summary>
public class ThemeResourceTests
{
    private static readonly Color ExpectedNeonCyanColor = Color.FromRgb(0, 255, 255);
    private static readonly Color ExpectedLightAccentColor = Color.FromRgb(0, 120, 215);

    /// <summary>
    /// DarkThemeリソースディクショナリが例外なく正常にロードされ、主要ブラシおよびAlwaysNeonCyanが期待通り定義されていることを検証します。
    /// </summary>
    [Fact]
    public void DarkTheme_リソース読み込み時_主要ブラシおよびAlwaysNeonCyanが期待通り定義されていること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var uri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml");

            // Act
            var sut = new ResourceDictionary { Source = uri };

            // Assert
            Assert.NotNull(sut);
            Assert.True(sut.Contains("WindowBackgroundBrush"));
            Assert.True(sut.Contains("NeonCyanBrush"));
            Assert.True(sut.Contains("AlwaysNeonCyanBrush"));
            Assert.True(sut.Contains("AlwaysNeonCyanColor"));

            var neonCyanColor = (Color)sut["NeonCyanColor"];
            Assert.Equal(ExpectedNeonCyanColor, neonCyanColor);

            var neonCyanBrush = Assert.IsType<SolidColorBrush>(sut["NeonCyanBrush"]);
            Assert.Equal(ExpectedNeonCyanColor, neonCyanBrush.Color);

            var alwaysColor = (Color)sut["AlwaysNeonCyanColor"];
            Assert.Equal(ExpectedNeonCyanColor, alwaysColor);

            var alwaysBrush = Assert.IsType<SolidColorBrush>(sut["AlwaysNeonCyanBrush"]);
            Assert.Equal(ExpectedNeonCyanColor, alwaysBrush.Color);
        });
    }

    /// <summary>
    /// LightThemeリソースディクショナリ読み込み時、通常アクセントは青色であり、ダークサーフェス用AlwaysNeonCyanは水色(#00FFFF)を保持していることを検証します。
    /// （Issue #129 再発防止）
    /// </summary>
    [Fact]
    public void LightTheme_リソース読み込み時_AlwaysNeonCyanがダークサーフェス用ネオンシアン色を保持していること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var uri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml");

            // Act
            var sut = new ResourceDictionary { Source = uri };

            // Assert
            Assert.NotNull(sut);
            Assert.True(sut.Contains("WindowBackgroundBrush"));
            Assert.True(sut.Contains("NeonCyanBrush"));
            Assert.True(sut.Contains("AlwaysNeonCyanBrush"));
            Assert.True(sut.Contains("AlwaysNeonCyanColor"));

            // ライトテーマの通常アクセントは暗青色(#0078D7)
            var neonCyanColor = (Color)sut["NeonCyanColor"];
            Assert.Equal(ExpectedLightAccentColor, neonCyanColor);

            var neonCyanBrush = Assert.IsType<SolidColorBrush>(sut["NeonCyanBrush"]);
            Assert.Equal(ExpectedLightAccentColor, neonCyanBrush.Color);

            // 【Issue #129 再発防止】黒半透明の再生オーバーレイ向けカラーは、ライトテーマでも鮮やかな水色(#00FFFF)
            var alwaysColor = (Color)sut["AlwaysNeonCyanColor"];
            Assert.Equal(ExpectedNeonCyanColor, alwaysColor);

            var alwaysBrush = Assert.IsType<SolidColorBrush>(sut["AlwaysNeonCyanBrush"]);
            Assert.Equal(ExpectedNeonCyanColor, alwaysBrush.Color);
        });
    }

    /// <summary>
    /// MainWindow.xamlのリソース定義において、右側パネルやプレイヤー操作系の配色を維持するNeonCyanBrushが水色(#00FFFF)として定義されていることを検証します。
    /// （リグレッション再発防止）
    /// </summary>
    [Fact]
    public void MainWindow_リソース定義確認_右側パネルとプレイヤー用のNeonCyanBrushが水色として定義されていること()
    {
        // Arrange
        var rootDir = FindWorkspaceRoot();
        var xamlPath = Path.Combine(rootDir, "AudioEffector", "Presentation", "Views", "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), $"MainWindow.xaml が見つかりません: {xamlPath}");

        // Act
        var xamlContent = File.ReadAllText(xamlPath);
        var sut = XDocument.Parse(xamlContent);

        // Assert
        // MainWindow.xaml の <Window.Resources> 内に NeonCyanColor (#00FFFF) および NeonCyanBrush が定義されていること
        var resourcesElement = sut.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Window.Resources");
        Assert.NotNull(resourcesElement);

        var colorElement = resourcesElement.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "Color" &&
            e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "NeonCyanColor"));
        Assert.NotNull(colorElement);
        Assert.Equal("#00FFFF", colorElement.Value.Trim());

        var brushElement = resourcesElement.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "SolidColorBrush" &&
            e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "NeonCyanBrush"));
        Assert.NotNull(brushElement);
    }

    /// <summary>
    /// LibraryView.xamlのアルバムアート再生オーバーレイにおいて、テーマに依存しないAlwaysNeonCyanBrushおよびAlwaysNeonCyanColorが参照されていることを検証します。
    /// （Issue #129 再発防止）
    /// </summary>
    [Fact]
    public void LibraryView_再生オーバーレイ確認_AlwaysNeonCyanリソースを参照していること()
    {
        // Arrange
        var rootDir = FindWorkspaceRoot();
        var xamlPath = Path.Combine(rootDir, "AudioEffector", "Presentation", "Views", "LibraryView.xaml");
        Assert.True(File.Exists(xamlPath), $"LibraryView.xaml が見つかりません: {xamlPath}");

        // Act
        var xamlContent = File.ReadAllText(xamlPath);
        var sut = XDocument.Parse(xamlContent);

        // Assert
        // ListView側(#80000000)およびGridView側(#60000000)のPlay Overlay（背景が黒半透明のGrid）を取得
        var overlayGrids = sut.Descendants()
            .Where(e => e.Name.LocalName == "Grid" &&
                e.Attributes().Any(a => a.Name.LocalName == "Background" && (a.Value == "#80000000" || a.Value == "#60000000")))
            .ToList();

        Assert.Equal(2, overlayGrids.Count);

        foreach (var overlay in overlayGrids)
        {
            var overlayXml = overlay.ToString();
            // 常時ネオンシアンのリソースが参照されていること
            Assert.Contains("{DynamicResource AlwaysNeonCyanBrush}", overlayXml);
            Assert.Contains("{DynamicResource AlwaysNeonCyanColor}", overlayXml);

            // 通常のNeonCyanBrush/Colorが参照されていないこと（ライトテーマ時の暗青色化・視認性低下防止）
            Assert.DoesNotContain("NeonCyanBrush", overlayXml.Replace("AlwaysNeonCyanBrush", ""));
            Assert.DoesNotContain("NeonCyanColor", overlayXml.Replace("AlwaysNeonCyanColor", ""));
        }
    }

    private static string FindWorkspaceRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "AudioEffector.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new InvalidOperationException("AudioEffector.sln が見つかりませんでした。");
    }

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                // STAスレッド終了前にDispatcherをシャットダウンし、
                // 後続テストでdispatcher.Invoke()がハングするのを防止する
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw threadEx;
        }
    }
}
