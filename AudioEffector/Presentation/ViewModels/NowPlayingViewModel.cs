using System;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Infrastructure.Library;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 現在再生中の楽曲のタイトル、アーティスト、アルバムアート、音質情報表示を担当するViewModel
/// </summary>
public class NowPlayingViewModel : ViewModelBase, IHandle<TrackChangedEvent>
{
    private readonly AlbumArtLoader _albumArtLoader;
    private readonly IEventBus _eventBus;

    private Track? _currentTrack;
    private byte[]? _albumArtBytes;
    private string _title = "No Track Playing";
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _qualityInfo = string.Empty;

    /// <summary>
    /// 現在再生中のトラック
    /// </summary>
    public Track? CurrentTrack
    {
        get => _currentTrack;
        set => SetProperty(ref _currentTrack, value);
    }

    /// <summary>
    /// アルバムアート画像のバイト配列
    /// </summary>
    public byte[]? AlbumArtBytes
    {
        get => _albumArtBytes;
        set => SetProperty(ref _albumArtBytes, value);
    }

    /// <summary>
    /// 楽曲タイトル
    /// </summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>
    /// アーティスト名
    /// </summary>
    public string Artist
    {
        get => _artist;
        set => SetProperty(ref _artist, value);
    }

    /// <summary>
    /// アルバム名
    /// </summary>
    public string Album
    {
        get => _album;
        set => SetProperty(ref _album, value);
    }

    /// <summary>
    /// 音質情報文字列（ハイレゾ/ロスレス/ビットレート等）
    /// </summary>
    public string QualityInfo
    {
        get => _qualityInfo;
        set => SetProperty(ref _qualityInfo, value);
    }

    /// <summary>
    /// NowPlayingViewModelを初期化します
    /// </summary>
    /// <param name="albumArtLoader">アルバムアートローダー</param>
    /// <param name="eventBus">イベントバス</param>
    public NowPlayingViewModel(AlbumArtLoader albumArtLoader, IEventBus eventBus)
    {
        _albumArtLoader = albumArtLoader ?? throw new ArgumentNullException(nameof(albumArtLoader));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        _eventBus.Subscribe<TrackChangedEvent>(HandleAsync);
    }

    /// <summary>
    /// トラック変更イベントを受信して画面表示を更新します
    /// </summary>
    /// <param name="domainEvent">トラック変更イベント</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task HandleAsync(TrackChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var track = domainEvent.Track;
        if (track == null)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                CurrentTrack = null;
                Title = string.Empty;
                Artist = string.Empty;
                Album = string.Empty;
                QualityInfo = string.Empty;
                AlbumArtBytes = null;
            });
            return;
        }

        byte[]? artBytes = await _albumArtLoader.GetAlbumArtBytesAsync(track.FilePath, cancellationToken);

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            CurrentTrack = track;
            Title = track.Title;
            Artist = track.Artist;
            Album = track.Album;
            QualityInfo = track.QualityInfo;
            AlbumArtBytes = artBytes;
        });
    }
}
