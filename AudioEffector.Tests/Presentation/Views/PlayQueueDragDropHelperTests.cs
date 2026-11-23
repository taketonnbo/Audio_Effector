using System.Windows;
using AudioEffector.Presentation.Views;
using Xunit;

namespace AudioEffector.Tests.Presentation.Views;

/// <summary>
/// <see cref="PlayQueueDragDropHelper"/> のドラッグ開始閾値判定、セクション間ドロップ可否、
/// 挿入位置および移動先インデックス計算を検証するテストクラス。
/// </summary>
public sealed class PlayQueueDragDropHelperTests
{
    /// <summary>
    /// 水平・垂直方向の移動距離とシステム規定閾値の比較により、ドラッグ開始可否が正しく判定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(10, 10, 12, 12, 4, 4, false)]
    [InlineData(10, 10, 14, 10, 4, 4, true)]
    [InlineData(10, 10, 10, 14, 4, 4, true)]
    [InlineData(10, 10, 6, 10, 4, 4, true)]
    [InlineData(10, 10, 10, 6, 4, 4, true)]
    public void HasExceededDragThreshold_移動距離と閾値の比較_期待される判定結果を返す(
        double startX,
        double startY,
        double currentX,
        double currentY,
        double minHorizontal,
        double minVertical,
        bool expected)
    {
        // Arrange
        var startPoint = new Point(startX, startY);
        var currentPoint = new Point(currentX, currentY);

        // Act
        bool actual = PlayQueueDragDropHelper.HasExceededDragThreshold(
            startPoint,
            currentPoint,
            minHorizontal,
            minVertical);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// セクション間および同一セクション内のドロップ可否ルール（UserQueue から AlbumQueue のみ不可）が正しく判定されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(QueueSection.UserQueue, QueueSection.UserQueue, true)]
    [InlineData(QueueSection.AlbumQueue, QueueSection.AlbumQueue, true)]
    [InlineData(QueueSection.AlbumQueue, QueueSection.UserQueue, true)]
    [InlineData(QueueSection.UserQueue, QueueSection.AlbumQueue, false)]
    public void CanDrop_セクションの組み合わせ_仕様通りのドロップ可否を返す(
        QueueSection source,
        QueueSection target,
        bool expected)
    {
        // Arrange & Act
        bool actual = PlayQueueDragDropHelper.CanDrop(source, target);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// アイテム内の相対Y座標（上半分／下半分）に応じて正しい挿入インデックスが返されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 5.0, 40.0, 3, 0)]
    [InlineData(0, 19.9, 40.0, 3, 0)]
    [InlineData(0, 20.0, 40.0, 3, 1)]
    [InlineData(2, 35.0, 40.0, 3, 3)]
    [InlineData(0, 10.0, 40.0, 0, 0)]
    public void ResolveInsertIndex_アイテム内相対座標指定時_上半分は手前下半分は後ろのインデックスを返す(
        int hoveredIndex,
        double relativeY,
        double itemHeight,
        int itemCount,
        int expected)
    {
        // Arrange & Act
        int actual = PlayQueueDragDropHelper.ResolveInsertIndex(hoveredIndex, relativeY, itemHeight, itemCount);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 同一セクション内でのドラッグ＆ドロップ時に、削除によるインデックスずれを補正した移動先インデックスが算出されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0, 3, -1)] // 自身の直前（変化なし）
    [InlineData(0, 1, 3, -1)] // 自身の直後（変化なし）
    [InlineData(0, 2, 3, 1)]  // 先頭を2番目の後ろへ -> index 1
    [InlineData(0, 3, 3, 2)]  // 先頭を末尾へ -> index 2
    [InlineData(2, 0, 3, 0)]  // 末尾を先頭へ -> index 0
    [InlineData(2, 1, 3, 1)]  // 末尾を2番目へ -> index 1
    [InlineData(2, 2, 3, -1)] // 自身の直前（変化なし）
    [InlineData(2, 3, 3, -1)] // 自身の直後（変化なし）
    [InlineData(0, 1, 1, -1)] // 要素1件のみ（移動不可）
    public void CalculateTargetIndex_同一セクション内移動_補正後の移動先インデックスまたは無効値を返す(
        int sourceIndex,
        int rawInsertIndex,
        int itemCount,
        int expected)
    {
        // Arrange & Act
        int actual = PlayQueueDragDropHelper.CalculateTargetIndex(
            QueueSection.UserQueue,
            sourceIndex,
            QueueSection.UserQueue,
            rawInsertIndex,
            itemCount);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// AlbumQueue から UserQueue への昇格移動時、補正なしで挿入位置がクランプされて返されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(0, 0, 2, 0)]
    [InlineData(1, 1, 2, 1)]
    [InlineData(2, 2, 2, 2)]
    [InlineData(0, 5, 2, 2)]
    [InlineData(0, 0, 0, 0)]
    public void CalculateTargetIndex_アルバムから予約キューへ昇格移動時_クランプ済み挿入位置を返す(
        int sourceIndex,
        int rawInsertIndex,
        int targetItemCount,
        int expected)
    {
        // Arrange & Act
        int actual = PlayQueueDragDropHelper.CalculateTargetIndex(
            QueueSection.AlbumQueue,
            sourceIndex,
            QueueSection.UserQueue,
            rawInsertIndex,
            targetItemCount);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// UserQueue から AlbumQueue への逆方向移動は拒否され -1 を返すことを検証します。
    /// </summary>
    [Fact]
    public void CalculateTargetIndex_予約キューからアルバムキューへの逆方向移動時_マイナス1を返す()
    {
        // Arrange & Act
        int actual = PlayQueueDragDropHelper.CalculateTargetIndex(
            QueueSection.UserQueue,
            0,
            QueueSection.AlbumQueue,
            1,
            3);

        // Assert
        Assert.Equal(-1, actual);
    }
}
