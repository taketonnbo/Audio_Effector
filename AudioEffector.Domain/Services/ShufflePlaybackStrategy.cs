using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioEffector.Domain.Services;

/// <summary>
/// ランダムシャッフル再生戦略（Fisher-Yatesシャッフルによる全曲一巡と履歴管理）
/// </summary>
public class ShufflePlaybackStrategy : IPlaybackOrderStrategy
{
    private readonly Random _random;
    private List<int> _shuffledIndices = [];
    private int _historyPosition = -1;

    /// <summary>
    /// 指定されたRandomシードでシャッフル戦略を初期化します
    /// </summary>
    /// <param name="random">乱数生成器（未指定時は新規生成）</param>
    public ShufflePlaybackStrategy(Random? random = null)
    {
        _random = random ?? new Random();
    }

    /// <summary>
    /// シャッフル順序テーブルを再構築します
    /// </summary>
    /// <param name="totalTracks">楽曲総数</param>
    /// <param name="currentTrackIndex">現在再生中の楽曲インデックス（先頭に配置）</param>
    public void Reshuffle(int totalTracks, int currentTrackIndex = -1)
    {
        if (totalTracks <= 0)
        {
            _shuffledIndices.Clear();
            _historyPosition = -1;
            return;
        }

        var indices = Enumerable.Range(0, totalTracks).ToList();

        // Fisher-Yates シャッフル
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        // 現在曲が指定されている場合は先頭に配置
        if (currentTrackIndex >= 0 && currentTrackIndex < totalTracks)
        {
            indices.Remove(currentTrackIndex);
            indices.Insert(0, currentTrackIndex);
            _historyPosition = 0;
        }
        else
        {
            _historyPosition = -1;
        }

        _shuffledIndices = indices;
    }

    /// <summary>
    /// シャッフル順序における次の楽曲インデックスを取得します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">楽曲総数</param>
    /// <returns>次のインデックス（一周終了時は再シャッフルして次へ、楽曲0曲時はnull）</returns>
    public int? GetNextIndex(int currentIndex, int totalTracks)
    {
        if (totalTracks <= 0) return null;

        // シャッフルリストの要素数が合わない場合は再構築
        if (_shuffledIndices.Count != totalTracks)
        {
            Reshuffle(totalTracks, currentIndex);
        }

        _historyPosition++;
        if (_historyPosition >= _shuffledIndices.Count)
        {
            // 一巡したら再度シャッフルして先頭へ
            Reshuffle(totalTracks, -1);
            _historyPosition = 0;
        }

        return _shuffledIndices.Count > 0 ? _shuffledIndices[_historyPosition] : null;
    }

    /// <summary>
    /// シャッフル順序における前の楽曲インデックス（履歴）を取得します
    /// </summary>
    /// <param name="currentIndex">現在の楽曲インデックス</param>
    /// <param name="totalTracks">楽曲総数</param>
    /// <returns>前のインデックス（履歴の先頭以前はnull）</returns>
    public int? GetPreviousIndex(int currentIndex, int totalTracks)
    {
        if (totalTracks <= 0 || _shuffledIndices.Count == 0) return null;

        if (_historyPosition > 0)
        {
            _historyPosition--;
            return _shuffledIndices[_historyPosition];
        }

        return null;
    }
}
