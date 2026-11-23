using System.Collections.Generic;
using System.Windows;
using AudioEffector.Domain.Entities;
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

    /// <summary>
    /// ドロップ時点で元インデックスの要素が一致している場合、元のインデックスがそのまま返されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveCurrentSourceIndex_位置変動なしの場合_元のインデックスを返す()
    {
        // Arrange
        var track1 = new Track { FilePath = @"C:\Music\1.mp3", Title = "1" };
        var track2 = new Track { FilePath = @"C:\Music\2.mp3", Title = "2" };
        var items = new List<Track> { track1, track2 };

        // Act
        int actual = PlayQueueDragDropHelper.ResolveCurrentSourceIndex(items, 1, track2);

        // Assert
        Assert.Equal(1, actual);
    }

    /// <summary>
    /// ドラッグ中に先頭曲が自動消費されてインデックスが前へずれた場合、最新位置へ正しく再解決されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveCurrentSourceIndex_ドラッグ中に先頭消費された場合_最新のインデックスを再解決する()
    {
        // Arrange
        var track2 = new Track { FilePath = @"C:\Music\2.mp3", Title = "2" };
        var track3 = new Track { FilePath = @"C:\Music\3.mp3", Title = "3" };
        // 元は [track1, track2, track3] で index=2 (track3) を掴んでいたが、track1 が消費された想定
        var currentItems = new List<Track> { track2, track3 };

        // Act
        int actual = PlayQueueDragDropHelper.ResolveCurrentSourceIndex(currentItems, 2, track3);

        // Assert
        Assert.Equal(1, actual);
    }

    /// <summary>
    /// 同一 Track インスタンスが重複して存在する場合、元インデックスに最も近い一致位置が選ばれることを検証します。
    /// </summary>
    [Fact]
    public void ResolveCurrentSourceIndex_同一インスタンス重複時に位置がずれた場合_元位置に最も近いインデックスを返す()
    {
        // Arrange
        var duplicated = new Track { FilePath = @"C:\Music\dup.mp3", Title = "Dup" };
        var other = new Track { FilePath = @"C:\Music\other.mp3", Title = "Other" };
        // 元は index=3 を掴んでいたが先頭が消費されて [dup(0), other(1), dup(2)] になった想定
        var currentItems = new List<Track> { duplicated, other, duplicated };

        // Act
        int actual = PlayQueueDragDropHelper.ResolveCurrentSourceIndex(currentItems, 3, duplicated);

        // Assert
        Assert.Equal(2, actual);
    }

    /// <summary>
    /// ドラッグ対象のトラックがドロップ時点でキューから既に削除されている場合、-1 を返すことを検証します。
    /// </summary>
    [Fact]
    public void ResolveCurrentSourceIndex_対象トラックが消失している場合_マイナス1を返す()
    {
        // Arrange
        var track1 = new Track { FilePath = @"C:\Music\1.mp3", Title = "1" };
        var removedTrack = new Track { FilePath = @"C:\Music\removed.mp3", Title = "Removed" };
        var currentItems = new List<Track> { track1 };

        // Act
        int actual = PlayQueueDragDropHelper.ResolveCurrentSourceIndex(currentItems, 1, removedTrack);

        // Assert
        Assert.Equal(-1, actual);
    }
}
