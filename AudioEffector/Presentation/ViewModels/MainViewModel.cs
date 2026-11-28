using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Logging;
using AudioEffector.Presentation.Views;
using MediaDevices;
using Microsoft.Win32;
using NLog;

namespace AudioEffector.Presentation.ViewModels
{
    /// <summary>
    /// アプリケーションのメインViewModel。
    /// UIのロジック、データバインディング、およびサービス間の連携を担当します。
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable, IShortcutCommandTarget
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string[] SupportedAudioExtensions = [".mp3", ".wav", ".aiff", ".wma", ".m4a", ".mp4", ".flac", ".aac", ".ogg", ".opus", ".alac"];
        private static readonly string[] LosslessAudioExtensions = [".flac", ".wav", ".aiff", ".alac"];
        private readonly IAudioService _audioService;
        private readonly AudioService? _fallbackAudioService;
        private readonly LibraryApplicationService? _libraryService;
        private readonly ISettingsService _settingsService;
        private readonly IDataDirectoryProvider? _dataDirectoryProvider;
        private readonly IDataDirectoryMigrationService? _dataDirectoryMigrationService;
        private readonly ILogConfigurationManager? _logConfigurationManager;
        private readonly Infrastructure.Repository.JsonSettingsRepository? _fallbackSettingsRepository;
        private readonly SettingsApplicationService? _fallbackSettings;
        private readonly bool _ownsAllSongs;
        private readonly bool _ownsArtists;
        private readonly bool _ownsRecent;
        private readonly bool _ownsAlbumDetail;
        private readonly bool _ownsLyrics;
        private readonly bool _ownsLrcGenerator;
        private readonly bool _ownsListeningStats;
        private ListeningStatsApplicationService? _fallbackListeningStatsService;
        private RightExtensionPanelType _activeRightExtensionPanel = RightExtensionPanelType.None;
        private Track? _selectedTagsTargetTrack;
        private RightExtensionCommand? _toggleAlbumViewSizeModeCommand;
        private RightExtensionCommand? _selectRightExtensionTabCommand;
        private RightExtensionCommand? _returnTagsToPlayingTrackCommand;
        private bool _disposed;

        private sealed class RightExtensionCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;

            public RightExtensionCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

            public void Execute(object? parameter) => _execute(parameter);

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 設定が更新された際に発生するイベント
        /// </summary>
        public event Action? SettingsUpdated;

        /// <summary>
        /// 再生コントロール専用ViewModel
        /// </summary>
        public Presentation.ViewModels.PlayerControlViewModel? PlayerControl { get; }

        /// <summary>
        /// ライブラリ専用ViewModel
        /// </summary>
        public Presentation.ViewModels.LibraryViewModel? Library { get; }

        /// <summary>
        /// アルバム情報詳細画面専用ViewModel
        /// </summary>
        public Presentation.ViewModels.AlbumDetailViewModel AlbumDetail { get; }

        /// <summary>
        /// すべての曲専用ViewModel
        /// </summary>
        public Presentation.ViewModels.AllSongsViewModel AllSongs { get; }

        /// <summary>
        /// アーティスト一覧専用ViewModel
        /// </summary>
        public Presentation.ViewModels.ArtistsViewModel Artists { get; }

        /// <summary>
        /// 最近再生した曲専用ViewModel
        /// </summary>
        public Presentation.ViewModels.RecentViewModel Recent { get; }

        /// <summary>
        /// プレイリスト専用ViewModel
        /// </summary>
        public Presentation.ViewModels.PlaylistViewModel? Playlist { get; }

        /// <summary>
        /// イコライザー専用ViewModel
        /// </summary>
        public Presentation.ViewModels.EqualizerViewModel? Equalizer { get; }

        /// <summary>
        /// デバイス同期専用ViewModel
        /// </summary>
        public Presentation.ViewModels.DeviceSyncViewModel? DeviceSync { get; }

        /// <summary>
        /// 再生中情報専用ViewModel
        /// </summary>
        public Presentation.ViewModels.NowPlayingViewModel? NowPlaying { get; }

        /// <summary>
        /// 歌詞表示専用ViewModel
        /// </summary>
        public Presentation.ViewModels.LyricsViewModel? Lyrics { get; }

        /// <summary>
        /// LRC 自動生成専用ViewModel
        /// </summary>
        public Presentation.ViewModels.LrcGeneratorViewModel? LrcGenerator { get; }

        /// <summary>
        /// リスニング統計ダッシュボード専用ViewModel
        /// </summary>
        public Presentation.ViewModels.ListeningStatsViewModel? ListeningStats { get; }

        /// <summary>
        /// フォルダーブラウズ専用ViewModel
        /// </summary>
        public FolderViewModel Folder { get; }

        /// <summary>
        /// デバイスブラウズ・転送専用ViewModel
        /// </summary>
        public DeviceBrowserViewModel DeviceBrowser { get; }

        /// <summary>
        /// コードビハインドからAudioServiceへアクセスするためのプロパティ
        /// </summary>
        public IAudioService AudioService => _audioService; // Public accessor for code-behind

        /// <summary>
        /// コードビハインドからSettingsServiceへアクセスするためのプロパティ
        /// </summary>
        public ISettingsService SettingsService => _settingsService;

        private Track? _currentTrack;
        private bool _isPlaying;
        private string _currentTimeDisplay = "00:00";
        private BitmapImage? _nowPlayingImage;
        private BitmapImage? _miniPlayerAlbumArtImage;
        private int _trackArtworkGeneration;
        private string _totalTimeDisplay = "00:00";
        private double _progress;
        private DispatcherTimer _timer;
        private bool _isNowPlayingVisible = true;
        private bool _isLoading;
        private bool _isGridView = true;
        private string _selectedSortOption = "Artist";
        private ViewType _currentViewType = ViewType.Albums;

