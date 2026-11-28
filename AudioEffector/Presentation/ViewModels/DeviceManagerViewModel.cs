using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 外部デバイスの楽曲・アルバム管理を行うViewModel
/// </summary>
public class DeviceManagerViewModel : ViewModelBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private MainViewModel.DeviceViewModel _currentDevice;
    private string _basePath;
    private List<Album> _pcAlbums;

    /// <summary>
    /// デバイス内のアルバム一覧を取得または設定します
    /// </summary>
    public ObservableCollection<DeviceAlbum> DeviceAlbums { get; set; } = new ObservableCollection<DeviceAlbum>();

    private bool _isLoading;

    /// <summary>
    /// アルバム読み込み中かどうかを示す値を取得または設定します
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// トラック削除コマンドを取得します
    /// </summary>
    public ICommand DeleteTrackCommand { get; }

    /// <summary>
    /// アルバム削除コマンドを取得します
    /// </summary>
    public ICommand DeleteAlbumCommand { get; }

    /// <summary>
    /// コンストラクタで開始された初回アルバム読み込みタスクを取得します
    /// </summary>
    internal Task InitialLoadTask { get; }

    /// <summary>
    /// デバイス情報、ベースパス、PC側アルバム一覧を指定してインスタンスを初期化します
    /// </summary>
    /// <param name="device">対象の外部デバイス情報</param>
    /// <param name="basePath">同期ベースパス</param>
    /// <param name="pcAlbums">PC側のアルバム一覧</param>
    public DeviceManagerViewModel(MainViewModel.DeviceViewModel device, string basePath, List<Album> pcAlbums)
    {
        _currentDevice = device;
        _basePath = basePath;
        _pcAlbums = pcAlbums;

        DeleteTrackCommand = new RelayCommand(ExecuteDeleteTrack);
        DeleteAlbumCommand = new RelayCommand(ExecuteDeleteAlbum);

        InitialLoadTask = LoadAlbumsAsync();
    }

    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".aiff", ".wma", ".m4a", ".mp4", ".flac", ".aac", ".ogg", ".opus", ".alac" };

    /// <summary>
    /// 接続デバイスからアルバムおよび収録トラックを読み込み、楽曲順へ整列して一覧を更新します
    /// </summary>
    /// <returns>読み込み完了を表すタスク</returns>
    internal async Task LoadAlbumsAsync()
    {
        if (_currentDevice == null) return;

        IsLoading = true;
        DeviceAlbums.Clear();

        var loadedAlbums = new List<DeviceAlbum>();

        await Task.Run(() =>
        {
            try
            {
                Logger.Info($"LoadAlbumsAsync started. DeviceType: {_currentDevice.Type}, BasePath: {_basePath}");
                if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem && _currentDevice.Drive != null)
                {
                    string targetPath = string.IsNullOrEmpty(_basePath) ? _currentDevice.Drive.RootDirectory.FullName : _basePath;
                    ScanFileSystemDirectory(targetPath, loadedAlbums);
                }
                else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null && _currentDevice.MtpDevice.IsConnected)
                {
                    string targetPath = string.IsNullOrEmpty(_basePath) ? @"\" : _basePath;
                    Logger.Info($"Scanning MTP targetPath: {targetPath}");
                    ScanMtpDirectory(targetPath, loadedAlbums);
                }
                else
                {
                    Logger.Warn($"Device not fully connected or recognized.");
                }

                // 全スキャンおよび複数フォルダ結合アルバムのマージ完了後に各アルバム内の楽曲順を整列する
                SortAlbumsTracks(loadedAlbums, _pcAlbums);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading device albums: {ex.Message}");
                });
            }
        });

        // ソートして追加
        foreach (var album in loadedAlbums.OrderBy(a => a.Artist).ThenBy(a => a.Title))
        {
            DeviceAlbums.Add(album);
        }
        IsLoading = false;
    }

    /// <summary>
    /// 複数のデバイスアルバムについて、PC側トラック番号優先およびファイル名自然順で各アルバム内楽曲を一括整列します
    /// </summary>
    /// <param name="albums">整列対象のデバイスアルバム一覧</param>
    /// <param name="pcAlbums">PC側ライブラリのアルバム一覧</param>
    internal static void SortAlbumsTracks(IEnumerable<DeviceAlbum>? albums, IReadOnlyList<Album>? pcAlbums)
    {
        if (albums == null)
        {
            return;
        }

        var pcTrackIndex = PcAlbumTrackIndex.Create(pcAlbums);
        foreach (var album in albums)
        {
            SortAlbumTracksCore(album, pcTrackIndex);
        }
    }

    /// <summary>
    /// 単一のデバイスアルバムについて、PC側トラック番号優先およびファイル名自然順で収録楽曲を整列します
    /// </summary>
    /// <param name="album">整列対象のデバイスアルバム</param>
    /// <param name="pcAlbums">PC側ライブラリのアルバム一覧</param>
    internal static void SortAlbumTracks(DeviceAlbum? album, IReadOnlyList<Album>? pcAlbums)
    {
        if (album == null || album.Tracks == null || album.Tracks.Count <= 1)
        {
            return;
        }

        var pcTrackIndex = PcAlbumTrackIndex.Create(pcAlbums);
        SortAlbumTracksCore(album, pcTrackIndex);
    }

    /// <summary>
    /// 文字列内の連続するASCII数字を数値昇順として比較する自然順比較を行います
    /// </summary>
    /// <param name="left">左辺文字列</param>
    /// <param name="right">右辺文字列</param>
    /// <returns>比較結果（左辺が小さい場合は負、等しい場合は0、大きい場合は正）</returns>
    internal static int CompareNatural(string? left, string? right)
        => NaturalStringComparer.Instance.Compare(left, right);

    private static void SortAlbumTracksCore(DeviceAlbum? album, PcAlbumTrackIndex? pcTrackIndex)
    {
        if (album == null || album.Tracks == null || album.Tracks.Count <= 1)
        {
            return;
        }

        int count = album.Tracks.Count;
        var keyedTracks = new KeyedDeviceTrack[count];
        for (int i = 0; i < count; i++)
        {
            var track = album.Tracks[i];
            uint trackNumber = 0;
            bool hasTrackNumber = track != null
                && pcTrackIndex != null
                && pcTrackIndex.TryGetTrackNumber(album, track, out trackNumber);

            keyedTracks[i] = new KeyedDeviceTrack(
                track,
                hasTrackNumber,
                trackNumber,
                track?.Title ?? string.Empty,
                track?.Path ?? string.Empty);
        }

        Array.Sort(keyedTracks, CompareKeyedTracks);

        bool orderChanged = false;
        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(album.Tracks[i], keyedTracks[i].Track))
            {
                orderChanged = true;
                break;
            }
        }

        if (!orderChanged)
        {
            return;
        }

        album.Tracks.Clear();
        for (int i = 0; i < count; i++)
        {
            if (keyedTracks[i].Track != null)
            {
                album.Tracks.Add(keyedTracks[i].Track!);
            }
        }
    }

    private static int CompareKeyedTracks(KeyedDeviceTrack left, KeyedDeviceTrack right)
    {
        // 優先順位1: PC側トラック番号（TrackNumber > 0）を持つ楽曲を先頭に配置し、トラック番号昇順で比較する
        if (left.HasTrackNumber != right.HasTrackNumber)
        {
            return left.HasTrackNumber ? -1 : 1;
        }

        if (left.HasTrackNumber)
        {
            int trackNumberComparison = left.TrackNumber.CompareTo(right.TrackNumber);
            if (trackNumberComparison != 0)
            {
                return trackNumberComparison;
            }
        }

        // 優先順位2: ファイル名（DeviceTrack.Title）の自然順ソート（OrdinalIgnoreCase フォールバック付き）
        int titleComparison = NaturalStringComparer.Instance.Compare(left.Title, right.Title);
        if (titleComparison != 0)
        {
            return titleComparison;
        }

        // タイブレーカー: 複数フォルダ結合アルバムで同名ファイルが存在する場合もパス自然順で確定的に整列する
        return NaturalStringComparer.Instance.Compare(left.Path, right.Path);
    }

    private readonly struct KeyedDeviceTrack
    {
        public KeyedDeviceTrack(DeviceTrack? track, bool hasTrackNumber, uint trackNumber, string title, string path)
        {
            Track = track;
            HasTrackNumber = hasTrackNumber;
            TrackNumber = trackNumber;
            Title = title;
            Path = path;
        }

        public DeviceTrack? Track { get; }

        public bool HasTrackNumber { get; }

        public uint TrackNumber { get; }

        public string Title { get; }

        public string Path { get; }
    }

    /// <summary>
    /// PC側アルバム群からデバイス楽曲のトラック番号を高速検索するためのルックアップインデックス
    /// </summary>
    private sealed class PcAlbumTrackIndex
    {
        private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());

        private static readonly (bool ReplaceInvalidWithUnderscore, bool TrimTrailingDots)[] DirectorySanitizeModes =
        {
            (true, true),   // 新規則: 禁止文字 _ 置換 + 末尾ピリオド除去（正規新配置）
            (false, true),  // 互換: 旧禁止文字除去 + 末尾ピリオド除去
            (true, false),  // 互換: 禁止文字 _ 置換 + 旧末尾ピリオド保持
            (false, false)  // 互換: 旧禁止文字除去 + 旧末尾ピリオド保持
        };

        private readonly Dictionary<ArtistTitleKey, TrackNumberLookup> _byArtistAndTitle = new(ArtistTitleKeyComparer.Instance);
        private readonly Dictionary<ArtistTitleKey, TrackNumberLookup> _byPerformerAndTitle = new(ArtistTitleKeyComparer.Instance);
        private readonly Dictionary<string, TrackNumberLookup> _byTitle = new(StringComparer.OrdinalIgnoreCase);

        public static PcAlbumTrackIndex? Create(IReadOnlyList<Album>? pcAlbums)
        {
            if (pcAlbums == null || pcAlbums.Count == 0)
            {
                return null;
            }

            var index = new PcAlbumTrackIndex();
            bool hasAnyTrack = false;

            foreach (var album in pcAlbums)
            {
                if (album == null)
                {
                    continue;
                }

                var titleKeys = BuildNormalizedFolderKeys(album.Title);
                if (titleKeys.Count == 0)
                {
                    continue;
                }

                var artistKeys = BuildNormalizedFolderKeys(album.Artist);

                // TrackNumber == 0 のみのアルバムも既知の (Artist, Title) として登録し、
                // 同名タイトルの別アーティストアルバムへ誤フォールバックしないようにする
                foreach (var titleKey in titleKeys)
                {
                    foreach (var artistKey in artistKeys)
                    {
                        var pairKey = new ArtistTitleKey(artistKey, titleKey);
                        if (!index._byArtistAndTitle.ContainsKey(pairKey))
                        {
                            index._byArtistAndTitle[pairKey] = new TrackNumberLookup();
                        }
                    }
                }

                if (album.Tracks == null || album.Tracks.Count == 0)
                {
                    continue;
                }

                foreach (var track in album.Tracks)
                {
                    if (track == null || track.TrackNumber == 0 || string.IsNullOrWhiteSpace(track.FilePath))
                    {
                        continue;
                    }

                    hasAnyTrack = true;

                    foreach (var titleKey in titleKeys)
                    {
                        if (!index._byTitle.TryGetValue(titleKey, out var titleLookup))
                        {
                            titleLookup = new TrackNumberLookup();
                            index._byTitle[titleKey] = titleLookup;
                        }
                        titleLookup.Register(track);

                        foreach (var artistKey in artistKeys)
                        {
                            var pairKey = new ArtistTitleKey(artistKey, titleKey);
                            index._byArtistAndTitle[pairKey].Register(track);
                        }
                    }

                    // 旧曲別アーティスト配置（Track.Artist 配下）との互換照合用に補助登録する
                    if (!string.IsNullOrWhiteSpace(track.Artist)
                        && !string.Equals(track.Artist.Trim(), album.Artist?.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        var performerKeys = BuildNormalizedFolderKeys(track.Artist);
                        foreach (var titleKey in titleKeys)
                        {
                            foreach (var performerKey in performerKeys)
                            {
                                var performerPairKey = new ArtistTitleKey(performerKey, titleKey);
                                if (!index._byPerformerAndTitle.TryGetValue(performerPairKey, out var performerLookup))
                                {
                                    performerLookup = new TrackNumberLookup();
                                    index._byPerformerAndTitle[performerPairKey] = performerLookup;
                                }
                                performerLookup.Register(track);
                            }
                        }
                    }
                }
            }

            return hasAnyTrack ? index : null;
        }

        public bool TryGetTrackNumber(DeviceAlbum album, DeviceTrack track, out uint trackNumber)
        {
            trackNumber = 0;
            if (album == null || track == null || string.IsNullOrWhiteSpace(album.Title))
            {
                return false;
            }

            string trimmedTitle = album.Title.Trim();
            string trimmedArtist = album.Artist?.Trim() ?? string.Empty;

            if (!IsAmbiguousArtist(trimmedArtist))
            {
                var artistTitleKey = new ArtistTitleKey(trimmedArtist, trimmedTitle);

                // (Artist, Title) が既知のPC側アルバムと一致した場合はその結果で確定し、
                // 対象曲が TrackNumber == 0 または端末独自ファイルでも他アーティストの _byTitle へフォールバックしない
                if (_byArtistAndTitle.TryGetValue(artistTitleKey, out var pairLookup))
                {
                    return pairLookup.TryGetTrackNumber(track, out trackNumber);
                }

                // 旧曲別アーティスト配置（Track.Artist 配下）での一致を確認する
                if (_byPerformerAndTitle.TryGetValue(artistTitleKey, out var performerLookup))
                {
                    return performerLookup.TryGetTrackNumber(track, out trackNumber);
                }

                // 明確な別アーティスト（PC未登録）の場合は同名タイトルの他アーティストへ誤フォールバックしない
                return false;
            }

            // 複数フォルダ結合（Various Artists）や Unknown Artist / 空文字の場合:
            // まずトラックの実パスから親アーティストフォルダ名を抽出して高精度照合する
            string trackArtist = ExtractParentArtistName(track.Path);
            if (!IsAmbiguousArtist(trackArtist))
            {
                var trackArtistKey = new ArtistTitleKey(trackArtist, trimmedTitle);
                if (_byArtistAndTitle.TryGetValue(trackArtistKey, out var trackPairLookup))
                {
                    return trackPairLookup.TryGetTrackNumber(track, out trackNumber);
                }

                if (_byPerformerAndTitle.TryGetValue(trackArtistKey, out var trackPerformerLookup))
                {
                    return trackPerformerLookup.TryGetTrackNumber(track, out trackNumber);
                }
            }

            // アーティスト名が特定できない結合アルバム等に限定してタイトル一致へフォールバックする
            if (_byTitle.TryGetValue(trimmedTitle, out var titleLookup)
                && titleLookup.TryGetTrackNumber(track, out trackNumber))
            {
                return true;
            }

            return false;
        }

        private static bool IsAmbiguousArtist(string? artist)
            => string.IsNullOrWhiteSpace(artist)
                || string.Equals(artist, "Unknown Artist", StringComparison.OrdinalIgnoreCase)
                || string.Equals(artist, "Various Artists", StringComparison.OrdinalIgnoreCase);

        private static string ExtractParentArtistName(string? trackPath)
        {
            if (string.IsNullOrWhiteSpace(trackPath))
            {
                return string.Empty;
            }

            string normalized = trackPath.Replace('/', '\\');
            string albumDir = GetParentDirectory(normalized);
            if (string.IsNullOrEmpty(albumDir) || albumDir == @"\")
            {
                return string.Empty;
            }

            string artistDir = GetParentDirectory(albumDir);
            if (string.IsNullOrEmpty(artistDir) || artistDir == @"\")
            {
                return string.Empty;
            }

            return Path.GetFileName(artistDir) ?? string.Empty;
        }

        private static List<string> BuildNormalizedFolderKeys(string? rawName)
        {
            var keys = new List<string>(DirectorySanitizeModes.Length + 1);
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return keys;
            }

            string trimmed = rawName.Trim();
            AddUniqueKey(keys, trimmed);

            foreach (var (replaceInvalidWithUnderscore, trimTrailingDots) in DirectorySanitizeModes)
            {
                string sanitized = SanitizeFolderName(trimmed, replaceInvalidWithUnderscore, trimTrailingDots);
                AddUniqueKey(keys, sanitized);
            }

            return keys;
        }

        private static void AddUniqueKey(List<string> keys, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            keys.Add(candidate);
        }

        private static string SanitizeFolderName(string trimmedInput, bool replaceInvalidWithUnderscore, bool trimTrailingDots)
        {
            string cleaned;
            if (replaceInvalidWithUnderscore)
            {
                var chars = new char[trimmedInput.Length];
                for (int i = 0; i < trimmedInput.Length; i++)
                {
                    char c = trimmedInput[i];
                    chars[i] = InvalidFileNameChars.Contains(c) ? '_' : c;
                }

                cleaned = new string(chars).Trim();
            }
            else
            {
                var buffer = new char[trimmedInput.Length];
                int length = 0;
                for (int i = 0; i < trimmedInput.Length; i++)
                {
                    char c = trimmedInput[i];
                    if (!InvalidFileNameChars.Contains(c))
                    {
                        buffer[length++] = c;
                    }
                }

                cleaned = new string(buffer, 0, length).Trim();
            }

            int end = cleaned.Length;
            while (end > 0 && (cleaned[end - 1] == '.' || char.IsWhiteSpace(cleaned[end - 1])))
            {
                end--;
            }

            if (end == 0)
            {
                return "Unknown";
            }

            return trimTrailingDots ? cleaned.Substring(0, end) : cleaned;
        }

        private readonly struct ArtistTitleKey
        {
            public ArtistTitleKey(string artist, string title)
            {
                Artist = artist;
                Title = title;
            }

            public string Artist { get; }

            public string Title { get; }
        }

        private sealed class ArtistTitleKeyComparer : IEqualityComparer<ArtistTitleKey>
        {
            public static readonly ArtistTitleKeyComparer Instance = new();

            public bool Equals(ArtistTitleKey x, ArtistTitleKey y)
                => string.Equals(x.Artist, y.Artist, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode(ArtistTitleKey obj)
                => HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Artist ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Title ?? string.Empty));
        }

        private sealed class TrackNumberLookup
        {
            private readonly Dictionary<string, uint> _byFileName = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, uint> _byFileNameWithoutExtension = new(StringComparer.OrdinalIgnoreCase);

            public void Register(Track track)
            {
                string fileName = Path.GetFileName(track.FilePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    RegisterKey(_byFileName, fileName, track.TrackNumber);
                }

                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(track.FilePath);
                if (!string.IsNullOrWhiteSpace(fileNameWithoutExt))
                {
                    RegisterKey(_byFileNameWithoutExtension, fileNameWithoutExt, track.TrackNumber);
                }
            }

            public bool TryGetTrackNumber(DeviceTrack deviceTrack, out uint trackNumber)
            {
                trackNumber = 0;
                string candidateName = !string.IsNullOrWhiteSpace(deviceTrack.Title)
                    ? deviceTrack.Title
                    : Path.GetFileName(deviceTrack.Path ?? string.Empty);

                if (string.IsNullOrWhiteSpace(candidateName))
                {
                    return false;
                }

                if (_byFileName.TryGetValue(candidateName, out trackNumber))
                {
                    return true;
                }

                string nameWithoutExt = Path.GetFileNameWithoutExtension(candidateName);
                if (!string.IsNullOrWhiteSpace(nameWithoutExt)
                    && _byFileNameWithoutExtension.TryGetValue(nameWithoutExt, out trackNumber))
                {
                    return true;
                }

                return false;
            }

            private static void RegisterKey(Dictionary<string, uint> map, string key, uint trackNumber)
            {
                if (!map.TryGetValue(key, out uint existing) || trackNumber < existing)
                {
                    map[key] = trackNumber;
                }
            }
        }
    }

    /// <summary>
    /// 文字列中の連続数字を数値昇順として比較するゼロアロケーションの自然順比較子
    /// </summary>
    private sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int ix = 0;
            int iy = 0;
            int xLen = x.Length;
            int yLen = y.Length;

            while (ix < xLen && iy < yLen)
            {
                char cx = x[ix];
                char cy = y[iy];

                if (char.IsAsciiDigit(cx) && char.IsAsciiDigit(cy))
                {
                    int xEnd = ix;
                    while (xEnd < xLen && char.IsAsciiDigit(x[xEnd]))
                    {
                        xEnd++;
                    }

                    int yEnd = iy;
                    while (yEnd < yLen && char.IsAsciiDigit(y[yEnd]))
                    {
                        yEnd++;
                    }

                    int xNonZero = ix;
                    while (xNonZero < xEnd && x[xNonZero] == '0')
                    {
                        xNonZero++;
                    }

                    int yNonZero = iy;
                    while (yNonZero < yEnd && y[yNonZero] == '0')
                    {
                        yNonZero++;
                    }

                    int xSigLen = xEnd - xNonZero;
                    int ySigLen = yEnd - yNonZero;
                    if (xSigLen != ySigLen)
                    {
                        return xSigLen.CompareTo(ySigLen);
                    }

                    for (int k = 0; k < xSigLen; k++)
                    {
                        char dx = x[xNonZero + k];
                        char dy = y[yNonZero + k];
                        if (dx != dy)
                        {
                            return dx.CompareTo(dy);
                        }
                    }

                    ix = xEnd;
                    iy = yEnd;
                }
                else
                {
                    char ux = char.ToUpperInvariant(cx);
                    char uy = char.ToUpperInvariant(cy);
                    if (ux != uy)
                    {
                        return ux.CompareTo(uy);
                    }

                    ix++;
                    iy++;
                }
            }

            if (ix < xLen)
            {
                return 1;
            }

            if (iy < yLen)
            {
                return -1;
            }

            int fallback = StringComparer.OrdinalIgnoreCase.Compare(x, y);
            if (fallback != 0)
            {
                return fallback;
            }

            return StringComparer.Ordinal.Compare(x, y);
        }
    }

    private void ScanFileSystemDirectory(string path, List<DeviceAlbum> loadedAlbums)
    {
        try
        {
            var files = Directory.GetFiles(path);
            var audioFiles = files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower(System.Globalization.CultureInfo.InvariantCulture))).ToList();

            if (audioFiles.Count > 0)
            {
                string albumName = Path.GetFileName(path) ?? "Unknown Album";
                string? dirName = Path.GetDirectoryName(path);
                string artistName = (!string.IsNullOrEmpty(dirName) ? Path.GetFileName(dirName) : null) ?? "Unknown Artist";

                var existingAlbum = loadedAlbums.FirstOrDefault(a => string.Equals(a.Title, albumName, StringComparison.OrdinalIgnoreCase));
                if (existingAlbum != null)
                {
                    if (existingAlbum.Artist != artistName && !existingAlbum.Artist.Contains("Various Artists"))
                    {
                        existingAlbum.Artist = "Various Artists";
                    }
                    foreach (var file in audioFiles)
                    {
                        existingAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                    }
                    Logger.Info($"[FS] Appended {audioFiles.Count} tracks to existing album: {albumName}");
                }
                else
                {
                    var deviceAlbum = new DeviceAlbum
                    {
                        Artist = artistName,
                        Title = albumName,
                        Path = path
                    };
                    foreach (var file in audioFiles)
                    {
                        deviceAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                    }
                    loadedAlbums.Add(deviceAlbum);
                    Logger.Info($"[FS] Added new FS album: {albumName} with {audioFiles.Count} tracks.");
                }
            }
        }
        catch (Exception ex) { Logger.Error(ex, "Error scanning directory"); }

        try
        {
            var dirs = Directory.GetDirectories(path);
            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name == "System Volume Information" || name == "$RECYCLE.BIN") continue;
                ScanFileSystemDirectory(dir, loadedAlbums);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error scanning FS dirs in {path}: {ex.Message}"); }
    }

    private void ScanMtpDirectory(string path, List<DeviceAlbum> loadedAlbums)
    {
        var mtp = _currentDevice?.MtpDevice;
        if (mtp == null)
        {
            return;
        }

        try
        {
            var files = mtp.GetFiles(path);
            var audioFiles = files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower(System.Globalization.CultureInfo.InvariantCulture))).ToList();

            if (audioFiles.Count > 0)
            {
                string albumName = Path.GetFileName(path) ?? "Unknown Album";
                string parentDir = GetParentDirectory(path);
                string artistName = (!string.IsNullOrEmpty(parentDir) ? Path.GetFileName(parentDir) : null) ?? "Unknown Artist";

                var existingAlbum = loadedAlbums.FirstOrDefault(a => string.Equals(a.Title, albumName, StringComparison.OrdinalIgnoreCase));
                if (existingAlbum != null)
                {
                    if (existingAlbum.Artist != artistName && !existingAlbum.Artist.Contains("Various Artists"))
                    {
                        existingAlbum.Artist = "Various Artists";
                    }
                    foreach (var file in audioFiles)
                    {
                        existingAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                    }
                    Logger.Info($"[MTP] Appended {audioFiles.Count} tracks to existing album: {albumName}");
                }
                else
                {
                    var deviceAlbum = new DeviceAlbum
                    {
                        Artist = artistName,
                        Title = albumName,
                        Path = path
                    };
                    foreach (var file in audioFiles)
                    {
                        deviceAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                    }
                    loadedAlbums.Add(deviceAlbum);
                    Logger.Info($"[MTP] Added new MTP album: {albumName} with {audioFiles.Count} tracks.");
                }
            }
        }
        catch (Exception ex) { Logger.Error(ex, "Error scanning directory"); }

        try
        {
            var dirs = mtp.GetDirectories(path);
            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name == "Android" || name == "LOST.DIR" || name == "System Volume Information" || name == "Alarms" || name == "Notifications" || name == "Ringtones" || name == "Podcasts") continue;
                ScanMtpDirectory(dir, loadedAlbums);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error scanning MTP dirs in {path}: {ex.Message}"); }
    }

    private async void ExecuteDeleteTrack(object? parameter)
    {
        if (parameter is DeviceTrack track)
        {
            var result = MessageBox.Show($"Are you sure you want to delete '{track.Title}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                IsLoading = true;
                bool deleteSuccess = false;
                await Task.Run(() =>
                {
                    try
                    {
                        if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem)
                        {
                            if (File.Exists(track.Path))
                                File.Delete(track.Path);
                        }
                        else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null)
                        {
                            _currentDevice.MtpDevice.DeleteFile(track.Path);
                        }
                        deleteSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error deleting file: {ex.Message}"));
                    }
                });

                if (deleteSuccess)
                {
                    // Remove from UI
                    foreach (var album in DeviceAlbums)
                    {
                        if (album.Tracks.Remove(track))
                        {
                            if (album.Tracks.Count == 0)
                            {
                                await ExecuteDeleteAlbum(album, skipConfirm: true);
                            }
                            break;
                        }
                    }
                }
                IsLoading = false;
            }
        }
    }

    private async void ExecuteDeleteAlbum(object? parameter)
    {
        await ExecuteDeleteAlbum(parameter, false);
    }

    private async Task ExecuteDeleteAlbum(object? parameter, bool skipConfirm)
    {
        if (parameter is DeviceAlbum album)
        {
            if (!skipConfirm)
            {
                var result = MessageBox.Show($"Are you sure you want to delete the entire album '{album.Title}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            IsLoading = true;
            bool deleteSuccess = false;
            await Task.Run(() =>
            {
                try
                {
                    if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem)
                    {
                        var artistPaths = album.Tracks.Select(t => Path.GetDirectoryName(t.Path)).Distinct().ToList();
                        foreach (var path in artistPaths)
                        {
                            if (Directory.Exists(path))
                                Directory.Delete(path, true);

                            string? pArtistPath = Path.GetDirectoryName(path);
                            if (pArtistPath != null && Directory.Exists(pArtistPath) && !Directory.EnumerateFileSystemEntries(pArtistPath).Any())
                            {
                                Directory.Delete(pArtistPath);
                            }
                        }
                    }
                    else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null)
                    {
                        var artistPaths = album.Tracks.Select(t => GetParentDirectory(t.Path)).Distinct().ToList();
                        foreach (var path in artistPaths)
                        {
                            if (path != null)
                            {
                                _currentDevice.MtpDevice.DeleteDirectory(path, true);

                                string pArtistPath = GetParentDirectory(path);
                                if (!string.IsNullOrEmpty(pArtistPath))
                                {
                                    var remainingItems = _currentDevice.MtpDevice.GetDirectories(pArtistPath);
                                    var remainingFiles = _currentDevice.MtpDevice.GetFiles(pArtistPath);
                                    if (remainingItems.Length == 0 && remainingFiles.Length == 0)
                                    {
                                        _currentDevice.MtpDevice.DeleteDirectory(pArtistPath, true);
                                    }
                                }
                            }
                        }
                    }
                    deleteSuccess = true;
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error deleting album: {ex.Message}"));
                }
            });

            if (deleteSuccess)
            {
                DeviceAlbums.Remove(album);
            }
            IsLoading = false;
        }
    }

    private static string GetParentDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        path = path.TrimEnd('\\');
        int index = path.LastIndexOf('\\');
        if (index > 0)
            return path.Substring(0, index);
        if (index == 0)
            return @"\";
        return string.Empty;
    }
}
