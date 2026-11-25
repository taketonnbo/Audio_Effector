using System.Diagnostics.CodeAnalysis;
using AudioEffector.Infrastructure.Ipc;

namespace AudioEffector.McpServer.Dtos;

/// <summary>
/// <c>play_track</c> ツールの応答データ DTO です。
/// </summary>
/// <param name="PlaybackState">現在の再生状態（例: <c>playing</c>）。</param>
/// <param name="Track">再生を開始した楽曲情報。</param>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "カテゴリ単位の DTO 集約ファイル規約（MCPサーバ詳細設計書 5.1 準拠）")]
public sealed record PlayTrackResponse(
    string PlaybackState,
    PlayedTrackDto Track);

/// <summary>
/// 再生制御応答で返却する楽曲情報 DTO です。
/// </summary>
/// <param name="Id">一意のトラック ID（GUID）。</param>
/// <param name="Title">楽曲タイトル。</param>
/// <param name="Artist">アーティスト名。</param>
/// <param name="Album">アルバム名。</param>
/// <param name="DurationSeconds">再生時間（秒）。</param>
/// <param name="DurationString">フォーマット済み再生時間文字列（例: <c>03:45</c>）。</param>
/// <param name="FilePath">音声ファイルの絶対パス。</param>
public sealed record PlayedTrackDto(
    Guid Id,
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    string DurationString,
    string FilePath)
{
    /// <summary>
    /// IPC 応答の <see cref="AppIpcTrackInfo"/> から <see cref="PlayedTrackDto"/> を生成します。
    /// </summary>
    /// <param name="track">IPC 応答トラック情報。</param>
    /// <returns>生成された DTO。</returns>
    public static PlayedTrackDto FromIpcTrackInfo(AppIpcTrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new PlayedTrackDto(
            Id: track.Id,
            Title: track.Title,
            Artist: track.Artist,
            Album: track.Album,
            DurationSeconds: track.DurationSeconds,
            DurationString: track.DurationString,
            FilePath: track.FilePath);
    }
}
