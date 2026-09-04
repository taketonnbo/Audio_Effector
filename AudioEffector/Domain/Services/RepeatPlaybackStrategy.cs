namespace AudioEffector.Domain.Services;

/// <summary>
/// リピート再生モード（単曲リピートまたは全曲ループ）
/// </summary>
public enum RepeatMode
{
    /// <summary>
    /// 全曲ループ再生
    /// </summary>
    All = 0,

    /// <summary>
    /// 単曲ループ再生
    /// </summary>
    One = 1
}

/// <summary>
/// リピート再生（単曲リピートまたは全曲リピート）戦略
/// </summary>
public class RepeatPlaybackStrategy : IPlaybackOrderStrategy
{
    /// <summary>
    /// リピートモード（全曲または単曲）
    /// </summary>
    public RepeatMode Mode { get; set; }

    /// <summary>
    /// 指定されたモードでリピート戦略を初期化します
    /// </summary>
    /// <param name="mode">リピートモード（デフォルト: All）</param>
    public RepeatPlaybackStrategy(RepeatMode mode = RepeatMode.All)
    {
        Mode = mode;
    }

    /// <summary>
    /// 次の楽曲インデックスを取得します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">楽曲総数</param>
    /// <returns>次のインデックス（全曲ループ時は末尾の次は0、単曲時は現在曲）</returns>
    public int? GetNextIndex(int currentIndex, int totalTracks)
    {
        if (totalTracks <= 0) return null;

        if (Mode == RepeatMode.One)
        {
            return currentIndex >= 0 && currentIndex < totalTracks ? currentIndex : 0;
        }

        // RepeatAll: 末尾まで行ったら先頭（0）に戻る
        if (currentIndex < 0) return 0;
        return (currentIndex + 1) % totalTracks;
    }

    /// <summary>
    /// 前の楽曲インデックスを取得します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">楽曲総数</param>
    /// <returns>前のインデックス（全曲ループ時は先頭の前は末尾、単曲時は現在曲）</returns>
    public int? GetPreviousIndex(int currentIndex, int totalTracks)
    {
        if (totalTracks <= 0) return null;

        if (Mode == RepeatMode.One)
        {
            return currentIndex >= 0 && currentIndex < totalTracks ? currentIndex : 0;
        }

        // RepeatAll: 先頭から前へ行ったら末尾に戻る
        if (currentIndex <= 0) return totalTracks - 1;
        return currentIndex - 1;
    }
}
