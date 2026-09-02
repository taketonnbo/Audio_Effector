namespace AudioEffector.Domain.Services;

/// <summary>
/// 通常の順次再生（先頭から末尾へ順番に再生し、末尾で停止する）戦略
/// </summary>
public class SequentialPlaybackStrategy : IPlaybackOrderStrategy
{
    /// <summary>
    /// 次の楽曲インデックスを取得します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">楽曲総数</param>
    /// <returns>次のインデックス（末尾の場合はnull）</returns>
    public int? GetNextIndex(int currentIndex, int totalTracks)
    {
        if (totalTracks <= 0) return null;
        if (currentIndex < 0) return 0;

        int nextIndex = currentIndex + 1;
        return nextIndex < totalTracks ? nextIndex : null;
    }

    /// <summary>
    /// 前の楽曲インデックスを取得します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">楽曲総数</param>
    /// <returns>前のインデックス（先頭の場合はnull）</returns>
    public int? GetPreviousIndex(int currentIndex, int totalTracks)
    {
        if (totalTracks <= 0) return null;
        if (currentIndex <= 0) return null;

        return currentIndex - 1;
    }
}
