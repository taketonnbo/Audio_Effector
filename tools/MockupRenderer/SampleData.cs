using System.Dynamic;
using System.Windows.Media;
using AudioEffector.Presentation.ViewModels;

namespace MockupRenderer;

/// <summary>描画専用の固定データ。ユーザーのライブラリ、設定、音声デバイスにアクセスしない。</summary>
public static class SampleData
{
    private static dynamic Row(params (string Key, object? Value)[] values)
    {
        IDictionary<string, object?> row = new ExpandoObject();
        foreach (var (key, value) in values) row[key] = value;
        return row;
    }

    public static dynamic Create(string state)
    {
        var tracks = Enumerable.Range(1, 8).Select(i => Row(("Title", $"サンプル楽曲 {i:00}"), ("Artist", "Sample Artist"), ("Album", "サンプルアルバム 01"), ("FilePath", ""), ("Duration", TimeSpan.FromSeconds(210 + i * 7)), ("DurationString", TimeSpan.FromSeconds(210 + i * 7).ToString(@"mm\:ss")), ("IsPlaying", i == 1), ("IsFavorite", i == 2), ("IsSelected", false), ("QualityLabel", "Lossless"), ("QualityInfo", "44.1 kHz / 16 bit"))).ToArray();
        var albums = Enumerable.Range(1, 6).Select(i => Row(("Title", $"サンプルアルバム {i:00}"), ("Artist", "Sample Artist"), ("Tracks", tracks), ("TrackCount", tracks.Length), ("IsSelected", false), ("IsOnDevice", false))).ToArray();
        var playlists = new[] { Row(("Name", "お気に入りの音楽")), Row(("Name", "作業用プレイリスト")) };
        dynamic root = Row(("CurrentViewType", ViewType.Albums), ("CurrentTrack", tracks[0]), ("Track", tracks[0]), ("Albums", albums), ("Playlists", playlists), ("UserPlaylists", playlists), ("PlaylistTracks", tracks), ("CurrentViewingPlaylist", playlists[0]), ("SelectedPlaylist", playlists[0]), ("CurrentPlaylistName", state == "favorites" ? "お気に入り" : "お気に入りの音楽"), ("IsFavoritesView", state == "favorites"), ("IsGridView", state == "grid"), ("IsListView", state != "grid"), ("IsRightPanelOpen", state != "closed"), ("IsAlbumViewMaximized", state == "maximized"), ("IsDeviceConnected", true), ("IsLoading", false), ("IsScanning", false), ("IsSelectionMode", false), ("IsPlaying", false), ("IsShuffleEnabled", false), ("IsAlbumRepeat", false), ("IsTrackRepeat", false), ("IsMuted", false), ("Volume", 0.65), ("Progress", 32.0), ("CurrentTimeDisplay", "01:12"), ("TotalTimeDisplay", "03:37"), ("NowPlayingImage", null), ("PlaylistBackgroundImage", null), ("PlaybackListName", "サンプルアルバム 01"), ("PlaybackListSubtitle", "Sample Artist"), ("PlaybackListTracks", tracks), ("PlayQueue", state == "empty" ? Array.Empty<object>() : tracks), ("UserQueue", tracks.Take(2).ToArray()), ("AlbumQueue", tracks.Skip(2).ToArray()), ("HasUserQueue", state != "empty"), ("HasAlbumQueue", state != "empty"), ("AlbumQueueTitle", "サンプルアルバム 01"), ("PlayHistory", tracks.Reverse().ToArray()), ("SelectedQueueTabIndex", state == "history" ? 1 : 0), ("IsPlayQueuePanelOpen", true), ("SpectrumBorderBrush", Brushes.DarkCyan), ("SpectrumBackgroundBrush", Brushes.Transparent), ("DeviceAlbums", albums), ("IsTransferring", state == "transferring"), ("TransferProgress", state == "transferring" ? 42.0 : 0.0), ("CurrentDevicePath", @"E:\Music"), ("DeviceDirectories", new[] { Row(("Name", "Music"), ("IsFolder", true)), Row(("Name", "Playlists"), ("IsFolder", true)) }), ("Message", "プレイリスト名を入力してください"), ("InputText", "新しいプレイリスト"));
        dynamic device = Row(("Name", "USB Drive (E:)"));
        root.RemovableDrives = new[] { device }; root.SelectedDevice = device;
        dynamic preset = Row(("Name", "Flat"));
        root.Presets = new[] { preset }; root.SelectedPreset = preset;
        root.Bands = new[] { "31Hz", "62Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" }.Select(label => Row(("Label", label), ("Gain", 0.0))).ToArray();
        root.PlayerControl = Row(("IsSpectrumVisible", state == "spectrum"), ("SpectrumValues", Enumerable.Range(0, 64).Select(i => Row(("Value", 12.0 + i * 17 % 95), ("PeakValue", 20.0 + i * 17 % 95))).ToArray()));
        root.Categories = new[] { "一般", "オーディオデバイス", "エフェクト・再生", "ショートカット", "データ管理・その他" };
        root.SelectedCategory = state switch { "audio" => "オーディオデバイス", "effects" => "エフェクト・再生", "shortcuts" => "ショートカット", "data" => "データ管理・その他", _ => "一般" };
        root.AvailableThemes = new[] { "Dark", "Light" }; root.SelectedTheme = "Dark";
        root.AvailableTopmostBehaviors = new[] { "常に最前面に表示", "表示時のみ最前面に表示", "最前面に表示しない" }; root.SelectedTopmostBehavior = "最前面に表示しない";
        root.AutoStart = false; root.StartMinimized = false; root.MasterVolume = 0.8; root.EnableNormalize = false;
        root.AvailableSampleRates = new[] { 44100, 48000, 88200, 96000, 192000 }; root.SelectedSampleRate = 44100;
        root.AvailableBufferSizes = new[] { 50, 100, 200, 300, 500 }; root.SelectedBufferSize = 100;
        root.SortOptions = new[] { "Artist", "Album" }; root.SelectedSortOption = "Artist"; root.IsAscending = true;
        if (state == "spectrum") root.IsPlaying = true;
        // MainViewModel.SpectrumBarBrush のアート未指定時の既定値。
        var spectrumBrush = new LinearGradientBrush { StartPoint = new System.Windows.Point(1, 0.5), EndPoint = new System.Windows.Point(0, 0.5) };
        spectrumBrush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0));
        spectrumBrush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1));
        spectrumBrush.Freeze();
        root.SpectrumBarBrush = spectrumBrush;
        var spectrumBorder = new SolidColorBrush(Color.FromArgb(230, 0, 229, 255));
        spectrumBorder.Freeze();
        root.SpectrumBorderBrush = spectrumBorder;
        var settings = new AudioEffector.Domain.Entities.AppSettings();
        var properties = (IDictionary<string, object?>)root;
        foreach (var property in settings.GetType().GetProperties().Where(p => p.PropertyType == typeof(AudioEffector.Domain.Entities.ShortcutKeyConfig))) properties[property.Name] = property.GetValue(settings);
        root.Library = root; root.Playlist = root; root.Folder = root; root.Equalizer = root; root.DeviceBrowser = root;
        return root;
    }
}
