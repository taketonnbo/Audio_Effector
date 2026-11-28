using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// 楽曲情報をローカルJSONファイルおよびメモリキャッシュで管理するリポジトリ具象クラス
/// </summary>
public class JsonTrackRepository : ITrackRepository, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Func<string> _filePathResolver;
    private readonly IJsonFileLockHelper _fileLockHelper;
    private readonly Dictionary<TrackId, Track> _tracks = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _lastLoadedFilePath;
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

        string resolvedFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "AudioEffector");
            Directory.CreateDirectory(appDir);
            resolvedFilePath = Path.Combine(appDir, "tracks.json");
        }
        else
        {
            resolvedFilePath = filePath;
        }

        _filePathResolver = () => resolvedFilePath;
    }

    /// <summary>
    /// データディレクトリプロバイダを指定してJsonTrackRepositoryを初期化します（保存先変更へ動的追従）
    /// </summary>
    /// <param name="dataDirectoryProvider">データディレクトリ解決プロバイダ</param>
    /// <param name="fileLockHelper">プロセス間ファイルロックおよび原子的書き込みヘルパー（未指定時は既定実装）</param>
    public JsonTrackRepository(IDataDirectoryProvider dataDirectoryProvider, IJsonFileLockHelper? fileLockHelper = null)
    {
        ArgumentNullException.ThrowIfNull(dataDirectoryProvider);
        _fileLockHelper = fileLockHelper ?? JsonFileLockHelper.Default;
        _filePathResolver = () => dataDirectoryProvider.GetFilePath("tracks.json");
    }

    private bool IsCacheFresh(string filePath, FileMetadataSnapshot currentMetadata) =>
        _isLoaded
        && string.Equals(_lastLoadedFilePath, filePath, StringComparison.OrdinalIgnoreCase)
        && currentMetadata == _lastLoadedMetadata;

    private async Task EnsureLoadedAsync(string filePath, CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(filePath);
        if (IsCacheFresh(filePath, currentMetadata))
        {
            return;
        }

        using IDisposable fileLock = await _fileLockHelper
            .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await ReloadFromDiskCoreAsync(filePath, forceReload: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadFromDiskCoreAsync(string filePath, bool forceReload, CancellationToken cancellationToken)
    {
        FileMetadataSnapshot currentMetadata = _fileLockHelper.GetFileMetadata(filePath);
        if (!forceReload && IsCacheFresh(filePath, currentMetadata))
        {
            return;
        }

        if (!forceReload && !currentMetadata.Exists)
        {
            _tracks.Clear();
            _lastLoadedFilePath = filePath;
            _lastLoadedMetadata = FileMetadataSnapshot.Missing;
            _isLoaded = true;
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var dtos = JsonSerializer.Deserialize<List<TrackDto>>(json) ?? throw new JsonException();
            var tracks = new List<Track>(dtos.Count);
            var parsedAlbumIds = new List<AlbumId?>(dtos.Count);
            var validAlbumIdsByKey = new Dictionary<(string NormalizedAlbum, string NormalizedArtist), AlbumId>();
            bool needsMigrationPersist = false;
            foreach (var dto in dtos)
            {
                var parsedAlbumId = TrackDto.TryParseAlbumId(dto.AlbumId);
                parsedAlbumIds.Add(parsedAlbumId);
                if (!parsedAlbumId.HasValue)
                {
                    needsMigrationPersist = true;
                }

                var track = dto.ToEntity(parsedAlbumId);
                tracks.Add(track);

                if (parsedAlbumId.HasValue)
                {
                    string artistKey = !string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.AlbumArtist : track.Artist;
                    validAlbumIdsByKey.TryAdd(AlbumGroupingResolver.CreateGroupKey(track.Album, artistKey), parsedAlbumId.Value);
                }
            }

            AlbumGroupingResolver.ResolveAndApply(tracks, validAlbumIdsByKey);

            if (!needsMigrationPersist)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (!parsedAlbumIds[i].HasValue || tracks[i].AlbumId != parsedAlbumIds[i]!.Value)
                    {
                        needsMigrationPersist = true;
                        break;
                    }
                }
            }

            if (needsMigrationPersist && tracks.Count > 0)
            {
                try
                {
                    var migratedDtos = tracks.Select(TrackDto.FromEntity).ToList();
                    string migratedJson = JsonSerializer.Serialize(migratedDtos, _jsonOptions);
                    await _fileLockHelper.WriteAllTextAtomicAsync(filePath, migratedJson, cancellationToken).ConfigureAwait(false);
                    currentMetadata = _fileLockHelper.GetFileMetadata(filePath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 読み取り専用環境等で書き戻せない場合でもメモリ上の復元結果は維持する
                }
            }

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

        _lastLoadedFilePath = filePath;
        _lastLoadedMetadata = currentMetadata;
        _isLoaded = true;
    }

    private async Task SaveCandidateTracksCoreAsync(
        string filePath,
        Dictionary<TrackId, Track> candidateTracks,
        CancellationToken cancellationToken)
    {
        var candidateList = candidateTracks.Values.ToList();
        AlbumGroupingResolver.ResolveAndApply(candidateList);
        var dtos = candidateList.Select(TrackDto.FromEntity).ToList();
        string json = JsonSerializer.Serialize(dtos, _jsonOptions);

        await _fileLockHelper.WriteAllTextAtomicAsync(filePath, json, cancellationToken).ConfigureAwait(false);

        // 原子的なファイル置換が成功した後にのみ、呼び出し元の可変インスタンスから独立したスナップショットでキャッシュを更新する
        _tracks.Clear();
        foreach (var dto in dtos)
        {
            var snapshot = dto.ToEntity();
            _tracks[snapshot.Id] = snapshot;
        }

        _lastLoadedFilePath = filePath;
        _lastLoadedMetadata = _fileLockHelper.GetFileMetadata(filePath);
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
            string filePath = _filePathResolver();
            await EnsureLoadedAsync(filePath, cancellationToken).ConfigureAwait(false);
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
            string targetFilePath = _filePathResolver();
            await EnsureLoadedAsync(targetFilePath, cancellationToken).ConfigureAwait(false);
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
            string filePath = _filePathResolver();
            await EnsureLoadedAsync(filePath, cancellationToken).ConfigureAwait(false);
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
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
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
            string filePath = _filePathResolver();
            await EnsureLoadedAsync(filePath, cancellationToken).ConfigureAwait(false);
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
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
            var candidateTracks = new Dictionary<TrackId, Track>(_tracks)
            {
                [track.Id] = track,
            };
            await SaveCandidateTracksCoreAsync(filePath, candidateTracks, cancellationToken).ConfigureAwait(false);
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
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
            var candidateTracks = new Dictionary<TrackId, Track>(_tracks);
            foreach (var track in tracks)
            {
                candidateTracks[track.Id] = track;
            }

            await SaveCandidateTracksCoreAsync(filePath, candidateTracks, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// リポジトリ内の全トラックを指定されたコレクションで完全に置き換えます
    /// </summary>
    /// <param name="tracks">同期後の全トラックコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    public async Task ReplaceAllAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var trackList = tracks.ToList();
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var candidateTracks = new Dictionary<TrackId, Track>(trackList.Count);
            foreach (var track in trackList)
            {
                candidateTracks[track.Id] = track;
            }

            await SaveCandidateTracksCoreAsync(filePath, candidateTracks, cancellationToken).ConfigureAwait(false);
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
            string filePath = _filePathResolver();
            using IDisposable fileLock = await _fileLockHelper
                .AcquireLockAsync(filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await ReloadFromDiskCoreAsync(filePath, forceReload: true, cancellationToken).ConfigureAwait(false);
            if (_tracks.ContainsKey(id))
            {
                var candidateTracks = new Dictionary<TrackId, Track>(_tracks);
                candidateTracks.Remove(id);
                await SaveCandidateTracksCoreAsync(filePath, candidateTracks, cancellationToken).ConfigureAwait(false);
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
        [JsonConverter(typeof(LenientAlbumIdJsonConverter))]
        public string? AlbumId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string? AlbumArtist { get; set; }
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
            AlbumId = track.AlbumId.ToString(),
            FilePath = track.FilePath,
            Title = track.Title,
            Artist = track.Artist,
            AlbumArtist = track.AlbumArtist,
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

        public Track ToEntity(AlbumId? parsedAlbumId = null) => new(
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
            isHiRes: IsHiRes,
            albumId: parsedAlbumId ?? TryParseAlbumId(AlbumId),
            albumArtist: AlbumArtist ?? string.Empty);

        public static AlbumId? TryParseAlbumId(string? value)
        {
            if (Guid.TryParse(value, out var guid) && guid != Guid.Empty)
            {
                return Domain.ValueObjects.AlbumId.From(guid);
            }

            return null;
        }
    }

    private sealed class LenientAlbumIdJsonConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            // 数値・真偽値・オブジェクト・配列などの不正型はその項目のみ読み飛ばして自動補完へフォールバックする
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }
    }
}
