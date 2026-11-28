using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// 楽曲に対する歌詞の非同期取得・メモリキャッシュ管理・キャッシュ無効化および更新通知を統括するアプリケーションサービスです。
/// </summary>
public class LyricsApplicationService
{
    /// <summary>
    /// メモリキャッシュに保持する楽曲歌詞の既定最大件数。
    /// </summary>
    public const int DEFAULT_MAX_CACHE_ENTRIES = 64;

    private readonly ILyricsProvider _lyricsProvider;
    private readonly IEventBus _eventBus;
    private readonly int _maxCacheEntries;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lruList = new();

    /// <summary>
    /// キャッシュ無効化または外部からの歌詞更新時に発行されるイベントです。
    /// </summary>
    public event EventHandler<LyricsUpdatedEvent>? LyricsUpdated;

    /// <summary>
    /// <see cref="LyricsApplicationService"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="lyricsProvider">歌詞データプロバイダー。</param>
    /// <param name="eventBus">ドメインイベントバス。</param>
    public LyricsApplicationService(ILyricsProvider lyricsProvider, IEventBus eventBus)
        : this(lyricsProvider, eventBus, DEFAULT_MAX_CACHE_ENTRIES)
    {
    }

    /// <summary>
    /// キャッシュ上限件数を指定して <see cref="LyricsApplicationService"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="lyricsProvider">歌詞データプロバイダー。</param>
    /// <param name="eventBus">ドメインイベントバス。</param>
    /// <param name="maxCacheEntries">メモリキャッシュの上限件数（1 以上）。</param>
    internal LyricsApplicationService(ILyricsProvider lyricsProvider, IEventBus eventBus, int maxCacheEntries)
    {
        _lyricsProvider = lyricsProvider ?? throw new ArgumentNullException(nameof(lyricsProvider));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

        if (maxCacheEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCacheEntries), maxCacheEntries, "キャッシュ上限件数は 1 以上である必要があります。");
        }

        _maxCacheEntries = maxCacheEntries;
    }

    /// <summary>
    /// 指定された楽曲の歌詞データを非同期で取得します。
    /// キャッシュ済みの場合は重複 I/O を回避して即座に返却し、<paramref name="forceReload"/> が <see langword="true"/> の場合はキャッシュをバイパスして最新内容を読み込みます。
    /// </summary>
    /// <param name="track">歌詞を取得する対象楽曲。</param>
    /// <param name="forceReload">キャッシュを無視して強制再読み込みを行う場合は <see langword="true"/>。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>取得された <see cref="LyricsDocument"/>（該当歌詞が存在しない場合は <see langword="null"/>）。</returns>
    public async Task<LyricsDocument?> LoadForTrackAsync(
        Track track,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();

        string key = NormalizeKey(track.FilePath);
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (!forceReload && TryGetFromCache(key, out LyricsDocument? cached))
        {
            return cached;
        }

        LyricsDocument? loaded = await _lyricsProvider
            .LoadLyricsAsync(track, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        SetCache(key, loaded);
        return loaded;
    }

    /// <summary>
    /// 指定された楽曲ファイルパスに対応するキャッシュを破棄し、更新通知（イベントおよび <see cref="IEventBus"/>）を発行します。
    /// LRC 自動生成機能等で外部 <c>.lrc</c> ファイルが保存・更新された際の即時反映に使用します。
    /// </summary>
    /// <param name="trackFilePath">キャッシュを破棄する対象楽曲のファイルパス。</param>
    public void InvalidateCache(string trackFilePath)
    {
        string key = NormalizeKey(trackFilePath);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        RemoveFromCache(key);

        var updatedEvent = new LyricsUpdatedEvent(key, Document: null);
        LyricsUpdated?.Invoke(this, updatedEvent);
        _ = PublishEventSafeAsync(updatedEvent, CancellationToken.None);
    }

    /// <summary>
    /// 指定された楽曲ファイルパスに対応するキャッシュを破棄し、更新通知（イベントおよび <see cref="IEventBus"/>）を非同期で発行します。
    /// </summary>
    /// <param name="trackFilePath">キャッシュを破棄する対象楽曲のファイルパス。</param>
    /// <param name="updatedDocument">更新後の歌詞ドキュメント（再読み込みを購読側へ委ねる場合は <see langword="null"/>）。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>非同期タスク。</returns>
    public async Task InvalidateCacheAsync(
        string trackFilePath,
        LyricsDocument? updatedDocument = null,
        CancellationToken cancellationToken = default)
    {
        string key = NormalizeKey(trackFilePath);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        RemoveFromCache(key);

        var updatedEvent = new LyricsUpdatedEvent(key, updatedDocument);
        LyricsUpdated?.Invoke(this, updatedEvent);
        await _eventBus.PublishAsync(updatedEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 保持しているすべての歌詞キャッシュを消去します。
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cacheMap.Clear();
            _lruList.Clear();
        }
    }

    /// <summary>
    /// 指定された楽曲ファイルパスが現在キャッシュに存在するかどうかを確認します。
    /// </summary>
    /// <param name="trackFilePath">確認対象の楽曲ファイルパス。</param>
    /// <param name="document">キャッシュされていた歌詞ドキュメント（歌詞なしキャッシュの場合は <see langword="null"/>）。</param>
    /// <returns>キャッシュに存在する場合は <see langword="true"/>。</returns>
    public bool TryGetCached(string trackFilePath, out LyricsDocument? document)
    {
        string key = NormalizeKey(trackFilePath);
        if (string.IsNullOrEmpty(key))
        {
            document = null;
            return false;
        }

        return TryGetFromCache(key, out document);
    }

    private bool TryGetFromCache(string key, out LyricsDocument? document)
    {
        lock (_cacheLock)
        {
            if (_cacheMap.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                document = node.Value.Document;
                return true;
            }
        }

        document = null;
        return false;
    }

    private void SetCache(string key, LyricsDocument? document)
    {
        lock (_cacheLock)
        {
            if (_cacheMap.TryGetValue(key, out LinkedListNode<CacheEntry>? existingNode))
            {
                existingNode.Value = new CacheEntry(key, document);
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                return;
            }

            if (_cacheMap.Count >= _maxCacheEntries && _lruList.Last is not null)
            {
                LinkedListNode<CacheEntry> oldest = _lruList.Last;
                _lruList.RemoveLast();
                _cacheMap.Remove(oldest.Value.Key);
            }

            var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(key, document));
            _lruList.AddFirst(newNode);
            _cacheMap[key] = newNode;
        }
    }

    private void RemoveFromCache(string key)
    {
        lock (_cacheLock)
        {
            if (_cacheMap.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                _lruList.Remove(node);
                _cacheMap.Remove(key);
            }
        }
    }

    private async Task PublishEventSafeAsync(LyricsUpdatedEvent updatedEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(updatedEvent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 同期 InvalidateCache 経由の発行時は呼び出し元を阻害しない
        }
    }

    private static string NormalizeKey(string? filePath)
    {
        return string.IsNullOrWhiteSpace(filePath) ? string.Empty : filePath.Trim();
    }

    private readonly record struct CacheEntry(string Key, LyricsDocument? Document);
}
