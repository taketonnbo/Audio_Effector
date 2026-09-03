using System;
using System.Linq;
using System.Windows;
using AudioEffector.Domain.Entities;
using Microsoft.Win32;

namespace AudioEffector.Presentation.Themes;

/// <summary>
/// アプリケーションのUIテーマ（Dark / Light / System連動）の切り替えを管理するクラス
/// </summary>
public class ThemeManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    public static void ApplyTheme(ThemeType theme)
    {
        ThemeType themeToApply = theme;

        if (theme == ThemeType.System)
        {
            themeToApply = GetSystemTheme();
        }

        string themeFileName = themeToApply == ThemeType.Light ? "LightTheme.xaml" : "DarkTheme.xaml";
        Uri themeUri = new Uri($"pack://application:,,,/AudioEffector;component/Presentation/Themes/{themeFileName}");

        var existingThemeDict = System.Windows.Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Themes/"));

        if (existingThemeDict != null)
        {
            System.Windows.Application.Current.Resources.MergedDictionaries.Remove(existingThemeDict);
        }

        ResourceDictionary newThemeDict = new ResourceDictionary { Source = themeUri };
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(newThemeDict);
    }

    private static ThemeType GetSystemTheme()
    {
        try
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key != null)
                {
                    object? registryValueObject = key.GetValue(RegistryValueName);
                    if (registryValueObject != null)
                    {
                        int registryValue = (int)registryValueObject;
                        return registryValue > 0 ? ThemeType.Light : ThemeType.Dark;
                    }
                }
            }
        }
        catch
        {
            // Fallback to dark theme on error
        }

        return ThemeType.Dark;
    }
}
