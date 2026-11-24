using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// 楽曲情報をローカルJSONファイルおよびメモリキャッシュで管理するリポジトリ具象クラス
/// </summary>
public class JsonTrackRepository : ITrackRepository, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly Dictionary<TrackId, Track> _tracks = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private FileMetadataSnapshot _lastLoadedMetadata = FileMetadataSnapshot.Missing;
    private bool _isLoaded;
    private bool _disposed;

    /// <summary>
    /// 指定されたJSONファイルパスでJsonTrackRepositoryを初期化します
    /// </summary>
    /// <param name="filePath">JSONファイル保存先パス（未指定時はAppData内のtracks.json）</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonTrackRepository(string? filePath = null, IJsonFileLockHelper? fileLockHelper = null)
    {
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
            _filePath = Path.Combine(appDir, "tracks.json");
        }
        else
        {
            _filePath = filePath;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        if (_isLoaded && currentMetadata == _lastLoadedMetadata)
        {
            return;
        }

        using IDisposable fileLock = await _fileLockHelper
            .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await ReloadFromDiskCoreAsync(forceReload: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadFromDiskCoreAsync(bool forceReload, CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        if (!forceReload && _isLoaded && currentMetadata == _lastLoadedMetadata)
        {
            return;
        }

        if (!forceReload && !currentMetadata.Exists)
        {
            _tracks.Clear();
            _lastLoadedMetadata = FileMetadataSnapshot.Missing;
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var dtos = JsonSerializer.Deserialize<List<TrackDto>>(json) ?? throw new JsonException();
            var tracks = dtos.Select(dto => dto.ToEntity()).ToList();
            _tracks.Clear();
            foreach (var track in tracks)
            {
                _tracks[track.Id] = track;
            }
        }
        catch (FileNotFoundException)
        {
            _tracks.Clear();
            currentMetadata = FileMetadataSnapshot.Missing;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch when (!forceReload)
        {
            // 読み込みエラー時は空のまま開始
            _tracks.Clear();
        }

        _lastLoadedMetadata = currentMetadata;
        _isLoaded = true;
    }

    private async Task SaveToFileCoreAsync(CancellationToken cancellationToken)
    {
        var dtos = _tracks.Values.Select(TrackDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos, _jsonOptions);

        await _fileLockHelper.WriteAllTextAtomicAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(_filePath);
        _isLoaded = true;
    }

    /// <summary>
    /// 指定されたIDのトラックを取得します
    /// </summary>
    /// <param name="id">トラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トラックエンティティ（存在しない場合はnull）</returns>
    public async Task<Track?> GetByIdAsync(TrackId id, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _tracks.TryGetValue(id, out var track) ? track : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたファイルパスのトラックを取得します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トラックエンティティ（存在しない場合はnull）</returns>
    public async Task<Track?> GetByPathAsync(AudioPath filePath, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _tracks.Values.FirstOrDefault(t => string.Equals(t.FilePath, filePath.Value, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 登録されているすべてのトラックを取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>全トラックのリスト</returns>
    public async Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _tracks.Values.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 永続化ファイルの整合性を検証したうえで登録トラック件数を取得します。
    /// ファイル未作成時は正常な空ライブラリとして 0 を返し、JSON 破損や読み込み異常時は例外を伝播させます。
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>登録されているトラック件数</returns>
    public async Task<int> GetVerifiedCountAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            return _tracks.Count;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// キーワードでトラックを検索します
    /// </summary>
    /// <param name="keyword">検索キーワード</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>一致したトラックのリスト</returns>
    public async Task<IReadOnlyList<Track>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return await GetAllAsync(cancellationToken).ConfigureAwait(false);
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _tracks.Values
                .Where(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            t.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            t.Album.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// トラックを保存または更新します
    /// </summary>
    /// <param name="track">保存対象のトラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            _tracks[track.Id] = track;
            await SaveToFileCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 複数のトラックを一括で保存または更新します
    /// </summary>
    /// <param name="tracks">保存対象のトラックコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveRangeAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            foreach (var track in tracks)
            {
                _tracks[track.Id] = track;
            }

            await SaveToFileCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 指定されたIDのトラックを削除します
    /// </summary>
    /// <param name="id">削除対象のトラックID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task DeleteAsync(TrackId id, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(_filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            if (_tracks.Remove(id))
            {
                await SaveToFileCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _semaphore.Release();
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
                _semaphore.Dispose();
            }

            _disposed = true;
        }
    }

    private sealed class TrackDto
    {
        public Guid Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public long DurationTicks { get; set; }
        public uint Year { get; set; }
        public uint TrackNumber { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public string Format { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public bool IsLossless { get; set; }
        public bool IsHiRes { get; set; }

        public static TrackDto FromEntity(Track track) => new()
        {
            Id = track.Id.Value,
            FilePath = track.FilePath,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            DurationTicks = track.Duration.Ticks,
            Year = track.Year,
            TrackNumber = track.TrackNumber,
            Bitrate = track.Bitrate,
            SampleRate = track.SampleRate,
            BitsPerSample = track.BitsPerSample,
            Format = track.Format,
            Genre = track.Genre,
            IsFavorite = track.IsFavorite,
            IsLossless = track.IsLossless,
            IsHiRes = track.IsHiRes
        };

        public Track ToEntity() => new(
            id: TrackId.From(Id),
            filePath: AudioPath.Create(FilePath),
            title: Title,
            artist: Artist,
            album: Album,
            duration: TimeSpan.FromTicks(DurationTicks),
            year: Year,
            trackNumber: TrackNumber,
            bitrate: Bitrate,
            sampleRate: SampleRate,
            bitsPerSample: BitsPerSample,
            format: Format,
            genre: Genre,
            isFavorite: IsFavorite,
            isLossless: IsLossless,
            isHiRes: IsHiRes);
    }
}