        /// <summary>
        /// 現在表示されているメインコンテンツのビュー種別を取得または設定します
        /// </summary>
        public ViewType CurrentViewType
        {
            get => _currentViewType;
            set
            {
                if (_currentViewType != value)
                {
                    var previousViewType = _currentViewType;
                    _currentViewType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsLibraryVisible));
                    OnPropertyChanged(nameof(IsAlbumsSectionActive));
                    OnPropertyChanged(nameof(IsFolderViewVisible));
                    OnPropertyChanged(nameof(IsPlaylistSelectorVisible));
                    OnPropertyChanged(nameof(IsPlaylistTracksVisible));
                    OnPropertyChanged(nameof(IsPlaylistSectionActive));
                    OnPropertyChanged(nameof(IsDeviceSyncVisible));
                    OnPropertyChanged(nameof(IsFavoritesView));
                    OnPropertyChanged(nameof(IsMainLoadingOverlayVisible));

                    if (previousViewType == ViewType.AlbumDetail && _currentViewType != ViewType.AlbumDetail)
                    {
                        AlbumDetail?.SetAlbum(null);
                    }

                    if (_currentViewType == ViewType.DeviceSync)
                    {
                        if (PlayerControl != null)
                        {
                            PlayerControl.IsSpectrumVisible = false;
                        }
                        DeviceBrowser.RefreshDrives();
                    }
                }
            }
        }

        /// <summary>
        /// 表示ビューを切り替えるコマンドを取得します
        /// </summary>
        public ICommand SwitchViewCommand { get; }

        /// <summary>
        /// アルバム情報画面からアルバム一覧画面へ戻るコマンドを取得します
        /// </summary>
        public ICommand BackToAlbumsCommand { get; }

        private bool _isRightPanelOpen;
        /// <summary>
        /// 右側タブパネルが開いているかどうかを示すプロパティ
        /// Falseの場合は画面上部にコンパクトプレイヤーが表示されます
        /// </summary>
        public bool IsRightPanelOpen
        {
            get => _isRightPanelOpen;
            set
            {
                if (_isRightPanelOpen != value)
                {
                    _isRightPanelOpen = value;
                    OnPropertyChanged();
                    NotifyRightExtensionVisibilityChanged();
                }
            }
        }

        /// <summary>
        /// 右側タブパネルの開閉状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleRightPanelCommand { get; }

        private bool _isPlayQueuePanelOpen;
        /// <summary>
        /// 再生キュースライドパネルが開いているかどうかを示すプロパティ
        /// </summary>
        public bool IsPlayQueuePanelOpen
        {
            get => _isPlayQueuePanelOpen;
            set
            {
                _isPlayQueuePanelOpen = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 再生キュースライドパネルの開閉状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand TogglePlayQueuePanelCommand { get; }

        /// <summary>
        /// 再生キュースライドパネルを閉じるコマンドを取得します
        /// </summary>
        public ICommand ClosePlayQueuePanelCommand { get; }

        /// <summary>
        /// 再生キューからトラックを削除するコマンドを取得します
        /// </summary>
        public ICommand RemoveFromQueueCommand { get; }

        /// <summary>
        /// 再生キューを全クリアするコマンドを取得します
        /// </summary>
        public ICommand ClearQueueCommand { get; }

        /// <summary>
        /// 再生履歴を全クリアするコマンドを取得します
        /// </summary>
        public ICommand ClearHistoryCommand { get; }

        /// <summary>
        /// 再生履歴からトラックを削除するコマンドを取得します
        /// </summary>
        public ICommand RemoveFromHistoryCommand { get; }

        /// <summary>
        /// 履歴からトラックを即時再生するコマンドを取得します
        /// </summary>
        public ICommand PlayFromHistoryCommand { get; }

        /// <summary>
        /// 選択中のタブ（再生リストまたは履歴）に応じた全クリアを実行するコマンドを取得します
        /// </summary>
        public ICommand ClearCurrentTabCommand { get; }

        /// <summary>
        /// 再生キューのタブ（0: 再生リスト, 1: 履歴）を選択するコマンドを取得します
        /// </summary>
        public ICommand SelectQueueTabCommand { get; }


        private bool _isAlbumViewMaximized = true;
        /// <summary>
        /// 右側パネルのアルバム情報表示が最大化（全画面）モードかどうかを示すプロパティ
        /// Trueの場合は特大アートとフルハイトトラックリスト、Falseの場合は上部にコンパクトプレイヤーと下部に3タブ拡張領域を表示します
        /// </summary>
        public bool IsAlbumViewMaximized
        {
            get => _isAlbumViewMaximized;
            set
            {
                if (!value && !HasRightExtensionTarget)
                {
                    return;
                }

                if (_isAlbumViewMaximized != value)
                {
                    _isAlbumViewMaximized = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanToggleAlbumViewSizeMode));

                    if (!value && _activeRightExtensionPanel == RightExtensionPanelType.None)
                    {
                        _activeRightExtensionPanel = RightExtensionPanelType.Lyrics;
                        OnPropertyChanged(nameof(ActiveRightExtensionPanel));
                        NotifyRightExtensionTabPropertiesChanged();
                    }

                    NotifyRightExtensionVisibilityChanged();
                    RaiseRightExtensionCommandsCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 現在再生中の楽曲が存在するかどうかを取得します
        /// </summary>
        public bool HasCurrentTrack => CurrentTrack != null;

        /// <summary>
        /// 右側拡張領域を表示するための対象楽曲（再生中または右クリック選択曲）が存在するかどうかを取得します
        /// </summary>
        public bool HasRightExtensionTarget =>
            CurrentTrack != null
            || ListeningStats?.TargetTrack != null
            || LrcGenerator?.TargetTrack != null
            || _selectedTagsTargetTrack != null;

        /// <summary>
        /// 右上の拡大／縮小ボタンが実行可能かどうかを取得します（未再生時は最大化からコンパクト表示へ開けません）
        /// </summary>
        public bool CanToggleAlbumViewSizeMode => !IsAlbumViewMaximized || HasCurrentTrack;

        /// <summary>
        /// 右側アルバム表示のサイズモード（最大化 / コンパクト）を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleAlbumViewSizeModeCommand { get; }

        /// <summary>
        /// 右側タブ下部拡張領域に現在選択されている排他パネル種別を取得または設定します
        /// </summary>
        public RightExtensionPanelType ActiveRightExtensionPanel
        {
            get => _activeRightExtensionPanel;
            set
            {
                if (_activeRightExtensionPanel != value)
                {
                    _activeRightExtensionPanel = value;
                    OnPropertyChanged();
                    NotifyRightExtensionTabPropertiesChanged();
                    NotifyRightExtensionVisibilityChanged();
                }
            }
        }

        /// <summary>
        /// 右側タブ下部拡張領域全体（3タブバー含む）が表示中かどうかを取得します
        /// </summary>
        public bool IsRightExtensionAreaVisible => IsRightPanelOpen && !IsAlbumViewMaximized;

        /// <summary>
        /// 3タブバー上で「歌詞」タブが選択状態かどうかを取得します（LRC 自動生成パネル表示中もアクティブ扱い）
        /// </summary>
        public bool IsLyricsTabSelected =>
            _activeRightExtensionPanel is RightExtensionPanelType.Lyrics or RightExtensionPanelType.LrcGenerator;

        /// <summary>
        /// 3タブバー上で「ダッシュボード」タブが選択状態かどうかを取得します
        /// </summary>
        public bool IsDashboardTabSelected => _activeRightExtensionPanel == RightExtensionPanelType.Dashboard;

        /// <summary>
        /// 3タブバー上で「タグ」タブが選択状態かどうかを取得します
        /// </summary>
        public bool IsTagsTabSelected => _activeRightExtensionPanel == RightExtensionPanelType.Tags;

        /// <summary>
        /// 歌詞パネルが選択状態かどうかを取得します
        /// </summary>
        public bool IsLyricsPanelVisible => _activeRightExtensionPanel == RightExtensionPanelType.Lyrics;

        /// <summary>
        /// 歌詞パネルが画面上に実際に表示されているかどうかを取得します（非表示時は Collapsed 連動）
        /// </summary>
        public bool IsLyricsViewVisible => IsRightExtensionAreaVisible && IsLyricsPanelVisible;

        /// <summary>
        /// LRC 自動生成パネルが選択状態かどうかを取得します
        /// </summary>
        public bool IsLrcGeneratorPanelVisible => _activeRightExtensionPanel == RightExtensionPanelType.LrcGenerator;

        /// <summary>
        /// LRC 自動生成パネルが画面上に実際に表示されているかどうかを取得します（非表示時は Collapsed 連動）
        /// </summary>
        public bool IsLrcGeneratorViewVisible => IsRightExtensionAreaVisible && IsLrcGeneratorPanelVisible;

        /// <summary>
        /// ダッシュボードパネルが選択状態かどうかを取得します
        /// </summary>
        public bool IsDashboardPanelVisible => _activeRightExtensionPanel == RightExtensionPanelType.Dashboard;

        /// <summary>
        /// ダッシュボードパネルが画面上に実際に表示されているかどうかを取得します（非表示時は Collapsed 連動）
        /// </summary>
        public bool IsDashboardViewVisible => IsRightExtensionAreaVisible && IsDashboardPanelVisible;

        /// <summary>
        /// タグパネルが選択状態かどうかを取得します
        /// </summary>
        public bool IsTagsPanelVisible => _activeRightExtensionPanel == RightExtensionPanelType.Tags;

        /// <summary>
        /// タグパネルが画面上に実際に表示されているかどうかを取得します（非表示時は Collapsed 連動）
        /// </summary>
        public bool IsTagsViewVisible => IsRightExtensionAreaVisible && IsTagsPanelVisible;

        /// <summary>
        /// タグパネルの表示対象楽曲（右クリック指定曲、未指定時は現在再生中楽曲）を取得します
        /// </summary>
        public Track? TagsTargetTrack => _selectedTagsTargetTrack ?? CurrentTrack;

        /// <summary>
        /// タグパネルで再生中以外の楽曲を表示しているかどうかを取得します
        /// </summary>
        public bool IsTagsShowingNonPlayingTrack =>
            _selectedTagsTargetTrack != null && !IsSameTrack(_selectedTagsTargetTrack, CurrentTrack);

        /// <summary>
        /// タグパネルで「再生中の曲に戻す」操作が可能かどうかを取得します
        /// </summary>
        public bool CanReturnTagsToPlayingTrack => IsTagsShowingNonPlayingTrack && CurrentTrack != null;

        /// <summary>
        /// タグパネルの対象楽曲タイトル表示文字列を取得します
        /// </summary>
        public string TagsTargetTrackTitle =>
            string.IsNullOrWhiteSpace(TagsTargetTrack?.Title) ? "楽曲が選択されていません" : TagsTargetTrack.Title;

        /// <summary>
        /// タグパネルの対象楽曲アーティスト表示文字列を取得します
        /// </summary>
        public string TagsTargetTrackArtist =>
            string.IsNullOrWhiteSpace(TagsTargetTrack?.Artist) ? string.Empty : TagsTargetTrack.Artist;

        /// <summary>
        /// 右側タブ下部拡張領域のタブ（歌詞 / ダッシュボード / タグ）を切り替えるコマンドを取得します
        /// </summary>
        public ICommand SelectRightExtensionTabCommand { get; }

        /// <summary>
        /// 指定楽曲（未指定時は現在再生中楽曲）を対象にダッシュボードタブを開くコマンドを取得します
        /// </summary>
        public ICommand OpenTrackDashboardCommand { get; }

        /// <summary>
        /// 指定楽曲（未指定時は現在再生中楽曲）を対象にタグタブを開くコマンドを取得します
        /// </summary>
        public ICommand OpenTrackTagsCommand { get; }

        /// <summary>
        /// タグタブの表示対象を現在再生中の楽曲へ戻すコマンドを取得します
        /// </summary>
        public ICommand ReturnTagsToPlayingTrackCommand { get; }

        /// <summary>
        /// 指定楽曲（未指定時は現在再生中楽曲）を対象に LRC 自動生成パネルを開くコマンドを取得します
        /// </summary>
        public ICommand OpenLrcGeneratorCommand { get; }

        private bool _isLibraryVisible = true;
        private bool _isFolderViewVisible;

        private string _playbackListName = "No Album Selected";

        /// <summary>
        /// 再生リストの名称（アルバム名やプレイリスト名）。
        /// </summary>
        public string PlaybackListName
        {
            get => PlayerControl?.PlaybackListName ?? _playbackListName;
            set
            {
                _playbackListName = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlaybackListName = value;
                }
                OnPropertyChanged();
            }
        }

        private string _playbackListSubtitle = "";

        /// <summary>
        /// 再生リストのサブタイトル（アーティスト名など）。
        /// </summary>
        public string PlaybackListSubtitle
        {
            get => PlayerControl?.PlaybackListSubtitle ?? _playbackListSubtitle;
            set
            {
                _playbackListSubtitle = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlaybackListSubtitle = value;
                }
                OnPropertyChanged();
            }
        }

        private ObservableCollection<Track> _playbackListTracks = new ObservableCollection<Track>();

        /// <summary>
        /// 現在画面の右ペインに表示されているトラックのコレクション（アルバム・プレイリスト）。
        /// </summary>
        public ObservableCollection<Track> PlaybackListTracks
        {
            get => PlayerControl?.PlaybackListTracks ?? _playbackListTracks;
            set
            {
                _playbackListTracks = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlaybackListTracks = value;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlaybackListTracksCountText));
            }
        }

        /// <summary>
        /// トラックリストの総曲数表示文字列（例: "Tracklist (9 tracks)"）。
        /// </summary>
        public string PlaybackListTracksCountText => PlayerControl?.PlaybackListTracksCountText ?? ((PlaybackListTracks?.Count ?? 0) == 1 ? "Tracklist (1 track)" : $"Tracklist ({PlaybackListTracks?.Count ?? 0} tracks)");

        private ObservableCollection<Track> _playQueue = new ObservableCollection<Track>();

        /// <summary>
        /// 実際の再生予定キュー。
        /// </summary>
        public ObservableCollection<Track> PlayQueue
        {
            get => PlayerControl?.PlayQueue ?? _playQueue;
            set
            {
                _playQueue = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlayQueue = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ユーザーが手動で追加した予約キュー
        /// </summary>
        public ObservableCollection<Track> UserQueue => PlayerControl?.UserQueue ?? new ObservableCollection<Track>();

        /// <summary>
        /// アルバムから引き続いて再生される予定のキュー
        /// </summary>
        public ObservableCollection<Track> AlbumQueue => PlayerControl?.AlbumQueue ?? new ObservableCollection<Track>();

        /// <summary>
        /// 予約キューが存在するかどうか
        /// </summary>
        public bool HasUserQueue => PlayerControl?.HasUserQueue ?? false;

        /// <summary>
        /// アルバムキューが存在するかどうか
        /// </summary>
        public bool HasAlbumQueue => PlayerControl?.HasAlbumQueue ?? false;

        /// <summary>
        /// 予約キュー曲数表示テキスト
        /// </summary>
        public string UserQueueCountText => PlayerControl?.UserQueueCountText ?? "0 tracks";

        /// <summary>
        /// アルバムキュータイトル
        /// </summary>
        public string AlbumQueueTitle => PlayerControl?.AlbumQueueTitle ?? "アルバムの残り曲";

        /// <summary>
        /// アルバムキュー曲数表示テキスト
        /// </summary>
        public string AlbumQueueCountText => PlayerControl?.AlbumQueueCountText ?? "0 tracks";

        /// <summary>
        /// 予約キューのみを全クリアするコマンド
        /// </summary>
        public ICommand? ClearUserQueueCommand => PlayerControl?.ClearUserQueueCommand;

        /// <summary>
        /// 予約キュー内の指定インデックスにあるトラックを別の位置へ移動します
        /// </summary>
        /// <param name="fromIndex">移動元のインデックス（0始まり）</param>
        /// <param name="toIndex">移動先のインデックス（0始まり）</param>
        public void MoveUserQueueItem(int fromIndex, int toIndex)
        {
            PlayerControl?.MoveUserQueueItem(fromIndex, toIndex);
        }

        /// <summary>
        /// アルバム残り曲キュー内の指定インデックスにあるトラックを別の位置へ移動します
        /// </summary>
        /// <param name="fromIndex">移動元のインデックス（0始まり）</param>
        /// <param name="toIndex">移動先のインデックス（0始まり）</param>
        public void MoveAlbumQueueItem(int fromIndex, int toIndex)
        {
            PlayerControl?.MoveAlbumQueueItem(fromIndex, toIndex);
        }

        /// <summary>
        /// アルバム残り曲キューの指定トラックを予約キューの指定位置へ昇格移動します
        /// </summary>
        /// <param name="fromAlbumIndex">移動元のアルバムキューインデックス（0始まり）</param>
        /// <param name="toUserIndex">移動先の予約キュー挿入インデックス（0始まり）</param>
        public void MoveAlbumQueueItemToUserQueue(int fromAlbumIndex, int toUserIndex)
        {
            PlayerControl?.MoveAlbumQueueItemToUserQueue(fromAlbumIndex, toUserIndex);
        }

        /// <summary>
        /// 最後に再生コマンド（旧: キューに追加）
        /// </summary>
        public ICommand? PlayLastCommand => PlayerControl?.PlayLastCommand;

        private ObservableCollection<Track> _playHistory = new ObservableCollection<Track>();

        /// <summary>
        /// 再生終了した楽曲の履歴コレクション。
        /// </summary>
        public ObservableCollection<Track> PlayHistory
        {
            get => PlayerControl?.PlayHistory ?? _playHistory;
            set
            {
                _playHistory = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlayHistory = value;
                }
                OnPropertyChanged();
            }
        }

        private int _selectedQueueTabIndex;

        /// <summary>
        /// 再生キューパネルで選択されているタブのインデックス（0: 再生リスト, 1: 履歴）
        /// </summary>
        public int SelectedQueueTabIndex
        {
            get => PlayerControl?.SelectedQueueTabIndex ?? _selectedQueueTabIndex;
            set
            {
                _selectedQueueTabIndex = value;
                if (PlayerControl != null)
                {
                    PlayerControl.SelectedQueueTabIndex = value;
                }
                OnPropertyChanged();
            }
        }

        private BitmapImage? _defaultSpectrumImage;
        private BitmapImage? _favoritesImage;
        private BitmapImage? _defaultNowPlayingImage;

        private Track? _lastPlayedTrack;
        private Album? _lastPlayedAlbum;

        private ImageSource? _spectrumBackgroundImage;

        /// <summary>
        /// スペクトラムアナライザーの背景画像。
        /// </summary>
        public ImageSource? SpectrumBackgroundImage
        {
            get => _spectrumBackgroundImage;
            set
            {
                _spectrumBackgroundImage = value;
                OnPropertyChanged();
            }
        }

        // ... (existing code) ...

        /// <summary>
        /// ViewModelの初期化を行い、各サービスのインスタンス生成、データの読み込み、コマンドの初期化を行います。
        /// </summary>
        public MainViewModel()
            : this(dataDirectoryProvider: null, dataDirectoryMigrationService: null, logConfigurationManager: null)
        {
        }

        /// <summary>
        /// DI コンテナからデータディレクトリ・ログ設定関連サービスを受け取り、ViewModel を初期化します。
        /// </summary>
        /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ（任意）</param>
        /// <param name="dataDirectoryMigrationService">データディレクトリ移行サービス（任意）</param>
        /// <param name="logConfigurationManager">ログ出力構成マネージャ（任意）</param>
        [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
        public MainViewModel(
            IDataDirectoryProvider? dataDirectoryProvider,
            IDataDirectoryMigrationService? dataDirectoryMigrationService,
            ILogConfigurationManager? logConfigurationManager = null)
        {
            _dataDirectoryProvider = dataDirectoryProvider
                ?? (App.ServiceProvider != null
                    ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IDataDirectoryProvider>(App.ServiceProvider)
                    : null);
            _dataDirectoryMigrationService = dataDirectoryMigrationService
                ?? (App.ServiceProvider != null
                    ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IDataDirectoryMigrationService>(App.ServiceProvider)
                    : null);
            _logConfigurationManager = logConfigurationManager
                ?? (App.ServiceProvider != null
                    ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ILogConfigurationManager>(App.ServiceProvider)
                    : null);

            if (App.ServiceProvider != null)
            {
                PlayerControl = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.PlayerControlViewModel>(App.ServiceProvider);
                Library = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.LibraryViewModel>(App.ServiceProvider);
                Playlist = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.PlaylistViewModel>(App.ServiceProvider);
                Equalizer = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.EqualizerViewModel>(App.ServiceProvider);
                DeviceSync = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.DeviceSyncViewModel>(App.ServiceProvider);
                NowPlaying = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.NowPlayingViewModel>(App.ServiceProvider);
            }

            if (App.ServiceProvider != null)
            {
                var resolvedSettings = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ISettingsService>(App.ServiceProvider);
                if (resolvedSettings != null)
                {
                    _settingsService = resolvedSettings;
                }
                else
                {
                    _fallbackSettingsRepository = new Infrastructure.Repository.JsonSettingsRepository();
                    _fallbackSettings = new SettingsApplicationService(_fallbackSettingsRepository, dataDirectoryProvider: null, fileLockHelper: AudioEffector.Infrastructure.Repository.JsonFileLockHelper.Default);
                    _settingsService = LoggingProxy<ISettingsService>.Create(_fallbackSettings);
                }
            }
            else
            {
                _fallbackSettingsRepository = new Infrastructure.Repository.JsonSettingsRepository();
                _fallbackSettings = new SettingsApplicationService(_fallbackSettingsRepository, dataDirectoryProvider: null, fileLockHelper: AudioEffector.Infrastructure.Repository.JsonFileLockHelper.Default);
                _settingsService = LoggingProxy<ISettingsService>.Create(_fallbackSettings);
            }

            if (App.ServiceProvider != null)
            {
                var resolved = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IAudioService>(App.ServiceProvider);
                if (resolved != null)
                {
                    _audioService = resolved;
                }
                else
                {
                    _fallbackAudioService = new AudioService(new Presentation.Services.WpfUiDispatcher(), _settingsService);
                    _audioService = LoggingProxy<IAudioService>.Create(_fallbackAudioService);
                }
            }
            else
            {
                _fallbackAudioService = new AudioService(new Presentation.Services.WpfUiDispatcher(), _settingsService);
                _audioService = LoggingProxy<IAudioService>.Create(_fallbackAudioService);
            }
            _libraryService = App.ServiceProvider != null ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<LibraryApplicationService>(App.ServiceProvider) : null;
            if (Playlist != null)
            {
                Playlist.ViewRequested += viewType =>
                {
                    CurrentViewType = viewType;
                    if (viewType == ViewType.PlaylistTracks)
                    {
                        Playlist.SetBackgroundImage(NowPlayingImage ?? _defaultNowPlayingImage);
                    }
                };
                Playlist.PlaybackRequested += (tracks, name, subtitle) =>
                {
                    PlaybackListName = name;
                    PlaybackListSubtitle = subtitle;
                    PlaybackListTracks = new ObservableCollection<Track>(tracks);
                    PlayerControl?.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
                };
                Playlist.FavoriteRemovalRequested += track =>
                {
                    if (track.IsFavorite)
                    {
                        ToggleFavorite(track);
                    }
                };
                Playlist.LoadPlaylists();
            }

            if (PlayerControl == null)
            {
#pragma warning disable CA2000 // オブジェクトの破棄
                PlayerControl = new Presentation.ViewModels.PlayerControlViewModel(
                    _audioService,
                    new Infrastructure.Audio.NAudioPlaybackEngine(),
                    new Application.Common.InMemoryEventBus(),
                    _settingsService);
#pragma warning restore CA2000 // オブジェクトの破棄
            }

            if (PlayerControl != null)
            {
                PlayerControl.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null)
                    {
                        OnPropertyChanged(e.PropertyName);
                    }
                };
                PlayerControl.TrackPlayHandler = track =>
                {
                    if (IsPlaylistTracksVisible && Playlist is { PlaylistTracks.Count: > 0 } playlistViewModel && playlistViewModel.PlaylistTracks.Contains(track))
                    {
                        PlayerControl.SetPlaybackList(playlistViewModel.PlaylistTracks, playlistViewModel.CurrentPlaylistName, playlistViewModel.IsFavoritesView ? "Selected You" : "Playlist", startTrack: track);
                        return false;
                    }
                    var album = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                    if (album != null)
                    {
                        PlayerControl.SetPlaybackList(album.Tracks, album.Title, album.Artist, startTrack: track);
                        return false;
                    }
                    return false;
                };
                PlayerControl.FavoriteToggleRequested = track => ToggleFavorite(track);
                PlayerControl.TrackChanged += track =>
                {
                    if (track != null)
                    {
                        CurrentAlbum = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                    }
                    else
                    {
                        CurrentAlbum = null;
                    }
                };
            }

            if (Library == null)
            {
#pragma warning disable CA2000 // オブジェクトの破棄
                Library = new Presentation.ViewModels.LibraryViewModel(
                    _libraryService ?? new LibraryApplicationService(
                        new Infrastructure.Repository.JsonTrackRepository(),
                        new Infrastructure.Repository.JsonFavoriteRepository(),
                        new Infrastructure.Library.TagLibMetadataExtractor(),
                        new InMemoryEventBus()),
                    _audioService,
                    _settingsService,
                    Playlist);
#pragma warning restore CA2000 // オブジェクトの破棄
            }

            if (Library != null)
            {
                Library.PlaybackRequested += (tracks, startTrack, name, subtitle) =>
                {
                    PlaybackListName = name;
                    PlaybackListSubtitle = subtitle;
                    PlaybackListTracks = new ObservableCollection<Track>(tracks);
                    PlayerControl?.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
                };
                Library.EnqueueRequested += (tracks, playNext) =>
                {
                    _audioService.EnqueueTracks(tracks, playNext);
                };
                Library.FavoriteToggled += track =>
                {
                    if (track == CurrentTrack)
                    {
                        OnPropertyChanged(nameof(CurrentTrack));
                    }
                    if (IsFavoritesView)
                    {
                        if (track.IsFavorite)
                        {
                            Playlist?.AddFavoriteTrack(track);
                        }
                        else
                        {
                            Playlist?.RemoveDisplayedTrack(track);
                        }
                    }
                };
                Library.TrackRemoved += track =>
                {
                    Playlist?.RemoveDisplayedTrack(track);
                    PlaybackListTracks.Remove(track);
                };
                Library.AlbumRemoved += album =>
                {
                    RemoveTracksNotInLibrary(album, Library?.Tracks, Playlist, PlaybackListTracks);
                };
                Library.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Library.IsLoading))
                    {
                        OnPropertyChanged(nameof(IsLoading));
                        OnPropertyChanged(nameof(IsMainLoadingOverlayVisible));
                    }
                };
                Library.LibrarySynced += _ =>
                {
                    if (IsFavoritesView)
                    {
                        ShowFavorites();
                    }
                };
                Library.AlbumDetailRequested += OnLibraryAlbumDetailRequested;
            }

            var resolvedAlbumDetail = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.AlbumDetailViewModel>(App.ServiceProvider)
                : null;
            if (resolvedAlbumDetail != null)
            {
                AlbumDetail = resolvedAlbumDetail;
                _ownsAlbumDetail = false;
            }
            else
            {
                AlbumDetail = new Presentation.ViewModels.AlbumDetailViewModel(Library!, _audioService, Playlist);
                _ownsAlbumDetail = true;
            }

            AlbumDetail.PlaybackRequested += OnAlbumDetailPlaybackRequested;
            AlbumDetail.EnqueueRequested += OnAlbumDetailEnqueueRequested;
            AlbumDetail.BackRequested += OnAlbumDetailBackRequested;

            var resolvedAllSongs = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.AllSongsViewModel>(App.ServiceProvider)
                : null;
            if (resolvedAllSongs != null)
            {
                AllSongs = resolvedAllSongs;
                _ownsAllSongs = false;
            }
            else
            {
                AllSongs = new Presentation.ViewModels.AllSongsViewModel(Library!, _audioService);
                _ownsAllSongs = true;
            }

            AllSongs.PlaybackRequested += OnAllSongsPlaybackRequested;
            AllSongs.EnqueueRequested += OnAllSongsEnqueueRequested;
            AllSongs.GenerateLrcRequested += OpenLrcGenerator;

            var resolvedArtists = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.ArtistsViewModel>(App.ServiceProvider)
                : null;
            if (resolvedArtists != null)
            {
                Artists = resolvedArtists;
                _ownsArtists = false;
            }
            else
            {
                Artists = new Presentation.ViewModels.ArtistsViewModel(Library!, _audioService);
                _ownsArtists = true;
            }

            Artists.PlaybackRequested += OnArtistsPlaybackRequested;
            Artists.EnqueueRequested += OnArtistsEnqueueRequested;

            var resolvedRecent = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.RecentViewModel>(App.ServiceProvider)
                : null;
            if (resolvedRecent != null)
            {
                Recent = resolvedRecent;
                _ownsRecent = false;
            }
            else
            {
                var statsRepo = App.ServiceProvider?.GetService(typeof(AudioEffector.Domain.Repositories.IListeningStatsRepository)) as AudioEffector.Domain.Repositories.IListeningStatsRepository;
                if (statsRepo != null)
                {
                    Recent = new Presentation.ViewModels.RecentViewModel(statsRepo, Library!, _audioService);
                }
                else
                {
                    var dataDirectoryProvider = App.ServiceProvider?.GetService(typeof(IDataDirectoryProvider)) as IDataDirectoryProvider;
                    var recentRepo = App.ServiceProvider?.GetService(typeof(AudioEffector.Domain.Repositories.IRecentTrackRepository)) as AudioEffector.Domain.Repositories.IRecentTrackRepository
                        ?? (dataDirectoryProvider != null
                            ? new Infrastructure.Repository.JsonListeningStatsRepository(dataDirectoryProvider)
                            : new Infrastructure.Repository.JsonListeningStatsRepository());
                    Recent = new Presentation.ViewModels.RecentViewModel(recentRepo, Library!, _audioService);
                }
                _ownsRecent = true;
            }

            Recent.PlaybackRequested += OnRecentPlaybackRequested;
            Recent.EnqueueRequested += OnRecentEnqueueRequested;

            var resolvedLyrics = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.LyricsViewModel>(App.ServiceProvider)
                : null;
            LyricsApplicationService? sharedLyricsAppService = App.ServiceProvider?.GetService(typeof(LyricsApplicationService)) as LyricsApplicationService;
            IUiDispatcher sharedUiDispatcher = App.ServiceProvider?.GetService(typeof(IUiDispatcher)) as IUiDispatcher
                ?? new Presentation.Services.WpfUiDispatcher();
            if (resolvedLyrics != null)
            {
                Lyrics = resolvedLyrics;
                _ownsLyrics = false;
            }
            else
            {
                var lyricsEventBus = App.ServiceProvider?.GetService(typeof(IEventBus)) as IEventBus
                    ?? new InMemoryEventBus();
                sharedLyricsAppService ??= new LyricsApplicationService(
                    new Infrastructure.Lyrics.LocalLyricsProvider(new Infrastructure.Lyrics.LrcParser()),
                    lyricsEventBus);
                Lyrics = new Presentation.ViewModels.LyricsViewModel(
                    _audioService,
                    lyricsEventBus,
                    sharedLyricsAppService,
                    sharedUiDispatcher);
                _ownsLyrics = true;
            }

            Lyrics.CloseRequested += OnLyricsCloseRequested;
            Lyrics.GenerateLrcRequested += OpenLrcGenerator;

            var resolvedLrcGenerator = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.LrcGeneratorViewModel>(App.ServiceProvider)
                : null;
            if (resolvedLrcGenerator != null)
            {
                LrcGenerator = resolvedLrcGenerator;
                _ownsLrcGenerator = false;
            }
            else
            {
                var lrcParser = new Infrastructure.Lyrics.LrcParser();
                var modelManager = new Infrastructure.LrcGeneration.WhisperModelManager();
                var aligner = new Domain.Services.LyricsForcedAligner();
                var alignmentEngine = new Infrastructure.LrcGeneration.WhisperLocalAlignmentEngine(modelManager, lrcParser, aligner);
                var textEncodingService = new Infrastructure.LrcGeneration.TextEncodingService();
                var fallbackLyricsSvc = sharedLyricsAppService ?? new LyricsApplicationService(
                    new Infrastructure.Lyrics.LocalLyricsProvider(lrcParser),
                    new InMemoryEventBus());
                var lrcGenAppService = new LrcGenerationApplicationService(
                    alignmentEngine,
                    textEncodingService,
                    modelManager,
                    lrcParser,
                    fallbackLyricsSvc);
                LrcGenerator = new Presentation.ViewModels.LrcGeneratorViewModel(
                    lrcGenAppService,
                    _audioService,
                    sharedUiDispatcher,
                    _settingsService);
                _ownsLrcGenerator = true;
            }

            LrcGenerator.BackToLyricsRequested += OnLrcGeneratorBackToLyricsRequested;
            LrcGenerator.CloseRequested += OnLrcGeneratorCloseRequested;

            var resolvedListeningStats = App.ServiceProvider != null
                ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.ListeningStatsViewModel>(App.ServiceProvider)
                : null;
            if (resolvedListeningStats != null)
            {
                ListeningStats = resolvedListeningStats;
                _ownsListeningStats = false;
            }
            else
            {
                var resolvedStatsService = App.ServiceProvider?.GetService(typeof(ListeningStatsApplicationService)) as ListeningStatsApplicationService;
                if (resolvedStatsService == null)
                {
                    var dataDirectoryProvider = App.ServiceProvider?.GetService(typeof(IDataDirectoryProvider)) as IDataDirectoryProvider;
                    var statsRepo = App.ServiceProvider?.GetService(typeof(AudioEffector.Domain.Repositories.IListeningStatsRepository)) as AudioEffector.Domain.Repositories.IListeningStatsRepository
                        ?? (dataDirectoryProvider != null
                            ? new Infrastructure.Repository.JsonListeningStatsRepository(dataDirectoryProvider)
                            : new Infrastructure.Repository.JsonListeningStatsRepository());
                    _fallbackListeningStatsService = new ListeningStatsApplicationService(statsRepo, _audioService);
                    resolvedStatsService = _fallbackListeningStatsService;
                }

                ListeningStats = new Presentation.ViewModels.ListeningStatsViewModel(
                    resolvedStatsService,
                    _audioService,
                    sharedUiDispatcher);
                _ownsListeningStats = true;
            }

            var appSettings = _settingsService.LoadSettings();
            _audioService.UpdateAudioProperties(appSettings.SampleRate, appSettings.AudioBufferSizeMs);

            _audioService.TrackChanged += OnTrackChanged;
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
            _audioService.PlaylistChanged += OnPlaylistChanged;
            _audioService.VolumeChanged += OnVolumeChanged;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
            _timer.Start();

            LoadDefaultImages();
            NowPlayingImage = _defaultNowPlayingImage;
            SpectrumBackgroundImage = _defaultSpectrumImage;

            Folder = Library != null
                ? new FolderViewModel(_settingsService, Library)
                : new FolderViewModel(_settingsService, path => LoadLibrary(path));
            // 再生制御系コマンド（PlayerControlViewModelへ完全委譲）
            TogglePlayPauseCommand = PlayerControl!.TogglePlayPauseCommand;
            StopCommand = PlayerControl!.StopCommand;
            NextCommand = PlayerControl!.NextCommand;
            PreviousCommand = PlayerControl!.PreviousCommand;
            PlayTrackCommand = PlayerControl!.PlayTrackCommand;
            PlayNextCommand = PlayerControl!.PlayNextCommand;
            EnqueueTrackCommand = PlayerControl!.EnqueueTrackCommand;
            PlayFromQueueCommand = PlayerControl!.PlayFromQueueCommand;
            ToggleMiniPlayerQueueCommand = PlayerControl!.ToggleMiniPlayerQueueCommand;
            CloseMiniPlayerQueueCommand = PlayerControl!.CloseMiniPlayerQueueCommand;
            RemoveFromQueueCommand = PlayerControl!.RemoveFromQueueCommand;
            ClearQueueCommand = PlayerControl!.ClearQueueCommand;
            ClearHistoryCommand = PlayerControl!.ClearHistoryCommand;
            RemoveFromHistoryCommand = PlayerControl!.RemoveFromHistoryCommand;
            PlayFromHistoryCommand = PlayerControl!.PlayFromHistoryCommand;
            ClearCurrentTabCommand = PlayerControl!.ClearCurrentTabCommand;
            SelectQueueTabCommand = PlayerControl!.SelectQueueTabCommand;
            ToggleShuffleCommand = PlayerControl!.ToggleShuffleCommand;
            ToggleRepeatCommand = PlayerControl!.ToggleRepeatCommand;
            IncreaseVolumeCommand = PlayerControl!.IncreaseVolumeCommand;
            DecreaseVolumeCommand = PlayerControl!.DecreaseVolumeCommand;
            ToggleMuteCommand = PlayerControl!.ToggleMuteCommand;

            // ライブラリ・アルバム系コマンド（LibraryViewModelへ完全委譲）
            PlayAlbumCommand = Library!.PlayAlbumCommand;
            PlayNextAlbumCommand = Library!.PlayNextAlbumCommand;
            EnqueueAlbumCommand = Library!.EnqueueAlbumCommand;
            DeleteAlbumCommand = Library!.DeleteAlbumCommand;
            DeleteTrackCommand = Library!.DeleteTrackCommand;
            ShowTrackPropertiesCommand = Library!.ShowTrackPropertiesCommand;
            OpenFileLocationCommand = Library!.OpenFileLocationCommand;
            ToggleViewCommand = Library!.ToggleViewCommand;
            ToggleSortDirectionCommand = Library!.ToggleSortDirectionCommand;
            ToggleFavoriteCommand = Library!.ToggleFavoriteCommand;

            // ナビゲーション・表示切り替えコマンド
            ShowFavoritesCommand = new RelayCommand(o => ShowFavorites());
            SwitchViewCommand = new RelayCommand(param =>
            {
                if (param is ViewType viewType)
                {
                    CurrentViewType = viewType;
                    if (viewType == ViewType.Favorites)
                    {
                        ShowFavorites();
                    }
                }
            });
            ShowLibraryCommand = new RelayCommand(o => ShowLibrary());
            BackToAlbumsCommand = new RelayCommand(_ => CurrentViewType = ViewType.Albums);
            ShowFolderCommand = new RelayCommand(o => ShowFolder());
            ToggleSelectionModeCommand = new RelayCommand(o => IsSelectionMode = !IsSelectionMode);

            // Device Sync Command Initialization
            SwitchToDeviceSyncCommand = new RelayCommand(o => CurrentViewType = ViewType.DeviceSync);

            var transcodeService = App.ServiceProvider?.GetService(typeof(IAudioTranscodeService)) as IAudioTranscodeService
                ?? new MediaFoundationAudioTranscodeService();
            var deviceLrcParser = App.ServiceProvider?.GetService(typeof(Domain.Services.ILrcParser)) as Domain.Services.ILrcParser
                ?? new Infrastructure.Lyrics.LrcParser();
            DeviceBrowser = new DeviceBrowserViewModel(
                Albums,
                () => Albums.ToList().AsReadOnly(),
                () => { },
                settingsService: _settingsService,
                audioTranscodeService: transcodeService,
                lrcParser: deviceLrcParser);
            ShowSettingsCommand = new RelayCommand(o => ShowSettings());
            ToggleRightPanelCommand = new RelayCommand(o => IsRightPanelOpen = !IsRightPanelOpen);
            TogglePlayQueuePanelCommand = new RelayCommand(o => IsPlayQueuePanelOpen = !IsPlayQueuePanelOpen);
            ClosePlayQueuePanelCommand = new RelayCommand(o => IsPlayQueuePanelOpen = false);
            _toggleAlbumViewSizeModeCommand = new RightExtensionCommand(
                _ => ToggleAlbumViewSizeMode(),
                _ => CanToggleAlbumViewSizeMode);
            ToggleAlbumViewSizeModeCommand = _toggleAlbumViewSizeModeCommand;
            _selectRightExtensionTabCommand = new RightExtensionCommand(
                param =>
                {
                    if (param is RightExtensionPanelType tab)
                    {
                        SelectRightExtensionTab(tab);
                    }
                },
                _ => HasRightExtensionTarget);
            SelectRightExtensionTabCommand = _selectRightExtensionTabCommand;
            OpenTrackDashboardCommand = new RelayCommand(param => OpenTrackDashboard(param as Track ?? CurrentTrack));
            OpenTrackTagsCommand = new RelayCommand(param => OpenTrackTags(param as Track ?? CurrentTrack));
            _returnTagsToPlayingTrackCommand = new RightExtensionCommand(
                _ => ReturnTagsToPlayingTrack(),
                _ => CanReturnTagsToPlayingTrack);
            ReturnTagsToPlayingTrackCommand = _returnTagsToPlayingTrackCommand;
            OpenLrcGeneratorCommand = new RelayCommand(param => OpenLrcGenerator(param as Track ?? CurrentTrack));
            UpdateRightExtensionActiveViewStates();

            _audioService.PlaylistEnded += OnPlaylistEnded;

            var settings = _settingsService.LoadSettings();
            if (settings.LeftColumnWidth > 0)
            {
                LeftColumnWidth = new GridLength(settings.LeftColumnWidth);
            }
            // Load Volume
            Volume = settings.Volume;

            LoadLibrary();
        }

        private void OnVolumeChanged(float newVolume)
        {
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumePercent));
        }

        private void LoadDefaultImages()
        {
            try
            {
                // Load Spectrum Default
                var uriSpectrum = new Uri("pack://application:,,,/Assets/Images/default_spectrum_bg.png");
                var bitmapSpectrum = new BitmapImage();
                bitmapSpectrum.BeginInit();
                bitmapSpectrum.UriSource = uriSpectrum;
                bitmapSpectrum.CacheOption = BitmapCacheOption.OnLoad;
                bitmapSpectrum.EndInit();
                bitmapSpectrum.Freeze();
                _defaultSpectrumImage = bitmapSpectrum;

                // Load Now Playing Default
                var uriNowPlaying = new Uri("pack://application:,,,/Assets/Images/default_now_playing_bg.png");
                var bitmapNowPlaying = new BitmapImage();
                bitmapNowPlaying.BeginInit();
                bitmapNowPlaying.UriSource = uriNowPlaying;
                bitmapNowPlaying.CacheOption = BitmapCacheOption.OnLoad;
                bitmapNowPlaying.EndInit();
                bitmapNowPlaying.Freeze();
                _defaultNowPlayingImage = bitmapNowPlaying;

                // Load Favorites Image (Phase 8)
                var uriFav = new Uri("pack://application:,,,/Assets/Images/favorites_bg.png");
                var bitmapFav = new BitmapImage();
                bitmapFav.BeginInit();
                bitmapFav.UriSource = uriFav;
                bitmapFav.CacheOption = BitmapCacheOption.OnLoad;
                bitmapFav.EndInit();
                bitmapFav.Freeze();
                _favoritesImage = bitmapFav;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load default images: {ex.Message}");
            }
        }

        // ...


        /// <summary>
        /// デバイスの種類（ファイルシステム または MTP）
        /// </summary>
        public enum DeviceType
        {
            /// <summary>
            /// USBマスストレージなどのファイルシステムデバイス
            /// </summary>
            FileSystem,

            /// <summary>
            /// ポータブルデバイスなどのMTPデバイス
            /// </summary>
            MTP
        }

        /// <summary>
        /// 接続されたデバイスを表すViewModel
        /// </summary>
        public class DeviceViewModel : ViewModelBase
        {
            private long _totalSpaceBytes;
            private long _freeSpaceBytes;
            private bool _hasStorageInfo;

            /// <summary>
            /// デバイス名を取得または設定します
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// 既定転送先フォルダ記憶用の機器識別子を取得または設定します
            /// </summary>
            public string DeviceKey { get; set; } = string.Empty;

            /// <summary>
            /// デバイス種別を取得または設定します
            /// </summary>
            public DeviceType Type { get; set; }

            /// <summary>
            /// 接続種別の表示ラベル（"MTP" または "USB"）を取得します
            /// </summary>
            public string ConnectionTypeLabel => Type == DeviceType.MTP ? "MTP" : "USB";

            /// <summary>
            /// ファイルシステムデバイスの場合のドライブ情報を取得または設定します
            /// </summary>
            public DriveInfo? Drive { get; set; }

            /// <summary>
            /// MTPデバイスの場合のデバイスインスタンスを取得または設定します
            /// </summary>
            public MediaDevice? MtpDevice { get; set; }

            /// <summary>
            /// デバイスのルートパスを取得または設定します
            /// </summary>
            public string RootPath { get; set; } = string.Empty; // For MTP, this might be device ID or root

            /// <summary>
            /// ストレージ容量情報が取得できているかどうかを取得します
            /// </summary>
            public bool HasStorageInfo
            {
                get => _hasStorageInfo;
                private set
                {
                    if (SetProperty(ref _hasStorageInfo, value))
                    {
                        OnPropertyChanged(nameof(StorageSummaryText));
                    }
                }
            }

            /// <summary>
            /// デバイスの総容量（バイト）を取得または設定します
            /// </summary>
            public long TotalSpaceBytes
            {
                get => _totalSpaceBytes;
                set
                {
                    long normalized = Math.Max(0L, value);
                    if (SetProperty(ref _totalSpaceBytes, normalized))
                    {
                        HasStorageInfo = normalized > 0;
                        OnPropertyChanged(nameof(UsedSpaceBytes));
                        OnPropertyChanged(nameof(UsedSpaceRatio));
                        OnPropertyChanged(nameof(StorageSummaryText));
                    }
                }
            }

            /// <summary>
            /// デバイスの空き容量（バイト）を取得または設定します
            /// </summary>
            public long FreeSpaceBytes
            {
                get => _freeSpaceBytes;
                set
                {
                    long normalized = Math.Max(0L, value);
                    if (SetProperty(ref _freeSpaceBytes, normalized))
                    {
                        OnPropertyChanged(nameof(UsedSpaceBytes));
                        OnPropertyChanged(nameof(UsedSpaceRatio));
                        OnPropertyChanged(nameof(StorageSummaryText));
                    }
                }
            }

            /// <summary>
            /// デバイスの使用済み容量（バイト）を取得します
            /// </summary>
            public long UsedSpaceBytes => _totalSpaceBytes > _freeSpaceBytes ? _totalSpaceBytes - _freeSpaceBytes : 0L;

            /// <summary>
            /// デバイスの使用率（0.0〜1.0）を取得します
            /// </summary>
            public double UsedSpaceRatio => _totalSpaceBytes > 0
                ? Math.Clamp((double)UsedSpaceBytes / _totalSpaceBytes, 0.0, 1.0)
                : 0.0;

            /// <summary>
            /// 空き容量／総容量の表示用サマリテキストを取得します
            /// </summary>
            public string StorageSummaryText => HasStorageInfo
                ? $"{FormatByteSize(_freeSpaceBytes)} 空き / {FormatByteSize(_totalSpaceBytes)}"
                : "容量情報取得不可";

            /// <summary>
            /// デバイスの総容量および空き容量を一括更新します
            /// </summary>
            /// <param name="totalBytes">総容量（バイト）</param>
            /// <param name="freeBytes">空き容量（バイト）</param>
            public void UpdateStorageSpace(long totalBytes, long freeBytes)
            {
                long normalizedTotal = Math.Max(0L, totalBytes);
                long normalizedFree = Math.Clamp(freeBytes, 0L, normalizedTotal);
                _totalSpaceBytes = normalizedTotal;
                _freeSpaceBytes = normalizedFree;
                _hasStorageInfo = normalizedTotal > 0;

                OnPropertyChanged(nameof(TotalSpaceBytes));
                OnPropertyChanged(nameof(FreeSpaceBytes));
                OnPropertyChanged(nameof(UsedSpaceBytes));
                OnPropertyChanged(nameof(UsedSpaceRatio));
                OnPropertyChanged(nameof(HasStorageInfo));
                OnPropertyChanged(nameof(StorageSummaryText));
            }

            /// <summary>
            /// ストレージ容量情報を未取得状態へリセットします
            /// </summary>
            public void ClearStorageSpace()
            {
                UpdateStorageSpace(0L, 0L);
            }

            /// <summary>
            /// バイト数を人間が読みやすい単位（B / KB / MB / GB / TB）へフォーマットします
            /// </summary>
            /// <param name="bytes">フォーマット対象のバイト数</param>
            /// <returns>フォーマット済み文字列</returns>
            internal static string FormatByteSize(long bytes)
            {
                if (bytes <= 0)
                {
                    return "0 B";
                }

                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double size = bytes;
                int unitIndex = 0;
                while (size >= 1024.0 && unitIndex < units.Length - 1)
                {
                    size /= 1024.0;
                    unitIndex++;
                }

                return unitIndex == 0
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} {1}", size, units[unitIndex])
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} {1}", size, units[unitIndex]);
            }
        }

        /// <summary>
        /// デバイス同期画面の表示状態。
        /// </summary>
        public bool IsDeviceSyncVisible => CurrentViewType == ViewType.DeviceSync;


        /// <summary>
        /// 現在再生中の曲のアルバムアート画像を取得または設定します
        /// </summary>
        public BitmapImage? NowPlayingImage
        {
            get => _nowPlayingImage;
            set
            {
                _nowPlayingImage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ミニプレイヤー表示用の現在再生中トラックの実アルバムアート画像を取得または設定します（未再生・未設定時は null）
        /// </summary>
        public BitmapImage? MiniPlayerAlbumArtImage
        {
            get => _miniPlayerAlbumArtImage;
            set
            {
                if (!ReferenceEquals(_miniPlayerAlbumArtImage, value))
                {
                    _miniPlayerAlbumArtImage = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// テストや差し替え用の非同期埋め込みアートワークバイト列ローダー（未設定時は TagLib によるファイル読み込みを使用）
        /// </summary>
        internal Func<string, Task<byte[]?>>? TrackArtworkBytesLoader { get; set; }

        private ObservableCollection<Album> _albums = new ObservableCollection<Album>();

        /// <summary>
        /// ライブラリ内のアルバムコレクションを取得または設定します
        /// </summary>
        public ObservableCollection<Album> Albums
        {
            get => Library?.Albums ?? _albums;
            set
            {
                if (Library != null)
                {
                    Library.Albums.Clear();
                    foreach (var a in value) Library.Albums.Add(a);
                }
                _albums = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ライブラリビューが表示されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsLibraryVisible
        {
            get => _isLibraryVisible;
            set
            {
                _isLibraryVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// フォルダービューが表示されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsFolderViewVisible
        {
            get => _isFolderViewVisible;
            set
            {
                _isFolderViewVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// プレイリスト選択画面が表示されているかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistSelectorVisible => CurrentViewType == ViewType.Playlists;

        /// <summary>
        /// プレイリスト楽曲一覧が表示されているかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistTracksVisible => CurrentViewType == ViewType.PlaylistTracks || CurrentViewType == ViewType.Favorites;

        /// <summary>
        /// データ読み込み中かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading || (Library?.IsLoading ?? false);
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMainLoadingOverlayVisible));
            }
        }

        /// <summary>
        /// メイン画面の全画面ローディングオーバーレイを表示するかどうかを取得します
        /// フォルダタブ表示中はフォルダタブ内のプログレスインジケーターを使用するため非表示とします
        /// </summary>
        public bool IsMainLoadingOverlayVisible => IsLoading && CurrentViewType != ViewType.Folders;

        /// <summary>
        /// グリッド表示モードかどうか。
        /// </summary>
        public bool IsGridView
        {
            get => _isGridView;
            set
            {
                _isGridView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsListView));
            }
        }

        /// <summary>
        /// リスト表示モードかどうか。
        /// </summary>
        public bool IsListView
        {
            get => !_isGridView;
            set
            {
                IsGridView = !value;
                OnPropertyChanged(nameof(IsGridView));
            }
        }

        /// <summary>
        /// ライブラリのソート順選択肢一覧を取得します
        /// </summary>
        public List<string> SortOptions { get; } = new List<string> { "Artist", "Album" };

        /// <summary>
        /// 選択されているライブラリのソート順を取得または設定します
        /// </summary>
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                _selectedSortOption = value;
                OnPropertyChanged();
                SortLibrary();
            }
        }

        /// <summary>
        /// シャッフル再生が有効かどうかを取得または設定します
        /// </summary>
        public bool IsShuffleEnabled
        {
            get => PlayerControl?.IsShuffleEnabled ?? _audioService.IsShuffleEnabled;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsShuffleEnabled = value;
                }
                else
                {
                    _audioService.IsShuffleEnabled = value;
                }
                OnPropertyChanged();
            }
        }

        private Album? _currentAlbum;

        /// <summary>
        /// 現在再生中のアルバムを取得または設定します
        /// </summary>
        public Album? CurrentAlbum
        {
            get => _currentAlbum;
            set
            {
                _currentAlbum = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 現在再生中のトラックを取得または設定します
        /// </summary>
        public Track? CurrentTrack
        {
            get => PlayerControl?.CurrentTrack ?? _currentTrack;
            set
            {
                _currentTrack = value;
                if (PlayerControl != null)
                {
                    PlayerControl.CurrentTrack = value;
                }
                OnPropertyChanged();

                if (value != null)
                {
                    CurrentAlbum = Albums.FirstOrDefault(a => a.Tracks.Contains(value));
                }
                else
                {
                    CurrentAlbum = null;
                }

                OnPropertyChanged(nameof(HasCurrentTrack));
                OnPropertyChanged(nameof(HasRightExtensionTarget));
                OnPropertyChanged(nameof(CanToggleAlbumViewSizeMode));
                NotifyTagsTargetProperties();
                RaiseRightExtensionCommandsCanExecuteChanged();
            }
        }

        /// <summary>
        /// 現在再生中かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsPlaying
        {
            get => PlayerControl?.IsPlaying ?? _isPlaying;
            set
            {
                _isPlaying = value;
                if (PlayerControl != null)
                {
                    PlayerControl.IsPlaying = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 現在の再生時間表示文字列を取得または設定します
        /// </summary>
        public string CurrentTimeDisplay
        {
            get => PlayerControl?.CurrentTimeDisplay ?? _currentTimeDisplay;
            set
            {
                _currentTimeDisplay = value;
                if (PlayerControl != null)
                {
                    PlayerControl.CurrentTimeDisplay = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 総再生時間表示文字列を取得または設定します
        /// </summary>
        public string TotalTimeDisplay
        {
            get => PlayerControl?.TotalTimeDisplay ?? _totalTimeDisplay;
            set
            {
                _totalTimeDisplay = value;
                if (PlayerControl != null)
                {
                    PlayerControl.TotalTimeDisplay = value;
                }
                OnPropertyChanged();
            }
        }

        private bool _isDraggingProgress;
        /// <summary>
        /// 現在の再生位置（進捗、0-100）
        /// ドラッグ操作中は再生位置を更新しません
        /// </summary>
        public double Progress
        {
            get => PlayerControl?.Progress ?? _progress;
            set
            {
                _progress = value;
                if (PlayerControl != null)
                {
                    PlayerControl.Progress = value;
                }
                else if (_isDraggingProgress)
                {
                    _audioService.SeekTo(value);
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// シークバーがドラッグ操作中かどうか
        /// </summary>
        public bool IsDraggingProgress
        {
            get => PlayerControl?.IsDraggingProgress ?? _isDraggingProgress;
            set
            {
                _isDraggingProgress = value;
                if (PlayerControl != null)
                {
                    PlayerControl.IsDraggingProgress = value;
                }
            }
        }

        /// <summary>
        /// 指定パーセント位置へシークします
        /// </summary>
        /// <param name="percentage">シーク位置（0-100）</param>
        public void Seek(double percentage)
        {
            if (PlayerControl != null)
            {
                PlayerControl.Seek(percentage);
            }
            else
            {
                _progress = percentage;
                OnPropertyChanged(nameof(Progress));
                _audioService.SeekTo(percentage);
            }
        }

        /// <summary>
        /// "Now Playing"セクションが表示されているかどうか
        /// </summary>
        public bool IsNowPlayingVisible
        {
            get => _isNowPlayingVisible;
            set
            {
                _isNowPlayingVisible = value;
                OnPropertyChanged();
            }
        }

        // コマンド定義

        private bool _isDeviceConnected;

        /// <summary>
        /// 外部デバイスが接続されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set
            {
                if (_isDeviceConnected != value)
                {
                    _isDeviceConnected = value;
                    OnPropertyChanged();
                    if (_isDeviceConnected)
                    {
                        DeviceBrowser.RefreshDrives();
                    }
                    else if (CurrentViewType == ViewType.DeviceSync)
                    {
                        CurrentViewType = ViewType.Albums;
                    }
                }
            }
        }

        /// <summary>
        /// 再生/一時停止を切り替えるコマンドを取得します
        /// </summary>
        public ICommand TogglePlayPauseCommand { get; }

        /// <summary>
        /// 次のトラックへ進むコマンドを取得します
        /// </summary>
        public ICommand NextCommand { get; }

        /// <summary>
        /// 前のトラックへ戻るコマンドを取得します
        /// </summary>
        public ICommand PreviousCommand { get; }

        /// <summary>
        /// 指定したトラックを再生するコマンドを取得します
        /// </summary>
        public ICommand PlayTrackCommand { get; }

        /// <summary>
        /// お気に入り状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>
        /// 表示ビューの切り替えコマンドを取得します
        /// </summary>
        public ICommand ToggleViewCommand { get; }

        /// <summary>
        /// お気に入り楽曲一覧を表示するコマンドを取得します
        /// </summary>
        public ICommand ShowFavoritesCommand { get; }

        /// <summary>
        /// ライブラリ一覧を表示するコマンドを取得します
        /// </summary>
        public ICommand ShowLibraryCommand { get; }

        /// <summary>
        /// フォルダーツリービューを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowFolderCommand { get; }

        /// <summary>
        /// アルバム全体を再生するコマンドを取得します
        /// </summary>
        public ICommand PlayAlbumCommand { get; }

        /// <summary>
        /// 次に再生するアルバムとしてキューに追加するコマンドを取得します
        /// </summary>
        public ICommand PlayNextAlbumCommand { get; }

        /// <summary>
        /// アルバムをキューの末尾に追加するコマンドを取得します
        /// </summary>
        public ICommand EnqueueAlbumCommand { get; }

        /// <summary>
        /// アルバムを削除するコマンドを取得します
        /// </summary>
        public ICommand DeleteAlbumCommand { get; }

        /// <summary>
        /// 指定トラックを次に再生するようキューに追加するコマンドを取得します
        /// </summary>
        public ICommand PlayNextCommand { get; }

        /// <summary>
        /// 指定トラックをキューの末尾に追加するコマンドを取得します
        /// </summary>
        public ICommand EnqueueTrackCommand { get; }

        /// <summary>
        /// トラックプロパティを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowTrackPropertiesCommand { get; }

        /// <summary>
        /// ファイルの保存場所をエクスプローラーで開くコマンドを取得します
        /// </summary>
        public ICommand OpenFileLocationCommand { get; }

        /// <summary>
        /// トラックを削除するコマンドを取得します
        /// </summary>
        public ICommand DeleteTrackCommand { get; }

        /// <summary>
        /// ミニプレイヤーの連結再生キューが開いているかどうかを取得または設定します
        /// </summary>
        public bool IsMiniPlayerQueueOpen
        {
            get => PlayerControl?.IsMiniPlayerQueueOpen ?? false;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsMiniPlayerQueueOpen = value;
                }
            }
        }

        /// <summary>
        /// ミニプレイヤーの連結再生キュー開閉を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleMiniPlayerQueueCommand { get; }

        /// <summary>
        /// ミニプレイヤーの連結再生キューを閉じるコマンドを取得します
        /// </summary>
        public ICommand CloseMiniPlayerQueueCommand { get; }

        /// <summary>
        /// 再生キュー内の指定曲を再生するコマンドを取得します
        /// </summary>
        public ICommand PlayFromQueueCommand { get; }

        // Device Sync Commands

        /// <summary>
        /// デバイス同期画面へ切り替えるコマンドを取得します
        /// </summary>
        public ICommand SwitchToDeviceSyncCommand { get; }

        private bool _isAscending = true;

        /// <summary>
        /// ソート順が昇順かどうかを取得または設定します
        /// </summary>
        public bool IsAscending
        {
            get => _isAscending;
            set
            {
                _isAscending = value;
                OnPropertyChanged();
                SortLibrary();
            }
        }

        private bool _isSelectionMode;
        /// <summary>
        /// トラック選択モードが有効かどうか
        /// </summary>
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set
            {
                _isSelectionMode = value;
                OnPropertyChanged();
            }
        }

        private bool _isAlbumRepeat;
        /// <summary>
        /// アルバムリピートモードが有効かどうか
        /// </summary>
        public bool IsAlbumRepeat
        {
            get => PlayerControl?.IsAlbumRepeat ?? _isAlbumRepeat;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsAlbumRepeat = value;
                }
                else
                {
                    _isAlbumRepeat = value;
                    _audioService.IsRepeatEnabled = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ソート順（昇順/降順）を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleSortDirectionCommand { get; }

        /// <summary>
        /// トラック選択モードの有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleSelectionModeCommand { get; }

        /// <summary>
        /// シャッフル再生の有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleShuffleCommand { get; }

        /// <summary>
        /// リピート再生の有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleRepeatCommand { get; }

        /// <summary>
        /// 音量を上げるコマンドを取得します
        /// </summary>
        public ICommand IncreaseVolumeCommand { get; }

        /// <summary>
        /// 音量を下げるコマンドを取得します
        /// </summary>
        public ICommand DecreaseVolumeCommand { get; }

        /// <summary>
        /// 再生を停止するコマンドを取得します
        /// </summary>
        public ICommand StopCommand { get; }

        /// <summary>
        /// ミュート状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleMuteCommand { get; }

        private bool _isMuted;

        /// <summary>
        /// ミュート状態
        /// </summary>
        public bool IsMuted
        {
            get => PlayerControl?.IsMuted ?? _isMuted;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsMuted = value;
                }
                else
                {
                    _isMuted = value;
                }
                OnPropertyChanged();
            }
        }

        private void ToggleMute()
        {
            PlayerControl?.ToggleMute();
        }

        /// <summary>
        /// 音量（0.0 - 1.0）
        /// 変更時に設定ファイルへ保存されます
        /// </summary>
        public float Volume
        {
            get => PlayerControl?.Volume ?? _audioService.Volume;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.Volume = value;
                }
                else if (_audioService.Volume != value)
                {
                    _audioService.Volume = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumePercent));

                    // Save settings
                    // 設定を保存
                    _settingsService.UpdateSettings(settings => settings.Volume = value);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercent));
            }
        }

        /// <summary>
        /// 音量のパーセント表示文字列を取得します
        /// </summary>
        public string VolumePercent => PlayerControl?.VolumePercent ?? $"{(int)(Volume * 100)}%";

        /// <summary>
        /// 外部デバイス上のディレクトリまたはファイルアイテムを表すクラス
        /// </summary>
        public class DirectoryItem
        {
            /// <summary>
            /// アイテム名を取得または設定します
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// アイテムのフルパスを取得または設定します
            /// </summary>
            public string FullPath { get; set; } = string.Empty;

            /// <summary>
            /// フォルダーかどうかを示す値を取得または設定します
            /// </summary>
            public bool IsFolder { get; set; }
        }

        private GridLength _leftColumnWidth = new GridLength(300);
        /// <summary>
        /// 左カラム（サイドバー）の幅
        /// </summary>
        public GridLength LeftColumnWidth
        {
            get => _leftColumnWidth;
            set
            {
                _leftColumnWidth = value;
                OnPropertyChanged();
                _settingsService.UpdateSettings(settings => settings.LeftColumnWidth = value.Value);
            }
        }

        /// <summary>
        /// 設定ダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowSettingsCommand { get; }



        /// <summary>
        /// ライブラリをソート条件（アーティスト/アルバム）と昇順/降順に従って並び替えます。
        /// </summary>
        private void SortLibrary()
        {
            Library?.SortLibrary();
        }




        private ImageSource? _spectrumBackgroundImageGray;

        /// <summary>
        /// スペクトラムアナライザーのグレースケール背景画像を取得または設定します
        /// </summary>
        public ImageSource? SpectrumBackgroundImageGray
        {
            get => _spectrumBackgroundImageGray;
            set
            {
                _spectrumBackgroundImageGray = value;
                OnPropertyChanged();
            }
        }

        private bool _isDefaultSpectrumImage;

        /// <summary>
        /// スペクトラムアナライザーの背景画像がデフォルト画像かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsDefaultSpectrumImage
        {
            get => _isDefaultSpectrumImage;
            set
            {
                _isDefaultSpectrumImage = value;
                OnPropertyChanged();
            }
        }

        private Brush? _spectrumBarBrush;
        /// <summary>
        /// スペクトラムアナライザーのバーのブラシ
        /// アルバムアートの主要な色に基づいて動的に更新されます
        /// </summary>
        public Brush SpectrumBarBrush
        {
            get
            {
                if (_spectrumBarBrush == null)
                {
                    // Default Gradient: Horizontal, Right (Low Sat) to Left (High Sat)
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new Point(1, 0.5); // Right
                    brush.EndPoint = new Point(0, 0.5);   // Left

                    // Right (High Freq): Low Saturation (pale), High Opacity (Bright)
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                    // Left (Low Freq): High Saturation (vibrant)
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));

                    _spectrumBarBrush = brush;
                }
                return _spectrumBarBrush;
            }

            set
            {
                _spectrumBarBrush = value;
                OnPropertyChanged();
            }
        }

        private Brush _spectrumBorderBrush = new SolidColorBrush(Color.FromArgb(230, 0, 229, 255)); // Default Neon Cyan Border (90%)

        /// <summary>
        /// スペクトラムアナライザーのボーダーブラシを取得または設定します
        /// </summary>
        public Brush SpectrumBorderBrush
        {
            get => _spectrumBorderBrush;
            set
            {
                _spectrumBorderBrush = value;
                OnPropertyChanged();
            }
        }

        private Color _spectrumShadowColor = Color.FromRgb(0, 229, 255); // Default Neon Cyan

        /// <summary>
        /// スペクトラムアナライザーのシャドウ色を取得または設定します
        /// </summary>
        public Color SpectrumShadowColor
        {
            get => _spectrumShadowColor;
            set
            {
                _spectrumShadowColor = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// アルバムアートを解析し、スペクトラムアナライザーの色（バー、ボーダー、シャドウ）を更新します。
        /// </summary>
        /// <param name="bitmap">解析対象の画像。</param>
        private void UpdateSpectrumBrush(BitmapSource bitmap)
        {
            try
            {
                // 1. Force Convert to Bgra32 to ensure byte order is B-G-R-A
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = bitmap;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                converted.Freeze();

                // 2. Resize to small size for performance
                var resized = new TransformedBitmap(converted, new ScaleTransform(100.0 / converted.PixelWidth, 100.0 / converted.PixelHeight));
                resized.Freeze();
                int width = resized.PixelWidth;
                int height = resized.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                resized.CopyPixels(pixels, stride, 0);

                // 3. Histogram / Bucketing Approach
                // Buckets for Hue (0-360), e.g., 36 buckets of 10 degrees
                long[] bucketR = new long[36];
                long[] bucketG = new long[36];
                long[] bucketB = new long[36];
                int[] bucketCount = new int[36];

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    // alpha is pixels[i+3], ignore

                    Color c = Color.FromRgb(r, g, b);
                    ColorToHsv(c, out double h, out double s, out double v);

                    // Skip grays, blacks, whites
                    if (s < 0.2 || v < 0.2) continue;

                    int bucketIndex = (int)(h / 10.0);
                    if (bucketIndex >= 36) bucketIndex = 35;

                    bucketR[bucketIndex] += r;
                    bucketG[bucketIndex] += g;
                    bucketB[bucketIndex] += b;
                    bucketCount[bucketIndex]++;
                }

                // Find winning bucket
                int maxCount = 0;
                int winningBucket = -1;
                for (int i = 0; i < 36; i++)
                {
                    if (bucketCount[i] > maxCount)
                    {
                        maxCount = bucketCount[i];
                        winningBucket = i;
                    }
                }

                Color finalColor;
                if (winningBucket != -1)
                {
                    byte avgR = (byte)(bucketR[winningBucket] / bucketCount[winningBucket]);
                    byte avgG = (byte)(bucketG[winningBucket] / bucketCount[winningBucket]);
                    byte avgB = (byte)(bucketB[winningBucket] / bucketCount[winningBucket]);
                    finalColor = Color.FromRgb(avgR, avgG, avgB);
                }
                else
                {
                    // Fallback to simple average if no vibrant pixels found
                    long sumR = 0, sumG = 0, sumB = 0;
                    int total = 0;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        sumB += pixels[i];
                        sumG += pixels[i + 1];
                        sumR += pixels[i + 2];
                        total++;
                    }
                    if (total > 0)
                    {
                        finalColor = Color.FromRgb((byte)(sumR / total), (byte)(sumG / total), (byte)(sumB / total));
                    }
                    else
                    {
                        finalColor = Color.FromRgb(64, 235, 255);
                    }
                }

                // 4. Boost Saturation/Value
                ColorToHsv(finalColor, out double fh, out double fs, out double fv);

                // Vertical Neon Gradient (Bottom -> Top)
                var brush = new LinearGradientBrush();
                brush.StartPoint = new Point(0.5, 1.0); // Bottom
                brush.EndPoint = new Point(0.5, 0.0);   // Top

                // Bottom: Deep Vibrant Purple/Magenta (Offset 0.0)
                Color colorBottom = HsvToColor((fh + 40) % 360, 0.85, 0.85);
                colorBottom.A = 220;

                // Middle: Bright Vibrant Neon Accent (Offset 0.5)
                Color colorMid = HsvToColor(fh, 0.95, 1.0);
                colorMid.A = 255;

                // Top: Luminous White-tinted Neon (Offset 1.0)
                Color colorTop = HsvToColor(fh, 0.15, 1.0);
                colorTop.A = 255;

                brush.GradientStops.Add(new GradientStop(colorBottom, 0.0));
                brush.GradientStops.Add(new GradientStop(colorMid, 0.5));
                brush.GradientStops.Add(new GradientStop(colorTop, 1.0));
                brush.Freeze();

                SpectrumBarBrush = brush;
                SpectrumShadowColor = HsvToColor(fh, 1.0, 1.0);

                Color borderColor = HsvToColor(fh, 0.2, 1.0);
                borderColor.A = 240;
                var solidBorderBrush = new SolidColorBrush(borderColor);
                solidBorderBrush.Freeze();
                SpectrumBorderBrush = solidBorderBrush;
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new Point(0.5, 1.0);
                    brush.EndPoint = new Point(0.5, 0.0);

                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(220, 139, 92, 246), 0.0)); // Deep Purple
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 229, 255), 0.5));   // Electric Cyan
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0)); // White Neon Tip
                    brush.Freeze();

                    SpectrumBarBrush = brush;
                    SpectrumShadowColor = Color.FromRgb(0, 229, 255);

                    var borderColor = Color.FromArgb(240, 204, 249, 255);
                    var solidBorderBrush = new SolidColorBrush(borderColor);
                    solidBorderBrush.Freeze();
                    SpectrumBorderBrush = solidBorderBrush;
                });
            }
        }

        private void OnPlaylistChanged(List<Track> playlist)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlayQueue = new System.Collections.ObjectModel.ObservableCollection<Track>(playlist);
                PlayerControl?.SyncTrackPlayingStates(CurrentTrack);
            });
        }

        /// <summary>
        /// 再生中のトラックが変更されたときに呼び出されます。
        /// スペクトラムの更新、アルバムアートの読み込み、背景画像の更新を行います。
        /// </summary>
        /// <param name="track">新しいトラック。</param>
        private void OnTrackChanged(Track? track)
        {
            if (CurrentTrack != null)
            {
                CurrentTrack.IsPlaying = false;
            }

            CurrentTrack = track;
            Progress = 0;
            int artworkGeneration = System.Threading.Interlocked.Increment(ref _trackArtworkGeneration);

            if (track == null)
            {
                CurrentAlbum = null;
                PlaybackListTracks = new ObservableCollection<Track>();
                PlaybackListName = "No Album Selected";
                PlaybackListSubtitle = string.Empty;
                PlayerControl?.SyncTrackPlayingStates(null);
                RunOnUiThread(() =>
                {
                    if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                    {
                        return;
                    }

                    NowPlayingImage = _defaultNowPlayingImage;
                    MiniPlayerAlbumArtImage = null;
                    SpectrumBackgroundImage = _defaultSpectrumImage;
                    SpectrumBackgroundImageGray = null;
                    IsDefaultSpectrumImage = true;
                });
                return;
            }

            track.IsPlaying = true;

            PlayerControl?.SyncTrackPlayingStates(track);

            if (track != null)
            {
                _lastPlayedTrack = track;
                _lastPlayedAlbum = CurrentAlbum;
                RunOnUiThread(() =>
                {
                    if (!IsPlaylistTracksVisible && PlaybackListTracks != null && !PlaybackListTracks.Any(t => PlayerControlViewModel.IsSameTrack(t, track)))
                    {
                        var album = Albums.FirstOrDefault(a => a.Tracks.Any(t => PlayerControlViewModel.IsSameTrack(t, track)));
                        if (album != null)
                        {
                            PlaybackListName = album.Title;
                            PlaybackListSubtitle = album.Artist;
                            PlaybackListTracks = new System.Collections.ObjectModel.ObservableCollection<Track>(album.Tracks);
                        }
                    }
                    PlayerControl?.SyncTrackPlayingStates(track);
                });
            }

            // Phase 9: Logic Update
            // If viewing Favorites, FORCE Background to Galaxy.
            if (IsFavoritesView && _favoritesImage != null)
            {
                Playlist?.SetFavoritesBackgroundImage(_favoritesImage);
            }
            // If viewing anything else (Playlist), use Track Art (NowPlayingImage).
            // But NowPlayingImage is not yet updated here (async).
            // So we wait for async block below.

            var customArtworkLoader = TrackArtworkBytesLoader;
            if (track != null && (customArtworkLoader != null || File.Exists(track.FilePath)))
            {
                _ = LoadAndApplyTrackArtworkAsync(track.FilePath, artworkGeneration, customArtworkLoader);
            }
            else
            {
                RunOnUiThread(() =>
                {
                    if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                    {
                        return;
                    }

                    NowPlayingImage = _defaultNowPlayingImage;
                    MiniPlayerAlbumArtImage = null;
                    SpectrumBackgroundImage = _defaultSpectrumImage;
                    SpectrumBackgroundImageGray = null;
                    IsDefaultSpectrumImage = true;
                    ApplyDefaultSpectrumBrushes();
                });
            }
        }

        private static byte[]? ReadEmbeddedArtworkBytes(string filePath)
        {
            using var tfile = TagLib.File.Create(filePath);
            if (tfile.Tag.Pictures.Length > 0)
            {
                return tfile.Tag.Pictures[0].Data.Data;
            }

            return null;
        }

        private async Task LoadAndApplyTrackArtworkAsync(string filePath, int artworkGeneration, Func<string, Task<byte[]?>>? customArtworkLoader)
        {
            try
            {
                byte[]? bin = customArtworkLoader != null
                    ? await customArtworkLoader(filePath).ConfigureAwait(false)
                    : await Task.Run(() => ReadEmbeddedArtworkBytes(filePath)).ConfigureAwait(false);

                if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                {
                    return;
                }

                if (bin != null && bin.Length > 0)
                {
                    RunOnUiThread(() =>
                    {
                        if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                        {
                            return;
                        }

                        try
                        {
                            var image = new BitmapImage();
                            using (var mem = new MemoryStream(bin))
                            {
                                mem.Position = 0;
                                image.BeginInit();
                                image.DecodePixelWidth = 500;
                                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.UriSource = null;
                                image.StreamSource = mem;
                                image.EndInit();
                            }
                            image.Freeze();

                            if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                            {
                                return;
                            }

                            NowPlayingImage = image;
                            MiniPlayerAlbumArtImage = image;
                            SpectrumBackgroundImage = image; // Keep Color

                            // Phase 9: Update PlaylistBackgroundImage if NOT favorites view
                            if (!IsFavoritesView)
                            {
                                Playlist?.SetBackgroundImage(image);
                            }

                            // Create Grayscale version for Spectrum Background Overlay
                            var grayImage = new FormatConvertedBitmap();
                            grayImage.BeginInit();
                            grayImage.Source = image;
                            grayImage.DestinationFormat = PixelFormats.Gray8;
                            grayImage.EndInit();
                            grayImage.Freeze();

                            SpectrumBackgroundImageGray = grayImage;
                            IsDefaultSpectrumImage = false;

                            // Update Spectrum Bar Color
                            UpdateSpectrumBrush(image);
                        }
                        catch
                        {
                            if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                            {
                                return;
                            }

                            // 画像デコード中断 (COMException E_ABORT) 等の例外を安全に無視・フォールバック
                            NowPlayingImage = _defaultNowPlayingImage;
                            MiniPlayerAlbumArtImage = null;
                            SpectrumBackgroundImage = _defaultSpectrumImage;
                            SpectrumBackgroundImageGray = null;
                        }
                    });
                }
                else
                {
                    RunOnUiThread(() =>
                    {
                        if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                        {
                            return;
                        }

                        try
                        {
                            NowPlayingImage = _defaultNowPlayingImage;
                            MiniPlayerAlbumArtImage = null;
                            SpectrumBackgroundImage = _defaultSpectrumImage;
                            SpectrumBackgroundImageGray = null;

                            // Border: Brighter (Lower Saturation), 100% Opacity
                            var borderColor = Color.FromRgb(204, 249, 255);
                            borderColor.A = 255;
                            var solidBorderBrush = new SolidColorBrush(borderColor);
                            solidBorderBrush.Freeze();
                            SpectrumBorderBrush = solidBorderBrush;
                        }
                        catch
                        {
                            // 例外無視
                        }
                    });
                }
            }
            catch
            {
                if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                {
                    return;
                }

                RunOnUiThread(() =>
                {
                    if (System.Threading.Volatile.Read(ref _trackArtworkGeneration) != artworkGeneration)
                    {
                        return;
                    }

                    NowPlayingImage = _defaultNowPlayingImage;
                    MiniPlayerAlbumArtImage = null;
                    SpectrumBackgroundImage = _defaultSpectrumImage;
                    SpectrumBackgroundImageGray = null;
                    ApplyDefaultSpectrumBrushes();
                });
            }
        }

        private void ApplyDefaultSpectrumBrushes()
        {
            // Default Gradient: Right to Left
            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(1, 0.5);
            brush.EndPoint = new Point(0, 0.5);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));
            brush.Freeze();
            SpectrumBarBrush = brush;
            SpectrumShadowColor = Color.FromRgb(0, 229, 255);

            // Border: Brighter (Lower Saturation), 100% Opacity
            var borderColor = Color.FromRgb(204, 249, 255);
            borderColor.A = 255;
            var solidBorderBrush = new SolidColorBrush(borderColor);
            solidBorderBrush.Freeze();
            SpectrumBorderBrush = solidBorderBrush;
        }

        // HSV Helpers
        /// <summary>
        /// RGBカラーをHSV色空間に変換します。
        /// </summary>
        private void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));

            // Calculate Hue
            if (max == min)
            {
                hue = 0;
            }
            else if (max == color.R)
            {
                hue = (60 * (color.G - color.B) / (double)(max - min) + 360) % 360;
            }
            else if (max == color.G)
            {
                hue = (60 * (color.B - color.R) / (double)(max - min) + 120);
            }
            else
            {
                hue = (60 * (color.R - color.G) / (double)(max - min) + 240);
            }

            saturation = (max == 0) ? 0 : 1d - (1d * min / max);
            value = max / 255d;
        }

        /// <summary>
        /// HSV色空間からRGBカラーを作成します。
        /// </summary>
        private Color HsvToColor(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromRgb((byte)v, (byte)t, (byte)p);
            else if (hi == 1)
                return Color.FromRgb((byte)q, (byte)v, (byte)p);
            else if (hi == 2)
                return Color.FromRgb((byte)p, (byte)v, (byte)t);
            else if (hi == 3)
                return Color.FromRgb((byte)p, (byte)q, (byte)v);
            else if (hi == 4)
                return Color.FromRgb((byte)t, (byte)p, (byte)v);
            else
                return Color.FromRgb((byte)v, (byte)p, (byte)q);
        }

        private void OnPlaybackStateChanged(bool isPlaying)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPlaying = isPlaying;
                if (isPlaying)
                {
                    _timer.Start();
                }
                else
                {
                    _timer.Stop();
                    // Do not reset images here to persist album art on stop
                }
            });
        }

        private int _tickCount;
        private bool _isCheckingDevices;

        private void OnTimerTick(object? sender, EventArgs e)
        {
            // デバイス接続状態を2秒おき(4 ticks = 500ms * 4)に非同期でチェック
            _tickCount++;
            if (_tickCount >= 4 && !_isCheckingDevices)
            {
                _tickCount = 0;
                _isCheckingDevices = true;

                Task.Run(() =>
                {
                    bool hasRemovable = DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.Removable && d.IsReady);
                    bool hasMtp = false;
                    try
                    {
                        hasMtp = MediaDevice.GetDevices().Any();
                    }
                    catch
                    {
                        // Ignore MTP exceptions
                    }

                    bool connected = hasRemovable || hasMtp;

                    App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsDeviceConnected = connected;

                        // もしデバイスが取り外されて、かつデバイス同期ビューが開いていたら閉じる
                        if (!IsDeviceConnected && CurrentViewType == ViewType.DeviceSync)
                        {
                            CurrentViewType = ViewType.Albums;
                        }

                        _isCheckingDevices = false;
                    });
                });
            }
        }



        /// <summary>
        /// 指定されたフォルダー（または設定された最後のパス）からライブラリを非同期でロードします。
        /// </summary>
        private void LoadLibrary(string? rootFolder = null)
        {
            Library?.LoadLibrary(rootFolder);
        }

        private void ToggleFavorite(object? obj)
        {
            var targetTrack = obj as Track ?? CurrentTrack;
            if (targetTrack != null)
            {
                Library?.ToggleFavorite(targetTrack);
            }
        }

        private void OnPlaylistEnded(object? sender, EventArgs e)
        {
            // If repeat is OFF, try to play next album
            if (!IsAlbumRepeat)
            {
                Action action = () =>
                {
                    var targetTrack = CurrentTrack ?? _lastPlayedTrack;
                    var targetAlbum = CurrentAlbum ?? _lastPlayedAlbum;
                    if (targetAlbum == null && targetTrack != null)
                    {
                        targetAlbum = Albums.FirstOrDefault(a => a.Tracks.Any(t => t.FilePath == targetTrack.FilePath));
                    }

                    if (targetAlbum != null)
                    {
                        int index = Albums.IndexOf(targetAlbum);
                        if (index >= 0 && index < Albums.Count - 1)
                        {
                            var nextAlbum = Albums[index + 1];
                            if (nextAlbum.Tracks.Count > 0)
                            {
                                if (Library != null)
                                {
                                    Library.PlayAlbum(nextAlbum);
                                }
                                else
                                {
                                    var tracks = nextAlbum.Tracks.ToList();
                                    Track startTrack;
                                    if (_audioService.IsShuffleEnabled)
                                    {
                                        var rng = new Random();
                                        startTrack = tracks[rng.Next(tracks.Count)];
                                    }
                                    else
                                    {
                                        startTrack = tracks.First();
                                    }

                                    PlaybackListName = nextAlbum.Title;
                                    PlaybackListSubtitle = nextAlbum.Artist;
                                    PlaybackListTracks = new ObservableCollection<Track>(tracks);
                                    PlayerControl?.SetPlaybackList(tracks, nextAlbum.Title, nextAlbum.Artist, updateAudioService: false);

                                    _audioService.SetPlaylist(tracks, startTrack);
                                    _audioService.PlayTrack(startTrack);
                                }
                            }
                        }
                    }
                };

                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(action);
                }
                else
                {
                    action();
                }
            }
        }

        /// <summary>
        /// リソースのクリーンアップを行います
        /// </summary>
        public void Cleanup()
        {
            _timer.Stop();
            if (Lyrics != null)
            {
                Lyrics.IsActiveView = false;
            }
            _audioService.Dispose();
        }

        /// <summary>
        /// アルバムセクション（アルバム一覧またはアルバム情報詳細）がアクティブかどうかを示す値を取得します
        /// </summary>
        public bool IsAlbumsSectionActive => CurrentViewType is ViewType.Albums or ViewType.AlbumDetail;

        /// <summary>
        /// 指定されたアルバムの詳細情報画面へ遷移します
        /// </summary>
        /// <param name="album">表示対象のアルバム</param>
        public void NavigateToAlbumDetail(Album album)
        {
            ArgumentNullException.ThrowIfNull(album);
            AlbumDetail.SetAlbum(album);
            CurrentViewType = ViewType.AlbumDetail;
        }

        /// <summary>
        /// プレイリストセクションがアクティブかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistSectionActive => CurrentViewType is ViewType.Playlists or ViewType.PlaylistTracks;

        /// <summary>
        /// 現在お気に入り画面を表示しているかどうかを示す値を取得します
        /// </summary>
        public bool IsFavoritesView => CurrentViewType == ViewType.Favorites;

        private void ShowFavorites()
        {
            CurrentViewType = ViewType.Favorites;
            var favoriteTracks = Library?.GetFavoriteTracks(path => LoadTrack(path, resolveFavoriteState: false)) ?? Array.Empty<Track>();
            Playlist?.ShowFavorites(favoriteTracks, _favoritesImage);
        }

        private void ShowLibrary()
        {
            CurrentViewType = ViewType.Albums;
        }

        private void ShowFolder()
        {
            CurrentViewType = ViewType.Folders;
        }

        /// <summary>
        /// 指定されたパスのトラック情報を読み込みます。
        /// メタデータとカバーアートを取得します。
        /// </summary>
        private Track? LoadTrack(string filePath, bool resolveFavoriteState = true)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var (title, artist, album, genre) = Infrastructure.Library.TagLibMetadataExtractor.ExtractDisplayTags(tagFile.Tag, filePath);
                var track = new Track
                {
                    Title = title,
                    Artist = artist,
                    AlbumArtist = Infrastructure.Library.TagLibMetadataExtractor.ExtractAlbumArtist(tagFile.Tag),
                    Album = album,
                    Genre = genre,
                    Year = tagFile.Tag.Year,
                    TrackNumber = tagFile.Tag.Track,
                    FilePath = filePath,
                    Duration = tagFile.Properties.Duration
                };
                if (resolveFavoriteState)
                {
                    track.IsFavorite = Library?.IsTrackFavorite(track) ?? false;
                }

                // Set quality information
                track.Bitrate = tagFile.Properties.AudioBitrate;
                track.SampleRate = tagFile.Properties.AudioSampleRate;
                track.BitsPerSample = tagFile.Properties.BitsPerSample;

                string ext = Path.GetExtension(filePath).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                track.Format = ext.TrimStart('.').ToUpper(System.Globalization.CultureInfo.InvariantCulture);
                track.IsLossless = LosslessAudioExtensions.Contains(ext);
                track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;

                return track;
            }
            catch
            {
                return null;
            }
        }

        private void ShowSettings()
        {
            var settingsViewModel = new SettingsViewModel(
                _settingsService,
                _audioService,
                _dataDirectoryProvider,
                _dataDirectoryMigrationService,
                _logConfigurationManager);

            void OnDataDirectoryChanged() => ReloadDataAfterDirectoryChange();
            settingsViewModel.DataDirectoryChanged += OnDataDirectoryChanged;
            try
            {
                var settingsDialog = new SettingsDialog(settingsViewModel);
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    settingsDialog.Owner = System.Windows.Application.Current.MainWindow;
                }
                settingsDialog.ShowDialog();
            }
            finally
            {
                settingsViewModel.DataDirectoryChanged -= OnDataDirectoryChanged;
            }

            SettingsUpdated?.Invoke();
        }

        /// <summary>
        /// データ保存先ディレクトリの変更完了後に各画面の永続化データ（プレイリスト・お気に入り・再生履歴・イコライザープリセット・ライブラリトラック整合）を再読み込みします
        /// </summary>
        internal void ReloadDataAfterDirectoryChange()
        {
            Library?.ReloadFavoritesFromRepository();
            Playlist?.LoadPlaylists();
            if (IsFavoritesView)
            {
                ShowFavorites();
            }
            else if (Playlist?.SelectedPlaylist != null)
            {
                var selectedId = Playlist.SelectedPlaylist.Id;
                var matched = Playlist.UserPlaylists.FirstOrDefault(p => p.Id == selectedId);
                if (matched != null)
                {
                    _ = Playlist.SelectPlaylistAsync(matched);
                }
                else
                {
                    _ = Playlist.SelectPlaylistAsync(null);
                    if (CurrentViewType == ViewType.PlaylistTracks)
                    {
                        CurrentViewType = ViewType.Playlists;
                    }
                }
            }

            Recent.LoadHistory();
            if (Equalizer != null)
            {
                _ = Equalizer.LoadPresetsAsync();
            }

            if (Library != null
                && !string.IsNullOrWhiteSpace(Library.CurrentLibraryPath)
                && Directory.Exists(Library.CurrentLibraryPath))
            {
                _ = Library.RescanCurrentFolderAsync();
            }

            if (CurrentTrack != null)
            {
                OnPropertyChanged(nameof(CurrentTrack));
            }
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.InvokeAsync(action);
            }
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void OnAllSongsPlaybackRequested(IReadOnlyList<Track> tracks, Track? startTrack, string name, string subtitle)
        {
            PlaybackListName = name;
            PlaybackListSubtitle = subtitle;
            PlaybackListTracks = new ObservableCollection<Track>(tracks);
            PlayerControl?.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
        }

        private void OnAllSongsEnqueueRequested(IReadOnlyList<Track> tracks, bool playNext)
        {
            _audioService.EnqueueTracks(tracks, playNext);
        }

        private void OnArtistsPlaybackRequested(IReadOnlyList<Track> tracks, Track? startTrack, string name, string subtitle)
        {
            PlaybackListName = name;
            PlaybackListSubtitle = subtitle;
            PlaybackListTracks = new ObservableCollection<Track>(tracks);
            PlayerControl?.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
        }

        private void OnArtistsEnqueueRequested(IReadOnlyList<Track> tracks, bool playNext)
        {
            _audioService.EnqueueTracks(tracks, playNext);
        }

        private void OnRecentPlaybackRequested(IReadOnlyList<Track> tracks, Track? startTrack, string name, string subtitle)
        {
            PlaybackListName = name;
            PlaybackListSubtitle = subtitle;
            PlaybackListTracks = new ObservableCollection<Track>(tracks);
            PlayerControl?.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
        }

        private void OnLibraryAlbumDetailRequested(Album album)
        {
            NavigateToAlbumDetail(album);
        }

        private void OnAlbumDetailPlaybackRequested(IReadOnlyList<Track> tracks, Track? startTrack, string name, string subtitle)
        {
            PlaybackListName = name;
            PlaybackListSubtitle = subtitle;
            PlaybackListTracks = new ObservableCollection<Track>(tracks);
            PlayerControl?.SetPlaybackList(tracks, name, subtitle, updateAudioService: false);
        }

        private void OnAlbumDetailEnqueueRequested(IReadOnlyList<Track> tracks, bool playNext)
        {
            _audioService.EnqueueTracks(tracks, playNext);
        }

        private void OnAlbumDetailBackRequested()
        {
            CurrentViewType = ViewType.Albums;
        }

        private void ToggleAlbumViewSizeMode()
        {
            if (IsAlbumViewMaximized)
            {
                if (!HasCurrentTrack)
                {
                    return;
                }

                IsAlbumViewMaximized = false;
            }
            else
            {
                IsAlbumViewMaximized = true;
            }
        }

        /// <summary>
        /// 右側タブ下部拡張領域の表示タブ（歌詞 / ダッシュボード / タグ）を切り替えます
        /// </summary>
        /// <param name="tab">切り替え先タブ種別</param>
        public void SelectRightExtensionTab(RightExtensionPanelType tab)
        {
            if (tab == RightExtensionPanelType.None)
            {
                CloseRightExtensionPanel();
                return;
            }

            if (!HasRightExtensionTarget)
            {
                return;
            }

            if (!IsRightPanelOpen)
            {
                IsRightPanelOpen = true;
            }

            if (IsAlbumViewMaximized)
            {
                IsAlbumViewMaximized = false;
            }

            ActiveRightExtensionPanel = tab;
        }

        /// <summary>
        /// 指定楽曲を対象に右側タブ下部拡張領域へダッシュボードパネルを展開します
        /// </summary>
        /// <param name="targetTrack">対象楽曲（未指定時は現在再生中楽曲）</param>
        public void OpenTrackDashboard(Track? targetTrack = null)
        {
            Track? effectiveTrack = targetTrack ?? CurrentTrack;
            if (effectiveTrack == null)
            {
                return;
            }

            ListeningStats?.SetTargetTrack(effectiveTrack);
            OnPropertyChanged(nameof(HasRightExtensionTarget));

            if (!IsRightPanelOpen)
            {
                IsRightPanelOpen = true;
            }

            if (IsAlbumViewMaximized)
            {
                IsAlbumViewMaximized = false;
            }

            ActiveRightExtensionPanel = RightExtensionPanelType.Dashboard;
        }

        /// <summary>
        /// 指定楽曲を対象に右側タブ下部拡張領域へタグパネルを展開します
        /// </summary>
        /// <param name="targetTrack">対象楽曲（未指定時は現在再生中楽曲）</param>
        public void OpenTrackTags(Track? targetTrack = null)
        {
            Track? effectiveTrack = targetTrack ?? CurrentTrack;
            if (effectiveTrack == null)
            {
                return;
            }

            _selectedTagsTargetTrack = IsSameTrack(effectiveTrack, CurrentTrack) ? null : effectiveTrack;
            NotifyTagsTargetProperties();
            OnPropertyChanged(nameof(HasRightExtensionTarget));

            if (!IsRightPanelOpen)
            {
                IsRightPanelOpen = true;
            }

            if (IsAlbumViewMaximized)
            {
                IsAlbumViewMaximized = false;
            }

            ActiveRightExtensionPanel = RightExtensionPanelType.Tags;
        }

        /// <summary>
        /// タグパネルの表示対象を現在再生中の楽曲へ戻します
        /// </summary>
        public void ReturnTagsToPlayingTrack()
        {
            if (CurrentTrack == null)
            {
                return;
            }

            _selectedTagsTargetTrack = null;
            NotifyTagsTargetProperties();
        }

        private void CloseRightExtensionPanel()
        {
            ActiveRightExtensionPanel = RightExtensionPanelType.None;
            if (!IsAlbumViewMaximized)
            {
                IsAlbumViewMaximized = true;
            }
        }

        private void OnLyricsCloseRequested()
        {
            CloseRightExtensionPanel();
        }

        /// <summary>
        /// 指定楽曲を対象に右側タブ下部拡張領域へ LRC 自動生成パネルを展開します
        /// </summary>
        /// <param name="targetTrack">対象楽曲（未指定時は現在再生中楽曲）</param>
        public void OpenLrcGenerator(Track? targetTrack = null)
        {
            Track? effectiveTrack = targetTrack ?? CurrentTrack;
            if (effectiveTrack == null)
            {
                return;
            }

            LrcGenerator?.SetTargetTrack(effectiveTrack);
            OnPropertyChanged(nameof(HasRightExtensionTarget));

            if (!IsRightPanelOpen)
            {
                IsRightPanelOpen = true;
            }

            if (IsAlbumViewMaximized)
            {
                IsAlbumViewMaximized = false;
            }

            ActiveRightExtensionPanel = RightExtensionPanelType.LrcGenerator;
        }

        private void OnLrcGeneratorBackToLyricsRequested()
        {
            if (!HasRightExtensionTarget)
            {
                return;
            }

            if (IsAlbumViewMaximized)
            {
                IsAlbumViewMaximized = false;
            }

            ActiveRightExtensionPanel = RightExtensionPanelType.Lyrics;
        }

        private void OnLrcGeneratorCloseRequested()
        {
            CloseRightExtensionPanel();
        }

        private void NotifyRightExtensionTabPropertiesChanged()
        {
            OnPropertyChanged(nameof(IsLyricsTabSelected));
            OnPropertyChanged(nameof(IsDashboardTabSelected));
            OnPropertyChanged(nameof(IsTagsTabSelected));
            OnPropertyChanged(nameof(IsLyricsPanelVisible));
            OnPropertyChanged(nameof(IsLrcGeneratorPanelVisible));
            OnPropertyChanged(nameof(IsDashboardPanelVisible));
            OnPropertyChanged(nameof(IsTagsPanelVisible));
        }

        private void NotifyRightExtensionVisibilityChanged()
        {
            OnPropertyChanged(nameof(IsRightExtensionAreaVisible));
            OnPropertyChanged(nameof(IsLyricsViewVisible));
            OnPropertyChanged(nameof(IsLrcGeneratorViewVisible));
            OnPropertyChanged(nameof(IsDashboardViewVisible));
            OnPropertyChanged(nameof(IsTagsViewVisible));
            UpdateRightExtensionActiveViewStates();
        }

        private void NotifyTagsTargetProperties()
        {
            OnPropertyChanged(nameof(TagsTargetTrack));
            OnPropertyChanged(nameof(IsTagsShowingNonPlayingTrack));
            OnPropertyChanged(nameof(CanReturnTagsToPlayingTrack));
            OnPropertyChanged(nameof(TagsTargetTrackTitle));
            OnPropertyChanged(nameof(TagsTargetTrackArtist));
            RaiseRightExtensionCommandsCanExecuteChanged();
        }

        private void RaiseRightExtensionCommandsCanExecuteChanged()
        {
            _toggleAlbumViewSizeModeCommand?.RaiseCanExecuteChanged();
            _selectRightExtensionTabCommand?.RaiseCanExecuteChanged();
            _returnTagsToPlayingTrackCommand?.RaiseCanExecuteChanged();
            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateRightExtensionActiveViewStates()
        {
            if (Lyrics != null)
            {
                Lyrics.IsActiveView = IsLyricsViewVisible;
            }

            if (ListeningStats != null)
            {
                ListeningStats.IsActiveView = IsDashboardViewVisible;
            }
        }

        private static bool IsSameTrack(Track? a, Track? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(a.FilePath) && !string.IsNullOrWhiteSpace(b.FilePath))
            {
                return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
            }

            return a.Id == b.Id;
        }

        private void OnRecentEnqueueRequested(IReadOnlyList<Track> tracks, bool playNext)
        {
            _audioService.EnqueueTracks(tracks, playNext);
        }

        /// <summary>
        /// アンマネージドリソースおよびマネージドリソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer?.Stop();
                    if (Library != null)
                    {
                        Library.AlbumDetailRequested -= OnLibraryAlbumDetailRequested;
                    }
                    if (AlbumDetail != null)
                    {
                        AlbumDetail.PlaybackRequested -= OnAlbumDetailPlaybackRequested;
                        AlbumDetail.EnqueueRequested -= OnAlbumDetailEnqueueRequested;
                        AlbumDetail.BackRequested -= OnAlbumDetailBackRequested;
                        if (_ownsAlbumDetail)
                        {
                            AlbumDetail.Dispose();
                        }
                    }
                    if (AllSongs != null)
                    {
                        AllSongs.PlaybackRequested -= OnAllSongsPlaybackRequested;
                        AllSongs.EnqueueRequested -= OnAllSongsEnqueueRequested;
                        AllSongs.GenerateLrcRequested -= OpenLrcGenerator;
                        if (_ownsAllSongs)
                        {
                            AllSongs.Dispose();
                        }
                    }
                    if (Artists != null)
                    {
                        Artists.PlaybackRequested -= OnArtistsPlaybackRequested;
                        Artists.EnqueueRequested -= OnArtistsEnqueueRequested;
                        if (_ownsArtists)
                        {
                            Artists.Dispose();
                        }
                    }
                    if (Recent != null)
                    {
                        Recent.PlaybackRequested -= OnRecentPlaybackRequested;
                        Recent.EnqueueRequested -= OnRecentEnqueueRequested;
                        if (_ownsRecent)
                        {
                            Recent.Dispose();
                        }
                    }
                    if (Lyrics != null)
                    {
                        Lyrics.CloseRequested -= OnLyricsCloseRequested;
                        Lyrics.GenerateLrcRequested -= OpenLrcGenerator;
                        Lyrics.IsActiveView = false;
                        if (_ownsLyrics)
                        {
                            Lyrics.Dispose();
                        }
                    }
                    if (LrcGenerator != null)
                    {
                        LrcGenerator.BackToLyricsRequested -= OnLrcGeneratorBackToLyricsRequested;
                        LrcGenerator.CloseRequested -= OnLrcGeneratorCloseRequested;
                        if (_ownsLrcGenerator)
                        {
                            LrcGenerator.Dispose();
                        }
                    }
                    if (ListeningStats != null)
                    {
                        ListeningStats.IsActiveView = false;
                        if (_ownsListeningStats)
                        {
                            ListeningStats.Dispose();
                        }
                    }
                    _fallbackListeningStatsService?.Dispose();
                    Folder?.Dispose();
                    DeviceBrowser?.Dispose();
                    _fallbackAudioService?.Dispose();
                    _fallbackSettings?.Dispose();
                    _fallbackSettingsRepository?.Dispose();
                    (_audioService as IDisposable)?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// アルバム除去通知を受け取った際、ライブラリから実際に削除された曲のみを表示中プレイリストおよび再生一覧から除去します。
        /// アーティスト名やアルバム名の変更により旧アルバムが外れた場合でも、同一 TrackId の曲がライブラリに残存していれば表示を維持します。
        /// </summary>
        /// <param name="removedAlbum">一覧から外れたアルバム</param>
        /// <param name="remainingLibraryTracks">現在ライブラリに残存している全トラック</param>
        /// <param name="playlist">表示中プレイリストViewModel</param>
        /// <param name="playbackListTracks">再生トラック一覧</param>
        internal static void RemoveTracksNotInLibrary(
            Album removedAlbum,
            IEnumerable<Track>? remainingLibraryTracks,
            Presentation.ViewModels.PlaylistViewModel? playlist,
            ObservableCollection<Track> playbackListTracks)
        {
            if (removedAlbum == null)
            {
                return;
            }

            var remainingTrackIds = remainingLibraryTracks?.Select(t => t.Id).ToHashSet();
            foreach (var track in removedAlbum.Tracks)
            {
                if (remainingTrackIds != null && remainingTrackIds.Contains(track.Id))
                {
                    continue;
                }

                playlist?.RemoveDisplayedTrack(track);
                playbackListTracks.Remove(track);
            }
        }
    }
}
