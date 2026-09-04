using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Library;

/// <summary>
/// アルバムアート画像を非同期で読み込み、LRUメモリキャッシュを提供するクラス
/// </summary>
public class AlbumArtLoader
{
    private const int MAX_CACHE_SIZE = 100;
    private readonly TagLibMetadataExtractor _extractor;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruList = new();
    private readonly object _lock = new();

    /// <summary>
    /// AlbumArtLoaderを初期化します
    /// </summary>
    /// <param name="extractor">メタデータ抽出器</param>
    public AlbumArtLoader(TagLibMetadataExtractor? extractor = null)
    {
        _extractor = extractor ?? new TagLibMetadataExtractor();
    }

    /// <summary>
    /// 指定された音声ファイルからアルバムアート画像のバイト配列を非同期で取得します（キャッシュ機能付き）
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>画像のバイト配列（存在しない場合はnull）</returns>
    public async Task<byte[]?> GetAlbumArtBytesAsync(AudioPath filePath, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(filePath.Value, out var cachedData))
            {
                // LRU更新
                _lruList.Remove(filePath.Value);
                _lruList.AddFirst(filePath.Value);
                return cachedData;
            }
        }

        var imageBytes = await _extractor.ExtractAlbumArtBytesAsync(filePath, cancellationToken);
        if (imageBytes != null)
        {
            lock (_lock)
            {
                if (!_cache.ContainsKey(filePath.Value))
                {
                    if (_lruList.Count >= MAX_CACHE_SIZE)
                    {
                        var oldest = _lruList.Last!.Value;
                        _lruList.RemoveLast();
                        _cache.Remove(oldest);
                    }

                    _cache[filePath.Value] = imageBytes;
                    _lruList.AddFirst(filePath.Value);
                }
            }
        }

        return imageBytes;
    }

    /// <summary>
    /// 指定された音声ファイルのアルバムアート画像をStreamとして非同期で取得します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>画像のMemoryStream（存在しない場合はnull）</returns>
    public async Task<Stream?> GetAlbumArtStreamAsync(AudioPath filePath, CancellationToken cancellationToken = default)
    {
        var bytes = await GetAlbumArtBytesAsync(filePath, cancellationToken);
        return bytes != null ? new MemoryStream(bytes) : null;
    }

    /// <summary>
    /// メモリキャッシュをクリアします
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }
}
