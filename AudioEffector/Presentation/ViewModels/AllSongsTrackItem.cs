using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 「すべての曲」一覧における表示行（1始まりの連番と楽曲エンティティの組）を表すレコード
/// </summary>
public record AllSongsTrackItem
{
    /// <summary>
    /// 現在の表示順における1始まりの連番
    /// </summary>
    public int DisplayNumber { get; }

    /// <summary>
    /// 対象楽曲エンティティ
    /// </summary>
    public Track Track { get; }

    /// <summary>
    /// AllSongsTrackItemを初期化します
    /// </summary>
    /// <param name="displayNumber">現在の表示順における1始まりの連番</param>
    /// <param name="track">対象楽曲エンティティ</param>
    public AllSongsTrackItem(int displayNumber, Track track)
    {
        DisplayNumber = displayNumber;
        Track = track;
    }
}
