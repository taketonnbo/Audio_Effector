using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AudioEffector.Presentation.AttachedProperties;

/// <summary>
/// アルバムアートを非同期で読み込み、キャッシュするためのユーティリティクラス（WPF添付プロパティ）
/// </summary>
public static class AlbumArtLoader
{
    private const int MaxCacheSize = 100;
    private static readonly Dictionary<string, BitmapImage> Cache = new();
    private static readonly LinkedList<string> LruList = new();

    /// <summary>
    /// 画像のソースパスを指定する添付プロパティ。
    /// このプロパティにパスを設定すると、自動的に非同期読み込みが開始されます。
    /// </summary>
    public static readonly DependencyProperty SourcePathProperty =
        DependencyProperty.RegisterAttached(
            "SourcePath",
            typeof(string),
            typeof(AlbumArtLoader),
            new PropertyMetadata(null, OnSourcePathChanged));

    public static string? GetSourcePath(DependencyObject obj)
    {
        return (string?)obj.GetValue(SourcePathProperty);
    }

    public static void SetSourcePath(DependencyObject obj, string? value)
    {
        obj.SetValue(SourcePathProperty, value);
    }

    private static BitmapImage? _defaultImage;

    private static BitmapImage? GetDefaultImage()
    {
        if (_defaultImage == null)
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/Images/default_now_playing_bg.png");
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                _defaultImage = bitmap;
            }
            catch
            {
                // Silent fail
            }
        }
        return _defaultImage;
    }

    private static async void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            string? path = e.NewValue as string;

            if (string.IsNullOrEmpty(path))
            {
                image.Source = GetDefaultImage();
                return;
            }

            // Check Cache
            if (Cache.TryGetValue(path, out var cachedImage))
            {
                image.Source = cachedImage;
                UpdateLru(path);
                return;
            }

            // Set placeholder while loading
            image.Source = GetDefaultImage();

            try
            {
                var loadedImage = await LoadImageAsync(path);
                if (loadedImage != null)
                {
                    AddToCache(path, loadedImage);
                    if (GetSourcePath(image) == path)
                    {
                        image.Source = loadedImage;
                    }
                }
                else
                {
                    if (GetSourcePath(image) == path)
                    {
                        image.Source = GetDefaultImage();
                    }
                }
            }
            catch
            {
                if (GetSourcePath(image) == path)
                {
                    image.Source = GetDefaultImage();
                }
            }
        }
    }

    private static Task<BitmapImage?> LoadImageAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                using var tfile = TagLib.File.Create(path);
                if (tfile.Tag.Pictures.Length > 0)
                {
                    var bin = tfile.Tag.Pictures[0].Data.Data;

                    var image = new BitmapImage();
                    using var mem = new MemoryStream(bin);
                    mem.Position = 0;
                    image.BeginInit();
                    image.DecodePixelWidth = 150; // Thumbnail size
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = null;
                    image.StreamSource = mem;
                    image.EndInit();
                    image.Freeze();
                    return (BitmapImage?)image;
                }
            }
            catch
            {
                // Ignore
            }
            return null;
        });
    }

    private static void AddToCache(string key, BitmapImage image)
    {
        if (Cache.ContainsKey(key))
        {
            UpdateLru(key);
            return;
        }

        if (Cache.Count >= MaxCacheSize)
        {
            var last = LruList.Last?.Value;
            if (last != null)
            {
                LruList.RemoveLast();
                Cache.Remove(last);
            }
        }

        Cache[key] = image;
        LruList.AddFirst(key);
    }

    private static void UpdateLru(string key)
    {
        if (LruList.Contains(key))
        {
            LruList.Remove(key);
            LruList.AddFirst(key);
        }
    }
}
