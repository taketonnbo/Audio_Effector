using System.Diagnostics.CodeAnalysis;
using AudioEffector.Domain.Entities;

namespace AudioEffector.McpServer.Dtos;

/// <summary>
/// <c>search_tracks</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="TotalCount">検索条件に一致した総件数（ページネーション適用前）。</param>
/// <param name="Count">今回返却された楽曲件数。</param>
/// <param name="Offset">適用された取得開始位置。</param>
/// <param name="Limit">適用された最大返却件数。</param>
/// <param name="Tracks">検索に一致した楽曲のメタデータ一覧。</param>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "カテゴリ単位の DTO 集約ファイル規約（MCPサーバ詳細設計書 5.1 準拠）")]
public sealed record SearchTracksResponse(
    int TotalCount,
    int Count,
    int Offset,
    int Limit,
    IReadOnlyList<TrackSummaryDto> Tracks);

/// <summary>
/// MCP 応答で返却する楽曲メタデータのサマリ DTO です。
/// </summary>
/// <param name="Id">一意のトラック ID（GUID）。</param>
/// <param name="Title">楽曲タイトル。</param>
/// <param name="Artist">アーティスト名。</param>
/// <param name="Album">アルバム名。</param>
/// <param name="DurationSeconds">再生時間（秒）。</param>
/// <param name="DurationString">フォーマット済み再生時間文字列（例: <c>03:45</c>）。</param>
/// <param name="TrackNumber">トラック番号。</param>
/// <param name="Year">リリース年。</param>
/// <param name="Genre">ジャンル名。</param>
/// <param name="Format">音声フォーマット名（例: <c>FLAC</c>, <c>MP3</c>）。</param>
/// <param name="Bitrate">ビットレート（kbps）。</param>
/// <param name="SampleRate">サンプリング周波数（Hz）。</param>
/// <param name="BitsPerSample">量子化ビット数（bit）。</param>
/// <param name="IsFavorite">お気に入り登録済みかどうか。</param>
/// <param name="FilePath">音声ファイルの絶対パス。</param>
public sealed record TrackSummaryDto(
    Guid Id,
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    string DurationString,
    uint TrackNumber,
    uint Year,
    string Genre,
    string Format,
    int Bitrate,
    int SampleRate,
    int BitsPerSample,
    bool IsFavorite,
    string FilePath)
{
    /// <summary>
    /// <see cref="Track"/> ドメインエンティティから <see cref="TrackSummaryDto"/> を生成します。
    /// </summary>
    /// <param name="track">変換元のトラックエンティティ。</param>
    /// <returns>生成された DTO。</returns>
    public static TrackSummaryDto FromEntity(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new TrackSummaryDto(
            Id: track.Id.Value,
            Title: track.Title,
            Artist: track.Artist,
            Album: track.Album,
            DurationSeconds: Math.Round(track.Duration.TotalSeconds, 3),
            DurationString: track.DurationString,
            TrackNumber: track.TrackNumber,
            Year: track.Year,
            Genre: track.Genre,
            Format: track.Format,
            Bitrate: track.Bitrate,
            SampleRate: track.SampleRate,
            BitsPerSample: track.BitsPerSample,
            IsFavorite: track.IsFavorite,
            FilePath: track.FilePath);
    }
}
