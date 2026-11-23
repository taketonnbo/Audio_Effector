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

    /// <summary>
    /// PlayQueueSidePanel.xamlにおいて、Issue #225に伴い再生オーバーレイが消去されていること、
    /// およびアクセントカラーとしてテーマに依存しないAlwaysNeonCyanBrushが参照されていることを検証します。
    /// （Issue #202, #225）
    /// </summary>
    [Fact]
    public void PlayQueueSidePanel_再生オーバーレイ確認_AlwaysNeonCyanリソースを参照していること()
    {
        // Arrange
        var rootDir = FindWorkspaceRoot();
        var xamlPath = Path.Combine(rootDir, "AudioEffector", "Presentation", "Views", "PlayQueueSidePanel.xaml");
        Assert.True(File.Exists(xamlPath), $"PlayQueueSidePanel.xaml が見つかりません: {xamlPath}");

        // Act
        var xamlContent = File.ReadAllText(xamlPath);
        var sut = XDocument.Parse(xamlContent);

        // Assert: Issue #225により再生中アイコンの半透明オーバーレイ(#80000000)は消去されていること
        var overlayBorders = sut.Descendants()
            .Where(e => e.Name.LocalName == "Border" &&
                e.Attributes().Any(a => a.Name.LocalName == "Background" && a.Value == "#80000000"))
            .ToList();

        Assert.Empty(overlayBorders);

        // パネル内のアクセント表示（ヘッダーアイコン、アクティブインジケーター、予約クリア等）でAlwaysNeonCyanBrushが参照されていること
        Assert.Contains("{DynamicResource AlwaysNeonCyanBrush}", xamlContent);
    }

    /// <summary>
    /// PlayQueueSidePanel.xamlのヘッダー（タイトルおよびアイコン）において、AlwaysNeonCyanBrushが参照されていることを検証します。
    /// （Issue #202 再発防止）
    /// </summary>
    [Fact]
    public void PlayQueueSidePanel_ヘッダー確認_AlwaysNeonCyanリソースを参照していること()
    {
        // Arrange
        var rootDir = FindWorkspaceRoot();
        var xamlPath = Path.Combine(rootDir, "AudioEffector", "Presentation", "Views", "PlayQueueSidePanel.xaml");
        Assert.True(File.Exists(xamlPath), $"PlayQueueSidePanel.xaml が見つかりません: {xamlPath}");

        // Act
        var xamlContent = File.ReadAllText(xamlPath);
        var sut = XDocument.Parse(xamlContent);

        // Assert
        var titleBlock = sut.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "TextBlock" &&
                e.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "再生キュー"));

        Assert.NotNull(titleBlock);
        Assert.Equal("{DynamicResource AlwaysNeonCyanBrush}", titleBlock.Attribute("Foreground")?.Value);
    }

    /// <summary>
    /// DarkThemeおよびLightThemeにアクセントボタン上の反転文字色リソース（InverseTextForegroundBrush）が定義されていることを検証します。
    /// </summary>
    [Fact]
    public void Theme_リソース読み込み時_InverseTextForegroundBrushが両テーマで視認性の高い色として定義されていること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var darkUri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml");
            var lightUri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml");

            // Act
            var darkDict = new ResourceDictionary { Source = darkUri };
            var lightDict = new ResourceDictionary { Source = lightUri };

            // Assert
            Assert.True(darkDict.Contains("InverseTextForegroundBrush"));
            Assert.True(lightDict.Contains("InverseTextForegroundBrush"));

            var darkInverse = Assert.IsType<SolidColorBrush>(darkDict["InverseTextForegroundBrush"]);
            var lightInverse = Assert.IsType<SolidColorBrush>(lightDict["InverseTextForegroundBrush"]);
            Assert.Equal(Color.FromRgb(0x16, 0x19, 0x20), darkInverse.Color);
            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), lightInverse.Color);
        });
    }

    /// <summary>
    /// AllSongsView.xamlの暗い再生オーバーレイにおいてAlwaysNeonCyanBrushが参照されていることを検証します。
    /// </summary>
    [Fact]
    public void AllSongsView_再生オーバーレイ確認_AlwaysNeonCyanリソースを参照していること()
    {
        // Arrange
        var rootDir = FindWorkspaceRoot();
        var xamlPath = Path.Combine(rootDir, "AudioEffector", "Presentation", "Views", "AllSongsView.xaml");
        Assert.True(File.Exists(xamlPath), $"AllSongsView.xaml が見つかりません: {xamlPath}");

        // Act
        var xamlContent = File.ReadAllText(xamlPath);
        var sut = XDocument.Parse(xamlContent);

        // Assert
        var overlayBorders = sut.Descendants()
            .Where(e => e.Name.LocalName == "Border" &&
                e.Attributes().Any(a => a.Name.LocalName == "Background" && a.Value == "#B0161920"))
            .ToList();

        Assert.Single(overlayBorders);
        var overlayXml = overlayBorders[0].ToString();
        Assert.Contains("{DynamicResource AlwaysNeonCyanBrush}", overlayXml);
        Assert.DoesNotContain("NeonCyanBrush", overlayXml.Replace("AlwaysNeonCyanBrush", ""));
    }

    /// <summary>
    /// MiniPlayerWindow.xaml で参照されているすべての DynamicResource が DarkTheme / LightTheme 両方に定義されており、
    /// テーマ背景上（横長レイアウト・キュー領域）では NeonCyanBrush を使用し、
    /// 正方形デザインの暗幕オーバーレイ（#B3161920）上では両テーマ共通の AlwaysNeonCyanBrush を使用していることを検証します。
    /// </summary>
    [Fact]
    public void MiniPlayerWindow_テーマリソース参照確認_全DynamicResourceが両テーマに定義され暗幕オーバーレイはAlwaysNeonCyanBrushであること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var rootDir = FindWorkspaceRoot();
            var xamlPath = Path.Combine(rootDir, "AudioEffector", "Presentation", "Views", "MiniPlayerWindow.xaml");
            Assert.True(File.Exists(xamlPath), $"MiniPlayerWindow.xaml が見つかりません: {xamlPath}");

            var xamlContent = File.ReadAllText(xamlPath);
            var doc = XDocument.Parse(xamlContent);
            var darkDict = new ResourceDictionary { Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml") };
            var lightDict = new ResourceDictionary { Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml") };

            // Act
            var referencedKeys = System.Text.RegularExpressions.Regex
                .Matches(xamlContent, @"\{DynamicResource\s+([A-Za-z0-9_]+)\}")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            var overlayBorders = doc.Descendants()
                .Where(e => e.Name.LocalName == "Border" &&
                    e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "SquarePlaybackControlsOverlay"))
                .ToList();

            var standardLayout = doc.Descendants()
                .Single(e => e.Name.LocalName == "Grid" &&
                    e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "StandardLayoutGrid"));

            // Assert
            Assert.NotEmpty(referencedKeys);
            foreach (var key in referencedKeys)
            {
                Assert.True(darkDict.Contains(key), $"DarkTheme.xaml に未定義のリソース '{key}' が MiniPlayerWindow.xaml で参照されています。");
                Assert.True(lightDict.Contains(key), $"LightTheme.xaml に未定義のリソース '{key}' が MiniPlayerWindow.xaml で参照されています。");
            }

            // 横長レイアウト（テーマ背景上）では AlwaysNeonCyanBrush を誤用せず NeonCyanBrush を使用する
            var standardXml = standardLayout.ToString();
            Assert.DoesNotContain("AlwaysNeonCyanBrush", standardXml);
            Assert.Contains("{DynamicResource NeonCyanBrush}", standardXml);

            // 正方形暗幕オーバーレイ（#B3161920）上では両テーマ共通の AlwaysNeonCyanBrush を使用する
            var overlayBorder = Assert.Single(overlayBorders);
            Assert.Equal("#B3161920", overlayBorder.Attributes().First(a => a.Name.LocalName == "Background").Value);
            var overlayXml = overlayBorder.ToString();
            Assert.Contains("{DynamicResource AlwaysNeonCyanBrush}", overlayXml);
            Assert.DoesNotContain("NeonCyanBrush", overlayXml.Replace("AlwaysNeonCyanBrush", ""));

            // 両テーマで AlwaysNeonCyanBrush が #00FFFF として定義されていることを確認
            var darkAlwaysCyan = Assert.IsType<SolidColorBrush>(darkDict["AlwaysNeonCyanBrush"]);
            var lightAlwaysCyan = Assert.IsType<SolidColorBrush>(lightDict["AlwaysNeonCyanBrush"]);
            Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), darkAlwaysCyan.Color);
            Assert.Equal(Color.FromRgb(0x00, 0xFF, 0xFF), lightAlwaysCyan.Color);
            Assert.Contains("BorderBrush", referencedKeys);
        });
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
