using System;
using System.Threading;
using System.Windows;
using Xunit;

namespace AudioEffector.Tests;

public class ThemeResourceTests
{
    [Fact]
    public void DarkTheme_リソースが例外なく正常にロードできる()
    {
        RunInStaThread(() =>
        {
            var uri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml");
            var dict = new ResourceDictionary { Source = uri };
            Assert.NotNull(dict);
            Assert.True(dict.Contains("WindowBackgroundBrush"));
            Assert.True(dict.Contains("NeonCyanBrush"));
        });
    }

    [Fact]
    public void LightTheme_リソースが例外なく正常にロードできる()
    {
        RunInStaThread(() =>
        {
            var uri = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml");
            var dict = new ResourceDictionary { Source = uri };
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
