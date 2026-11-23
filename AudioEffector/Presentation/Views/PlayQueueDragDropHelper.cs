using System;
using System.Collections;
using System.Windows;
using AudioEffector.Domain.Entities;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 再生キューのドラッグ＆ドロップ判定およびインデックス計算を行う純粋ヘルパークラス
/// </summary>
public static class PlayQueueDragDropHelper
{
    /// <summary>
    /// WPF DataObject に格納する際のデータフォーマット識別子
    /// </summary>
    public const string DragDataFormat = "AudioEffector.PlayQueueDragPayload";

    /// <summary>
    /// マウス移動量がシステム規定のドラッグ開始閾値を超えたかを判定します
    /// </summary>
    /// <param name="startPoint">マウス押下時の開始座標</param>
    /// <param name="currentPoint">現在のマウス座標</param>
    /// <param name="minHorizontalDistance">水平方向の最小ドラッグ距離</param>
    /// <param name="minVerticalDistance">垂直方向の最小ドラッグ距離</param>
    /// <returns>閾値以上の移動があった場合は true</returns>
    public static bool HasExceededDragThreshold(
        Point startPoint,
        Point currentPoint,
        double minHorizontalDistance,
        double minVerticalDistance)
    {
        return Math.Abs(currentPoint.X - startPoint.X) >= minHorizontalDistance
            || Math.Abs(currentPoint.Y - startPoint.Y) >= minVerticalDistance;
    }

    /// <summary>
    /// 指定されたドラッグ元セクションからドロップ先セクションへの移動が許可されるかを判定します
    /// </summary>
    /// <param name="sourceSection">ドラッグ元のセクション</param>
    /// <param name="targetSection">ドロップ先のセクション</param>
    /// <returns>ドロップ可能な場合は true（UserQueue から AlbumQueue への逆方向移動は false）</returns>
    public static bool CanDrop(QueueSection sourceSection, QueueSection targetSection)
    {
        if (sourceSection == targetSection)
        {
            return true;
        }

        // AlbumQueue -> UserQueue は予約キューへの昇格として許可
        return sourceSection == QueueSection.AlbumQueue && targetSection == QueueSection.UserQueue;
    }

    /// <summary>
    /// アイテム要素内のマウス相対Y座標から、挿入インデックス（0〜itemCount）を決定します
    /// </summary>
    /// <param name="hoveredItemIndex">マウス直下にあるアイテムのインデックス</param>
    /// <param name="relativeY">アイテム上端からの相対Y座標</param>
    /// <param name="itemHeight">アイテムの高さ</param>
    /// <param name="itemCount">対象セクションの総アイテム数</param>
    /// <returns>クランプ済みの挿入インデックス</returns>
    public static int ResolveInsertIndex(int hoveredItemIndex, double relativeY, double itemHeight, int itemCount)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        int rawIndex = itemHeight > 0 && relativeY >= itemHeight / 2.0
            ? hoveredItemIndex + 1
            : hoveredItemIndex;

        return Math.Clamp(rawIndex, 0, itemCount);
    }

    /// <summary>
    /// 挿入位置（0〜targetItemCount）から、実際の移動先インデックスを計算します
    /// 移動が不要または不許可の場合は -1 を返します
    /// </summary>
    /// <param name="sourceSection">ドラッグ元のセクション</param>
    /// <param name="sourceIndex">ドラッグ元のインデックス</param>
    /// <param name="targetSection">ドロップ先のセクション</param>
    /// <param name="rawInsertIndex">アイテム境界としての挿入インデックス（0〜targetItemCount）</param>
    /// <param name="targetItemCount">ドロップ先セクションの現在のアイテム数</param>
    /// <returns>適用すべき移動先インデックス。移動不要または不許可時は -1</returns>
    public static int CalculateTargetIndex(
        QueueSection sourceSection,
        int sourceIndex,
        QueueSection targetSection,
        int rawInsertIndex,
        int targetItemCount)
    {
        if (!CanDrop(sourceSection, targetSection) || sourceIndex < 0)
        {
            return -1;
        }

        if (sourceSection == targetSection)
        {
            if (targetItemCount <= 1 || sourceIndex >= targetItemCount)
            {
                return -1;
            }

            int clampedInsert = Math.Clamp(rawInsertIndex, 0, targetItemCount);
            int targetIndex = sourceIndex < clampedInsert ? clampedInsert - 1 : clampedInsert;
            targetIndex = Math.Clamp(targetIndex, 0, targetItemCount - 1);

            return targetIndex == sourceIndex ? -1 : targetIndex;
        }

        // セクション間移動（AlbumQueue -> UserQueue）は元リストと別コレクションのため補正不要
        return Math.Clamp(rawInsertIndex, 0, Math.Max(0, targetItemCount));
    }

    /// <summary>
    /// ドロップ時点のコレクション状態から、ドラッグ元トラックの最新インデックスを再解決します
    /// ドラッグ中の非同期キュー変化により元の位置がずれた場合は、最も近い一致要素のインデックスを返します
    /// </summary>
    /// <param name="currentItems">ドロップ時点の移動元コレクション</param>
    /// <param name="originalIndex">ドラッグ開始時のインデックス</param>
    /// <param name="draggedTrack">ドラッグ対象のトラック</param>
    /// <returns>最新のインデックス。対象トラックがキューから消失している場合は -1</returns>
    public static int ResolveCurrentSourceIndex(IList currentItems, int originalIndex, Track draggedTrack)
    {
        if (currentItems == null || draggedTrack == null || currentItems.Count == 0)
        {
            return -1;
        }

        if (originalIndex >= 0 && originalIndex < currentItems.Count && ReferenceEquals(currentItems[originalIndex], draggedTrack))
        {
            return originalIndex;
        }

        int bestIndex = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < currentItems.Count; i++)
        {
            if (ReferenceEquals(currentItems[i], draggedTrack))
            {
                int distance = Math.Abs(i - originalIndex);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
        }

        return bestIndex;
    }
}

/// <summary>
/// 再生キューパネルにおけるセクション種別
/// </summary>
public enum QueueSection
{
    /// <summary>
    /// ユーザーが手動で追加した予約キュー（UserQueue）
    /// </summary>
    UserQueue,

    /// <summary>
    /// アルバムの残り曲キュー（AlbumQueue）
    /// </summary>
    AlbumQueue
}

/// <summary>
/// 再生キューのドラッグ＆ドロップ操作で受け渡すペイロード
/// </summary>
/// <param name="SourceSection">ドラッグ元のセクション</param>
/// <param name="SourceIndex">ドラッグ元のインデックス（0始まり）</param>
/// <param name="Track">ドラッグ対象のトラック</param>
public sealed record QueueDragPayload(QueueSection SourceSection, int SourceIndex, Track Track);
