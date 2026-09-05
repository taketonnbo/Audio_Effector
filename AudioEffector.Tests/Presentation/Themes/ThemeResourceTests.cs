using System;
using System.Threading;
using System.Windows;
using Xunit;

namespace AudioEffector.Tests.Presentation.Themes;

/// <summary>
/// テーマリソース（DarkTheme / LightTheme）のXAMLロードおよび主要ブラシ定義の検証を行うテストクラス。
/// </summary>
public class ThemeResourceTests
{
    /// <summary>
    /// DarkThemeリソースディクショナリが例外なく正常にロードされ、主要ブラシが含まれていることを検証します。
    /// </summary>
    [Fact]
    public void DarkTheme_リソースが例外なく正常にロードできる()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var uri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml");

            // Act
            var dict = new ResourceDictionary { Source = uri };

            // Assert
            Assert.NotNull(dict);
            Assert.True(dict.Contains("WindowBackgroundBrush"));
            Assert.True(dict.Contains("NeonCyanBrush"));
        });
    }

    /// <summary>
    /// LightThemeリソースディクショナリが例外なく正常にロードされ、主要ブラシが含まれていることを検証します。
    /// </summary>
    [Fact]
    public void LightTheme_リソースが例外なく正常にロードできる()
    {
        RunInStaThread(() =>
        {
            // Arrange
            var uri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml");

            // Act
            var dict = new ResourceDictionary { Source = uri };

            // Assert
            Assert.NotNull(dict);
            Assert.True(dict.Contains("WindowBackgroundBrush"));
            Assert.True(dict.Contains("NeonCyanBrush"));
        });
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
