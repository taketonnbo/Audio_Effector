using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AudioEffector.Services
{
    public static class AlbumArtLoader
    {
        // LRU Cache
        private static readonly int MaxCacheSize = 100;
        private static readonly Dictionary<string, BitmapImage> Cache = new Dictionary<string, BitmapImage>();
        private static readonly LinkedList<string> LruList = new LinkedList<string>();

        public static readonly DependencyProperty SourcePathProperty =
            DependencyProperty.RegisterAttached(
                "SourcePath",
                typeof(string),
                typeof(AlbumArtLoader),
                new PropertyMetadata(null, OnSourcePathChanged));

        public static string GetSourcePath(DependencyObject obj)
        {
            return (string)obj.GetValue(SourcePathProperty);
        }

        public static void SetSourcePath(DependencyObject obj, string value)
        {
            obj.SetValue(SourcePathProperty, value);
        }

        private static async void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Image image)
            {
                string path = e.NewValue as string;

                if (string.IsNullOrEmpty(path))
                {
                    image.Source = null;
                    return;
                }

                // Check Cache
                if (Cache.TryGetValue(path, out var cachedImage))
                {
                    image.Source = cachedImage;
                    UpdateLru(path);
                    return;
                }

                // Set placeholder or null while loading
                image.Source = null; // Or a placeholder resource

                try
                {
                    var loadedImage = await LoadImageAsync(path);
                    if (loadedImage != null)
                    {
                        AddToCache(path, loadedImage);
                        // Verify the path hasn't changed while we were loading
                        if (GetSourcePath(image) == path)
                        {
                            image.Source = loadedImage;
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        private static Task<BitmapImage> LoadImageAsync(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    using (var tfile = TagLib.File.Create(path))
                    {
                        if (tfile.Tag.Pictures.Length > 0)
                        {
                            var bin = tfile.Tag.Pictures[0].Data.Data;
                            
                            var image = new BitmapImage();
                            using (var mem = new MemoryStream(bin))
                            {
                                mem.Position = 0;
                                image.BeginInit();
                                image.DecodePixelWidth = 150; // Thumbnail size
                                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.UriSource = null;
                                image.StreamSource = mem;
                                image.EndInit();
                            }
                            image.Freeze();
                            return image;
                        }
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
                var last = LruList.Last.Value;
                LruList.RemoveLast();
                Cache.Remove(last);
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
}
