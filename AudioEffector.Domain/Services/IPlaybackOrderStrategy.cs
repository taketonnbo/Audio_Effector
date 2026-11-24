namespace AudioEffector.Domain.Services;

/// <summary>
/// 楽曲の再生順序（通常順・シャッフル・リピート）を決定する戦略インターフェース
/// </summary>
public interface IPlaybackOrderStrategy
{
    /// <summary>
    /// 次に再生すべき楽曲のインデックスを計算します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス（-1は未選択）</param>
    /// <param name="totalTracks">プレイリスト全体の楽曲数</param>
    /// <returns>次の楽曲インデックス（再生終了時はnull）</returns>
    int? GetNextIndex(int currentIndex, int totalTracks);

    /// <summary>
    /// 前に再生すべき楽曲のインデックスを計算します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">プレイリスト全体の楽曲数</param>
    /// <returns>前の楽曲インデックス（先頭以前がない場合はnull）</returns>
    int? GetPreviousIndex(int currentIndex, int totalTracks);
}
