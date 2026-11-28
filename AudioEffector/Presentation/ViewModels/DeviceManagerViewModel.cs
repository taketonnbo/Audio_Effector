using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using Microsoft.Win32;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 外部デバイスの楽曲・アルバム管理（一覧表示・削除・PC への逆転送インポート）を行うViewModel
/// </summary>
public class DeviceManagerViewModel : ViewModelBase, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ISettingsService? _settingsService;
    private readonly Action<string>? _onLibraryRefreshRequested;
    private readonly Func<MainViewModel.DeviceViewModel, DeviceBrowserViewModel.IMtpDeviceClient?> _resolveMtpClient;
    private readonly Func<string?, string?> _selectDestinationFolder;
    private readonly Action<TransferResultSummary> _showImportResult;
    private readonly ExplicitImportRelayCommand _cancelImportCommand;
    private MainViewModel.DeviceViewModel _currentDevice;
    private string _basePath;
    private List<Album> _pcAlbums;
    private CancellationTokenSource? _importCts;
    private Task? _activeImportTask;
    private volatile bool _isDisposed;
    private volatile bool _suppressImportResultNotification;

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

    private bool _isImporting;

    /// <summary>
    /// 接続機器から PC への逆転送（インポート）を実行中かどうかを取得または設定します
    /// </summary>
    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetProperty(ref _isImporting, value))
            {
                NotifyImportCommandStateChanged();
            }
        }
    }

    private double _importProgress;

    /// <summary>
    /// 逆転送（インポート）の進捗率（0〜100）を取得または設定します
    /// </summary>
    public double ImportProgress
    {
        get => _importProgress;
        set
        {
            if (SetProperty(ref _importProgress, value))
            {
                OnPropertyChanged(nameof(ImportProgressCountText));
            }
        }
    }

    private int _importCurrentCount;

    /// <summary>
    /// 逆転送処理中または完了済みの曲数を取得します
    /// </summary>
    public int ImportCurrentCount
    {
        get => _importCurrentCount;
        private set
        {
            if (SetProperty(ref _importCurrentCount, value))
            {
                OnPropertyChanged(nameof(ImportProgressCountText));
            }
        }
    }

    private int _importTotalCount;

    /// <summary>
    /// 逆転送対象の総曲数を取得します
    /// </summary>
    public int ImportTotalCount
    {
        get => _importTotalCount;
        private set
        {
            if (SetProperty(ref _importTotalCount, value))
            {
                OnPropertyChanged(nameof(ImportProgressCountText));
            }
        }
    }

    private string _currentImportFileName = string.Empty;

    /// <summary>
    /// 現在逆転送処理中のファイル名を取得します
    /// </summary>
    public string CurrentImportFileName
    {
        get => _currentImportFileName;
        private set => SetProperty(ref _currentImportFileName, value);
    }

    private string _importPhaseText = string.Empty;

    /// <summary>
    /// 現在の逆転送フェーズテキストを取得します
    /// </summary>
    public string ImportPhaseText
    {
        get => _importPhaseText;
        private set => SetProperty(ref _importPhaseText, value);
    }

    private bool _isCancellingImport;

    /// <summary>
    /// 逆転送キャンセル処理中かどうかを取得します（二重押下防止用）
    /// </summary>
    public bool IsCancellingImport
    {
        get => _isCancellingImport;
        internal set
        {
            if (SetProperty(ref _isCancellingImport, value))
            {
                NotifyImportCommandStateChanged();
            }
        }
    }

    /// <summary>
    /// 逆転送の件数およびパーセント表示文字列を取得します
    /// </summary>
    public string ImportProgressCountText => ImportTotalCount > 0
        ? $"{ImportCurrentCount} / {ImportTotalCount} 曲 ({ImportProgress:0}%)"
        : $"{ImportProgress:0}%";

    /// <summary>
    /// トラック削除コマンドを取得します
    /// </summary>
    public ICommand DeleteTrackCommand { get; }

    /// <summary>
    /// アルバム削除コマンドを取得します
    /// </summary>
    public ICommand DeleteAlbumCommand { get; }

    /// <summary>
    /// アルバム単位で PC へ取り込む（逆転送）コマンドを取得します
    /// </summary>
    public ICommand ImportAlbumCommand { get; }

    /// <summary>
    /// 曲単位で PC へ取り込む（逆転送）コマンドを取得します
    /// </summary>
    public ICommand ImportTrackCommand { get; }

    /// <summary>
    /// 実行中の逆転送（インポート）を安全に中止するコマンドを取得します
    /// </summary>
    public ICommand CancelImportCommand { get; }

    /// <summary>
    /// コンストラクタで開始された初回アルバム読み込みタスクを取得します
    /// </summary>
    internal Task InitialLoadTask { get; }

    /// <summary>
    /// 現在実行中の逆転送バックグラウンドタスクを取得します（未実行時は null）
    /// </summary>
    internal Task? ActiveImportTask => Volatile.Read(ref _activeImportTask);

    /// <summary>
    /// デバイス情報、ベースパス、PC側アルバム一覧を指定してインスタンスを初期化します
    /// </summary>
    /// <param name="device">対象の外部デバイス情報</param>
    /// <param name="basePath">同期ベースパス</param>
    /// <param name="pcAlbums">PC側のアルバム一覧</param>
    /// <param name="settingsService">アプリケーション設定サービス（既定保存先・ライブラリフォルダ参照用）</param>
    /// <param name="onLibraryRefreshRequested">ライブラリフォルダ配下へ取り込み完了した際に再スキャンを要求するコールバック</param>
    public DeviceManagerViewModel(
        MainViewModel.DeviceViewModel device,
        string basePath,
        List<Album> pcAlbums,
        ISettingsService? settingsService = null,
        Action<string>? onLibraryRefreshRequested = null)
        : this(
            device,
            basePath,
            pcAlbums,
            settingsService,
            onLibraryRefreshRequested,
            resolveMtpClient: null,
            selectDestinationFolder: null,
            showImportResult: null)
    {
    }

    /// <summary>
    /// テスト差し替え用の MTP クライアント・フォルダ選択・結果通知デリゲートを含む内部コンストラクタ
    /// </summary>
    /// <param name="device">対象の外部デバイス情報</param>
    /// <param name="basePath">同期ベースパス</param>
    /// <param name="pcAlbums">PC側のアルバム一覧</param>
    /// <param name="settingsService">アプリケーション設定サービス</param>
    /// <param name="onLibraryRefreshRequested">PC ライブラリ再スキャン要求コールバック</param>
    /// <param name="resolveMtpClient">MTP クライアント解決関数</param>
    /// <param name="selectDestinationFolder">保存先フォルダ選択関数</param>
    /// <param name="showImportResult">逆転送結果サマリ表示関数</param>
    internal DeviceManagerViewModel(
        MainViewModel.DeviceViewModel device,
        string basePath,
        List<Album> pcAlbums,
        ISettingsService? settingsService,
        Action<string>? onLibraryRefreshRequested,
        Func<MainViewModel.DeviceViewModel, DeviceBrowserViewModel.IMtpDeviceClient?>? resolveMtpClient,
        Func<string?, string?>? selectDestinationFolder = null,
        Action<TransferResultSummary>? showImportResult = null)
    {
        _currentDevice = device;
        _basePath = basePath;
        _pcAlbums = pcAlbums;
        _settingsService = settingsService;
        _onLibraryRefreshRequested = onLibraryRefreshRequested;
        _resolveMtpClient = resolveMtpClient ?? DeviceBrowserViewModel.DefaultResolveMtpClient;
        _selectDestinationFolder = selectDestinationFolder ?? DefaultSelectDestinationFolder;
        _showImportResult = showImportResult ?? DefaultShowImportResult;

        DeleteTrackCommand = new RelayCommand(ExecuteDeleteTrack, _ => !IsImporting);
        DeleteAlbumCommand = new RelayCommand(ExecuteDeleteAlbum, _ => !IsImporting);
        ImportAlbumCommand = new RelayCommand(ExecuteImportAlbum, _ => !IsImporting);
        ImportTrackCommand = new RelayCommand(ExecuteImportTrack, _ => !IsImporting);
        _cancelImportCommand = new ExplicitImportRelayCommand(() => CancelImport(), () => IsImporting && !IsCancellingImport);
        CancelImportCommand = _cancelImportCommand;

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

    /// <summary>
    /// 進行中の逆転送（インポート）処理を安全にキャンセル要求します
    /// </summary>
    /// <param name="suppressResultDialog">ダイアログ終了時など完了後の結果サマリ通知・再スキャンを抑止する場合は true</param>
    public void CancelImport(bool suppressResultDialog = false)
    {
        if (suppressResultDialog)
        {
            _suppressImportResultNotification = true;
        }

        var cts = Volatile.Read(ref _importCts);
        if (cts == null || !IsImporting)
        {
            return;
        }

        if (!IsCancellingImport)
        {
            IsCancellingImport = true;
            ImportPhaseText = "キャンセル中...";
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// 進行中の逆転送をキャンセルし、保持しているリソースを解放します
    /// </summary>
    public void Dispose()
    {
        _isDisposed = true;
        _suppressImportResultNotification = true;

        var cts = Interlocked.Exchange(ref _importCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 指定された対象パスが基準ディレクトリ自体またはそのサブディレクトリ配下に属するかどうかを判定します
    /// </summary>
    /// <param name="targetPath">判定対象のパス</param>
    /// <param name="baseDirectory">基準ディレクトリパス</param>
    /// <returns>配下（または同一ディレクトリ）である場合は true</returns>
    internal static bool IsPathWithinDirectory(string? targetPath, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        try
        {
            string fullTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullBase = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(fullTarget, fullBase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string prefix = fullBase + Path.DirectorySeparatorChar;
            return fullTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async void ExecuteImportAlbum(object? parameter)
    {
        if (parameter is DeviceAlbum album)
        {
            await ImportAlbumAsync(album);
        }
    }

    private async void ExecuteImportTrack(object? parameter)
    {
        if (parameter is DeviceTrack track)
        {
            await ImportTrackAsync(track);
        }
    }

    /// <summary>
    /// 指定されたデバイスアルバム内の全楽曲を PC の指定フォルダへ逆転送（インポート）します
    /// </summary>
    /// <param name="album">取り込み対象のデバイスアルバム</param>
    /// <returns>逆転送結果サマリ（キャンセル・未実行時は null）</returns>
    internal async Task<TransferResultSummary?> ImportAlbumAsync(DeviceAlbum album)
    {
        if (album == null || album.Tracks.Count == 0 || IsImporting || IsLoading)
        {
            return null;
        }

        var items = album.Tracks.Select(t => (Album: album, Track: t)).ToList();
        return await ExecuteImportItemsAsync(items);
    }

    /// <summary>
    /// 指定されたデバイストラック 1 曲を PC の指定フォルダへ逆転送（インポート）します
    /// </summary>
    /// <param name="track">取り込み対象のデバイストラック</param>
    /// <returns>逆転送結果サマリ（キャンセル・未実行時は null）</returns>
    internal async Task<TransferResultSummary?> ImportTrackAsync(DeviceTrack track)
    {
        if (track == null || IsImporting || IsLoading)
        {
            return null;
        }

        var ownerAlbum = DeviceAlbums.FirstOrDefault(a => a.Tracks.Contains(track));
        var fallbackAlbum = ownerAlbum ?? new DeviceAlbum(
            title: !string.IsNullOrWhiteSpace(track.Album) ? track.Album : Path.GetFileName(GetParentDirectory(track.Path)),
            artist: !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist : Path.GetFileName(GetParentDirectory(GetParentDirectory(track.Path))),
            path: GetParentDirectory(track.Path));

        return await ExecuteImportItemsAsync(new[] { (Album: fallbackAlbum, Track: track) });
    }

    private async Task<TransferResultSummary?> ExecuteImportItemsAsync(IReadOnlyList<(DeviceAlbum Album, DeviceTrack Track)> items)
    {
        if (items.Count == 0 || _currentDevice == null || IsImporting)
        {
            return null;
        }

        string? initialFolder = ResolveDefaultImportDirectory();
        string? destinationRoot = _selectDestinationFolder(initialFolder);
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return null;
        }

        var importCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _importCts, importCts);
        if (previousCts != null)
        {
            try { previousCts.Cancel(); } catch (ObjectDisposedException) { }
            previousCts.Dispose();
        }

        _suppressImportResultNotification = false;
        IsImporting = true;
        IsCancellingImport = false;
        ImportProgress = 0;
        ImportCurrentCount = 0;
        ImportTotalCount = items.Count;
        CurrentImportFileName = string.Empty;
        ImportPhaseText = "取り込みを準備中...";

        TransferResultSummary? summary = null;
        Task<TransferResultSummary>? importTask = null;

        try
        {
            var detailProgress = new InlineImportProgress(ApplyImportProgress);
            importTask = ImportTracksToPcAsync(items, destinationRoot, detailProgress, importCts.Token);
            Volatile.Write(ref _activeImportTask, importTask);
            summary = await importTask;

            if (_isDisposed || _suppressImportResultNotification)
            {
                return summary;
            }

            if (summary.SuccessCount > 0 && ShouldRefreshLibrary(destinationRoot))
            {
                try
                {
                    _onLibraryRefreshRequested?.Invoke(destinationRoot);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "逆転送完了後のライブラリ再反映呼び出しに失敗しました。");
                }
            }

            _showImportResult(summary);
            return summary;
        }
        finally
        {
            if (importTask != null)
            {
                _ = Interlocked.CompareExchange(ref _activeImportTask, null, importTask);
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _importCts, null, importCts), importCts))
            {
                importCts.Dispose();
            }

            IsImporting = false;
            IsCancellingImport = false;
            ImportProgress = 0;
            ImportCurrentCount = 0;
            ImportTotalCount = 0;
            CurrentImportFileName = string.Empty;
            ImportPhaseText = string.Empty;
        }
    }

    /// <summary>
    /// デバイス上の楽曲一覧を PC 側の指定ルートフォルダ配下（Artist\Album\FileName）へ逆転送します
    /// </summary>
    /// <param name="items">取り込み対象の（アルバム, トラック）一覧</param>
    /// <param name="destinationRoot">PC 側の保存先ルートフォルダ</param>
    /// <param name="detailProgress">詳細進捗通知</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>逆転送結果サマリ</returns>
    internal Task<TransferResultSummary> ImportTracksToPcAsync(
        IReadOnlyList<(DeviceAlbum Album, DeviceTrack Track)> items,
        string destinationRoot,
        IProgress<TransferProgressInfo>? detailProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        return Task.Run(async () =>
        {
            var failedItems = new List<TransferFailedItem>();
            int total = items.Count;
            int processed = 0;
            int successCount = 0;
            int skippedCount = 0;
            bool wasCancelled = false;
            bool wasAbortedByFatalError = false;
            string? fatalErrorMessage = null;

            DeviceBrowserViewModel.IMtpDeviceClient? mtpClient = null;
            string? sourceRoot = !string.IsNullOrWhiteSpace(_basePath) ? _basePath : _currentDevice.RootPath;
            if (_currentDevice.Type == MainViewModel.DeviceType.MTP)
            {
                mtpClient = _resolveMtpClient(_currentDevice);
                if (mtpClient == null || !mtpClient.IsConnected)
                {
                    return new TransferResultSummary(
                        successCount: 0,
                        skippedCount: 0,
                        failedItems: failedItems,
                        wasAbortedByFatalError: true,
                        fatalErrorMessage: "MTP デバイスが接続されていません。",
                        operationLabel: "取り込み");
                }
            }
            else if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem
                && !string.IsNullOrWhiteSpace(sourceRoot)
                && !Directory.Exists(sourceRoot))
            {
                return new TransferResultSummary(
                    successCount: 0,
                    skippedCount: 0,
                    failedItems: failedItems,
                    wasAbortedByFatalError: true,
                    fatalErrorMessage: "接続ドライブまたは機器フォルダが見つかりません。",
                    operationLabel: "取り込み");
            }

            foreach (var (album, track) in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    break;
                }

                string fileName = ResolveImportFileName(track);
                string displayTitle = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : fileName;
                int currentDisplayIndex = processed + 1;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(track.Path) || string.IsNullOrWhiteSpace(fileName))
                    {
                        failedItems.Add(new TransferFailedItem(displayTitle, fileName, "デバイス上のファイルパスが無効です。"));
                        processed++;
                        double invalidPercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                        detailProgress?.Report(new TransferProgressInfo(processed, total, invalidPercent, fileName, "エラー"));
                        continue;
                    }

                    string artistFolder = DeviceBrowserViewModel.SanitizeFileName(ResolveImportArtistName(album, track));
                    string albumFolder = DeviceBrowserViewModel.SanitizeFileName(ResolveImportAlbumName(album, track));
                    string targetDir = Path.Combine(destinationRoot, artistFolder, albumFolder);
                    string destPath = Path.Combine(targetDir, fileName);

                    string fullTargetDir = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string fullDestPath = Path.GetFullPath(destPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!IsPathWithinDirectory(destPath, targetDir)
                        || string.Equals(fullDestPath, fullTargetDir, StringComparison.OrdinalIgnoreCase))
                    {
                        failedItems.Add(new TransferFailedItem(displayTitle, fileName, "保存先ファイルパスが無効です。"));
                        processed++;
                        double invalidDestPercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                        detailProgress?.Report(new TransferProgressInfo(processed, total, invalidDestPercent, fileName, "エラー"));
                        continue;
                    }

                    // 保存先に同名ファイルが既に存在する場合は重複スキップして意図しない上書きを防止する
                    if (File.Exists(destPath))
                    {
                        skippedCount++;
                        processed++;
                        double skipPercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                        detailProgress?.Report(new TransferProgressInfo(processed, total, skipPercent, fileName, "スキップ中..."));
                        continue;
                    }

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    double prePercent = total > 0 ? (double)processed / total * 100.0 : 0.0;
                    detailProgress?.Report(new TransferProgressInfo(currentDisplayIndex, total, prePercent, fileName, "取り込み中..."));

                    if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem)
                    {
                        if (!File.Exists(track.Path))
                        {
                            if (!string.IsNullOrWhiteSpace(sourceRoot) && !Directory.Exists(sourceRoot))
                            {
                                failedItems.Add(new TransferFailedItem(displayTitle, fileName, "接続ドライブまたは機器フォルダが見つかりません。"));
                                processed++;
                                double disconnectedPercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                                detailProgress?.Report(new TransferProgressInfo(processed, total, disconnectedPercent, fileName, "エラー"));
                                wasAbortedByFatalError = true;
                                fatalErrorMessage = "接続ドライブまたは機器フォルダにアクセスできなくなったため取り込みを中断しました。";
                                break;
                            }

                            failedItems.Add(new TransferFailedItem(displayTitle, fileName, "デバイス上のファイルが見つかりません。"));
                            processed++;
                            double missingPercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                            detailProgress?.Report(new TransferProgressInfo(processed, total, missingPercent, fileName, "エラー"));
                            continue;
                        }

                        await CopyFileToPcWithCancellationAsync(track.Path, destPath, cancellationToken).ConfigureAwait(false);
                    }
                    else if (_currentDevice.Type == MainViewModel.DeviceType.MTP)
                    {
                        DownloadMtpFileToPcWithCancellation(mtpClient!, track.Path, destPath, cancellationToken);
                    }

                    successCount++;
                    processed++;
                    double donePercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                    detailProgress?.Report(new TransferProgressInfo(processed, total, donePercent, fileName, "取り込み中..."));
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"デバイス楽曲の逆転送に失敗しました: {track.Path}");
                    failedItems.Add(new TransferFailedItem(displayTitle, fileName, ex.Message));
                    processed++;
                    double errPercent = total > 0 ? (double)processed / total * 100.0 : 100.0;
                    detailProgress?.Report(new TransferProgressInfo(processed, total, errPercent, fileName, "エラー"));

                    if (_currentDevice.Type == MainViewModel.DeviceType.MTP
                        && (mtpClient == null || !mtpClient.IsConnected || ex is System.Runtime.InteropServices.COMException))
                    {
                        wasAbortedByFatalError = true;
                        fatalErrorMessage = $"MTP デバイスとの通信が切断されたため取り込みを中断しました: {ex.Message}";
                        break;
                    }

                    if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem
                        && (ex is DriveNotFoundException
                            || !Directory.Exists(destinationRoot)
                            || (!string.IsNullOrWhiteSpace(sourceRoot) && !Directory.Exists(sourceRoot))))
                    {
                        wasAbortedByFatalError = true;
                        fatalErrorMessage = $"保存先フォルダまたは接続ドライブにアクセスできなくなったため取り込みを中断しました: {ex.Message}";
                        break;
                    }
                }
            }

            return new TransferResultSummary(
                successCount: successCount,
                skippedCount: skippedCount,
                failedItems: failedItems,
                wasCancelled: wasCancelled,
                wasAbortedByFatalError: wasAbortedByFatalError,
                fatalErrorMessage: fatalErrorMessage,
                operationLabel: "取り込み");
        });
    }

    private static async Task CopyFileToPcWithCancellationAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int COPY_BUFFER_SIZE = 64 * 1024;
        var buffer = new byte[COPY_BUFFER_SIZE];
        bool destCreated = false;
        bool completed = false;

        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, COPY_BUFFER_SIZE, useAsync: true);
            await using var destStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, COPY_BUFFER_SIZE, useAsync: true);
            destCreated = true;

            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            await destStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed && destCreated)
            {
                TryDeleteIncompleteFile(destinationPath);
            }
        }
    }

    private static void DownloadMtpFileToPcWithCancellation(
        DeviceBrowserViewModel.IMtpDeviceClient mtpClient,
        string sourceDevicePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        bool destCreated = false;
        bool completed = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var destStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                destCreated = true;
                mtpClient.DownloadFile(sourceDevicePath, destStream);
                destStream.Flush();
            }

            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
        }
        finally
        {
            if (!completed && destCreated)
            {
                TryDeleteIncompleteFile(destinationPath);
            }
        }
    }

    private static void TryDeleteIncompleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"不完全な逆転送ファイルの削除に失敗しました: {filePath}");
        }
    }

    /// <summary>
    /// デバイストラックから Windows ファイルシステムへ安全に保存可能なファイル名を解決します
    /// </summary>
    /// <param name="track">対象デバイストラック</param>
    /// <returns>サニタイズ済みファイル名（無効なパス・相対セグメントの場合は空文字列）</returns>
    internal static string ResolveImportFileName(DeviceTrack track)
    {
        if (track == null)
        {
            return string.Empty;
        }

        string raw = !string.IsNullOrWhiteSpace(track.Path)
            ? track.Path
            : (track.Title ?? string.Empty);

        string normalized = raw.Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.EndsWith('\\'))
        {
            return string.Empty;
        }

        int lastSep = normalized.LastIndexOf('\\');
        string segment = (lastSep >= 0 ? normalized[(lastSep + 1)..] : normalized).Trim();
        if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..")
        {
            return string.Empty;
        }

        int dotIndex = segment.LastIndexOf('.');
        string rawBase = dotIndex > 0 ? segment[..dotIndex] : segment;
        string rawExt = dotIndex > 0 ? segment[dotIndex..] : string.Empty;

        string sanitizedBase = SanitizeSinglePathSegment(rawBase).TrimEnd(' ', '.');
        string sanitizedExt = string.IsNullOrEmpty(rawExt)
            ? string.Empty
            : "." + SanitizeSinglePathSegment(rawExt[1..]).TrimEnd(' ', '.');
        if (sanitizedExt == ".")
        {
            sanitizedExt = string.Empty;
        }

        string combined = (sanitizedBase + sanitizedExt).TrimEnd(' ', '.');
        if (string.IsNullOrEmpty(sanitizedBase)
            || string.IsNullOrEmpty(combined)
            || combined == "."
            || combined == "..")
        {
            return string.Empty;
        }

        return combined;
    }

    private static string SanitizeSinglePathSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        return string.Create(value.Length, (value, invalidChars), static (span, state) =>
        {
            for (int i = 0; i < state.value.Length; i++)
            {
                char c = state.value[i];
                span[i] = Array.IndexOf(state.invalidChars, c) >= 0 || c == ':' ? '_' : c;
            }
        });
    }

    private static string ResolveImportArtistName(DeviceAlbum album, DeviceTrack track)
    {
        if (!string.IsNullOrWhiteSpace(album.Artist)
            && !string.Equals(album.Artist.Trim(), "Various Artists", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(album.Artist.Trim(), "Unknown Artist", StringComparison.OrdinalIgnoreCase))
        {
            return album.Artist.Trim();
        }

        string albumDir = GetParentDirectory(track.Path);
        string artistDir = GetParentDirectory(albumDir);
        string fromPath = !string.IsNullOrEmpty(artistDir) ? Path.GetFileName(artistDir) : string.Empty;
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        if (!string.IsNullOrWhiteSpace(album.Artist))
        {
            return album.Artist.Trim();
        }

        return !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist.Trim() : "Unknown Artist";
    }

    private static string ResolveImportAlbumName(DeviceAlbum album, DeviceTrack track)
    {
        if (!string.IsNullOrWhiteSpace(album.Title))
        {
            return album.Title.Trim();
        }

        string albumDir = GetParentDirectory(track.Path);
        string fromPath = !string.IsNullOrEmpty(albumDir) ? Path.GetFileName(albumDir) : string.Empty;
        return !string.IsNullOrWhiteSpace(fromPath) ? fromPath : "Unknown Album";
    }

    private string? ResolveDefaultImportDirectory()
    {
        try
        {
            string? libraryPath = _settingsService?.LoadSettings()?.LastLibraryPath;
            if (!string.IsNullOrWhiteSpace(libraryPath) && Directory.Exists(libraryPath))
            {
                return libraryPath;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "既定の取り込み先ライブラリフォルダ取得に失敗しました。");
        }

        string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return !string.IsNullOrWhiteSpace(myMusic) && Directory.Exists(myMusic) ? myMusic : null;
    }

    private bool ShouldRefreshLibrary(string destinationRoot)
    {
        if (_onLibraryRefreshRequested == null)
        {
            return false;
        }

        if (_settingsService == null)
        {
            return true;
        }

        try
        {
            string? libraryPath = _settingsService.LoadSettings()?.LastLibraryPath;
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                return false;
            }

            return IsPathWithinDirectory(destinationRoot, libraryPath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "ライブラリフォルダ配下判定に失敗しました。");
            return false;
        }
    }

    private void ApplyImportProgress(TransferProgressInfo info)
    {
        RunOnUiThread(() =>
        {
            if (!IsImporting)
            {
                return;
            }

            ImportCurrentCount = info.CurrentCount;
            ImportTotalCount = info.TotalCount;
            ImportProgress = info.Percent;
            CurrentImportFileName = info.CurrentFileName;
            if (!IsCancellingImport)
            {
                ImportPhaseText = info.PhaseText;
            }
        });
    }

    private void NotifyImportCommandStateChanged()
    {
        RunOnUiThread(() =>
        {
            _cancelImportCommand?.RaiseCanExecuteChanged();
            CommandManager.InvalidateRequerySuggested();
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }
        else
        {
            action();
        }
    }

    private static string? DefaultSelectDestinationFolder(string? initialDirectory)
    {
        string? selectedPath = null;
        RunOnUiThread(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = "PC への取り込み先フォルダを選択してください"
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                selectedPath = dialog.FolderName;
            }
        });

        return selectedPath;
    }

    private static void DefaultShowImportResult(TransferResultSummary summary)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        void ShowCore()
        {
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            var dialog = new Views.TransferResultDialog(summary);
            var activeWindow = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
                ?? System.Windows.Application.Current?.MainWindow;
            if (activeWindow != null && activeWindow.IsVisible)
            {
                dialog.Owner = activeWindow;
            }

            dialog.ShowDialog();
        }

        if (dispatcher.CheckAccess())
        {
            ShowCore();
        }
        else
        {
            dispatcher.Invoke(ShowCore);
        }
    }

    private sealed class InlineImportProgress : IProgress<TransferProgressInfo>
    {
        private readonly Action<TransferProgressInfo> _handler;

        public InlineImportProgress(Action<TransferProgressInfo> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(TransferProgressInfo value) => _handler(value);
    }

    private sealed class ExplicitImportRelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public ExplicitImportRelayCommand(Action execute, Func<bool> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? throw new ArgumentNullException(nameof(canExecute));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute();

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
