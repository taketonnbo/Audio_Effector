using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// 転送処理中のリアルタイム詳細進捗情報を表します
/// </summary>
public sealed class TransferProgressInfo
{
    /// <summary>
    /// 現在処理中または完了済みの曲数（1 始まりのインデックス、完了時は総数）を取得します
    /// </summary>
    public int CurrentCount { get; }

    /// <summary>
    /// 転送対象の総曲数を取得します
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// 全体の進捗率（0〜100）を取得します
    /// </summary>
    public double Percent { get; }

    /// <summary>
    /// 現在処理中の楽曲ファイル名またはタイトルを取得します
    /// </summary>
    public string CurrentFileName { get; }

    /// <summary>
    /// 現在の処理フェーズ説明テキスト（例: 変換中 / コピー中 / スキップ中）を取得します
    /// </summary>
    public string PhaseText { get; }

    /// <summary>
    /// <see cref="TransferProgressInfo"/> のインスタンスを初期化します
    /// </summary>
    /// <param name="currentCount">現在処理中の曲数</param>
    /// <param name="totalCount">総曲数</param>
    /// <param name="percent">進捗率（0〜100）</param>
    /// <param name="currentFileName">現在処理中のファイル名</param>
    /// <param name="phaseText">現在フェーズの説明テキスト</param>
    public TransferProgressInfo(
        int currentCount,
        int totalCount,
        double percent,
        string currentFileName,
        string phaseText)
    {
        CurrentCount = Math.Max(0, currentCount);
        TotalCount = Math.Max(0, totalCount);
        Percent = Math.Clamp(percent, 0.0, 100.0);
        CurrentFileName = currentFileName ?? string.Empty;
        PhaseText = phaseText ?? string.Empty;
    }
}
