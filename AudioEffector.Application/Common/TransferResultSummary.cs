using System;
using System.Collections.Generic;

namespace AudioEffector.Application.Common;

/// <summary>
/// 機器転送または逆転送の実行結果サマリを表します
/// </summary>
public sealed class TransferResultSummary
{
    /// <summary>
    /// 正常に完了した曲数を取得します
    /// </summary>
    public int SuccessCount { get; }

    /// <summary>
    /// 重複済みのためスキップした曲数を取得します
    /// </summary>
    public int SkippedCount { get; }

    /// <summary>
    /// 個別エラーにより失敗した楽曲の一覧を取得します
    /// </summary>
    public IReadOnlyList<TransferFailedItem> FailedItems { get; }

    /// <summary>
    /// 失敗した曲数を取得します
    /// </summary>
    public int FailedCount => FailedItems.Count;

    /// <summary>
    /// ユーザー操作により途中でキャンセルされたかどうかを取得します
    /// </summary>
    public bool WasCancelled { get; }

    /// <summary>
    /// デバイス切断などの回復不能エラーにより途中で中断されたかどうかを取得します
    /// </summary>
    public bool WasAbortedByFatalError { get; }

    /// <summary>
    /// 回復不能エラーによる中断時のエラーメッセージを取得します
    /// </summary>
    public string? FatalErrorMessage { get; }

    /// <summary>
    /// 対象操作の表示ラベル（例: 転送 / 取り込み）を取得します
    /// </summary>
    public string OperationLabel { get; }

    /// <summary>
    /// 失敗曲が 1 曲以上存在するかどうかを取得します
    /// </summary>
    public bool HasFailedItems => FailedItems.Count > 0;

    /// <summary>
    /// 回復不能エラーメッセージが存在するかどうかを取得します
    /// </summary>
    public bool HasFatalError => WasAbortedByFatalError && !string.IsNullOrWhiteSpace(FatalErrorMessage);

    /// <summary>
    /// ダイアログのウィンドウタイトルを取得します
    /// </summary>
    public string DialogTitle
    {
        get
        {
            if (WasCancelled)
            {
                return $"{OperationLabel}を中止しました";
            }

            if (WasAbortedByFatalError)
            {
                return $"{OperationLabel}が中断されました";
            }

            return HasFailedItems ? $"{OperationLabel}完了（一部エラー）" : $"{OperationLabel}結果サマリ";
        }
    }

    /// <summary>
    /// ダイアログの見出しテキストを取得します
    /// </summary>
    public string HeaderText
    {
        get
        {
            if (WasCancelled)
            {
                return $"{OperationLabel}を中止しました";
            }

            if (WasAbortedByFatalError)
            {
                return $"⚠ デバイス切断等のエラーにより{OperationLabel}を中断しました";
            }

            return HasFailedItems
                ? $"⚠ {OperationLabel}が完了しました（{FailedCount} 曲でエラー）"
                : $"{OperationLabel}が完了しました";
        }
    }

    /// <summary>
    /// 結果件数をまとめた説明テキストを取得します
    /// </summary>
    public string StatusDescription =>
        $"成功: {SuccessCount} 曲 / スキップ（重複済み）: {SkippedCount} 曲 / 失敗: {FailedCount} 曲";

    /// <summary>
    /// 成功件数の表示テキストを取得します
    /// </summary>
    public string SuccessCountText => $"{SuccessCount} 曲";

    /// <summary>
    /// スキップ件数の表示テキストを取得します
    /// </summary>
    public string SkippedCountText => $"{SkippedCount} 曲";

    /// <summary>
    /// 失敗件数の表示テキストを取得します
    /// </summary>
    public string FailedCountText => $"{FailedCount} 曲";

    /// <summary>
    /// <see cref="TransferResultSummary"/> のインスタンスを初期化します
    /// </summary>
    /// <param name="successCount">成功曲数</param>
    /// <param name="skippedCount">スキップ曲数</param>
    /// <param name="failedItems">失敗曲情報一覧</param>
    /// <param name="wasCancelled">途中キャンセルされた場合は true</param>
    /// <param name="wasAbortedByFatalError">回復不能エラーで中断された場合は true</param>
    /// <param name="fatalErrorMessage">回復不能エラー時のメッセージ</param>
    /// <param name="operationLabel">操作ラベル（既定: "転送"）</param>
    public TransferResultSummary(
        int successCount,
        int skippedCount,
        IReadOnlyList<TransferFailedItem>? failedItems = null,
        bool wasCancelled = false,
        bool wasAbortedByFatalError = false,
        string? fatalErrorMessage = null,
        string operationLabel = "転送")
    {
        SuccessCount = Math.Max(0, successCount);
        SkippedCount = Math.Max(0, skippedCount);
        FailedItems = failedItems != null ? new List<TransferFailedItem>(failedItems) : Array.Empty<TransferFailedItem>();
        WasCancelled = wasCancelled;
        WasAbortedByFatalError = wasAbortedByFatalError;
        FatalErrorMessage = fatalErrorMessage;
        OperationLabel = string.IsNullOrWhiteSpace(operationLabel) ? "転送" : operationLabel.Trim();
    }
}
