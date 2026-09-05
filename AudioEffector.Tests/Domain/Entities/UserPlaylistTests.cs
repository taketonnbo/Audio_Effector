using System;
using System.Collections.Generic;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.Entities;

public class UserPlaylistTests
{
    /// <summary>
    /// デフォルトコンストラクタで生成した場合、一意のIDと初期名"New Playlist"、空のトラックリストで生成されるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_デフォルト初期化_NewPlaylistとして一意のID付きで生成されること()
    {
        // Arrange & Act
        var sut = new UserPlaylist();

        // Assert
        Assert.NotEqual(Guid.Empty, sut.Id.Value);
        Assert.Equal("New Playlist", sut.Name);
        Assert.Empty(sut.TrackIds);
        Assert.Equal(0, sut.TrackCount);
    }

    /// <summary>
    /// 名前とトラックIDリストを指定して初期化した場合、指定した値が正しくプロパティに設定されるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_名前と初期トラックID指定_正しく初期化されること()
    {
        // Arrange
        var id = PlaylistId.New();
        var trackId1 = TrackId.New();
        var trackId2 = TrackId.New();

        // Act
        var sut = new UserPlaylist(id, "My Favorites", [trackId1, trackId2]);

        // Assert
        Assert.Equal(id, sut.Id);
        Assert.Equal("My Favorites", sut.Name);
        Assert.Equal(2, sut.TrackCount);
        Assert.Equal(trackId1, sut.TrackIds[0]);
        Assert.Equal(trackId2, sut.TrackIds[1]);
    }

    /// <summary>
    /// 有効な新しい名前を指定してRenameを呼び出した場合、名前とUpdatedAtが更新されるかを検証します。
    /// </summary>
    [Fact]
    public void Rename_有効な名前_名前とUpdatedAtが更新されること()
    {
        // Arrange
        var sut = new UserPlaylist(PlaylistId.New(), "Old Name");
        var initialUpdated = sut.UpdatedAt;

        // Act
        sut.Rename("Updated Name");

        // Assert
        Assert.Equal("Updated Name", sut.Name);
        Assert.True(sut.UpdatedAt >= initialUpdated);
    }

    /// <summary>
    /// 空文字または空白文字列を指定してRenameを呼び出した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_空文字または空白_ArgumentExceptionをスローすること(string invalidName)
    {
        // Arrange
        var sut = new UserPlaylist();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sut.Rename(invalidName));
    }

    /// <summary>
    /// AddTrackを呼び出した場合、トラックがリストの末尾に追加され、TrackCountが増加するかを検証します。
    /// </summary>
    [Fact]
    public void AddTrack_トラック追加_末尾に追加されTrackCountとUpdatedAtが更新されること()
    {
        // Arrange
        var sut = new UserPlaylist();
        var trackId1 = TrackId.New();
        var trackId2 = TrackId.New();

        // Act
        sut.AddTrack(trackId1);
        sut.AddTrack(trackId2);

        // Assert
        Assert.Equal(2, sut.TrackCount);
        Assert.Equal(trackId1, sut.TrackIds[0]);
        Assert.Equal(trackId2, sut.TrackIds[1]);
    }

    /// <summary>
    /// InsertTrackを呼び出した場合、指定したインデックス位置にトラックが正しく挿入されるかを検証します。
    /// </summary>
    [Fact]
    public void InsertTrack_指定位置への挿入_指定インデックスに挿入されること()
    {
        // Arrange
        var track1 = TrackId.New();
        var track2 = TrackId.New();
        var trackInsert = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track1, track2]);

        // Act (インデックス1の位置に挿入)
        sut.InsertTrack(1, trackInsert);

        // Assert
        Assert.Equal(3, sut.TrackCount);
        Assert.Equal(track1, sut.TrackIds[0]);
        Assert.Equal(trackInsert, sut.TrackIds[1]);
        Assert.Equal(track2, sut.TrackIds[2]);
    }

    /// <summary>
    /// InsertTrackで範囲外のインデックスを指定した場合、安全に0または末尾にクランプされて挿入されるかを検証します。
    /// </summary>
    [Fact]
    public void InsertTrack_範囲外インデックス_先頭または末尾にクランプされて挿入されること()
    {
        // Arrange
        var track1 = TrackId.New();
        var trackPre = TrackId.New();
        var trackPost = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track1]);

        // Act
        sut.InsertTrack(-5, trackPre); // 先頭0にクランプ
        sut.InsertTrack(100, trackPost); // 末尾にクランプ

        // Assert
        Assert.Equal(3, sut.TrackCount);
        Assert.Equal(trackPre, sut.TrackIds[0]);
        Assert.Equal(track1, sut.TrackIds[1]);
        Assert.Equal(trackPost, sut.TrackIds[2]);
    }

    /// <summary>
    /// 存在するトラックIDを指定してRemoveTrackを呼び出した場合、リストから削除されtrueが返されるかを検証します。
    /// </summary>
    [Fact]
    public void RemoveTrack_存在するトラックID_削除されtrueを返すこと()
    {
        // Arrange
        var track1 = TrackId.New();
        var track2 = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track1, track2]);

        // Act
        var removed = sut.RemoveTrack(track1);

        // Assert
        Assert.True(removed);
        Assert.Equal(1, sut.TrackCount);
        Assert.Equal(track2, sut.TrackIds[0]);
    }

    /// <summary>
    /// 存在しないトラックIDを指定してRemoveTrackを呼び出した場合、リストは変化せずfalseが返されるかを検証します。
    /// </summary>
    [Fact]
    public void RemoveTrack_存在しないトラックID_削除されずfalseを返すこと()
    {
        // Arrange
        var track1 = TrackId.New();
        var notInList = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track1]);

        // Act
        var removed = sut.RemoveTrack(notInList);

        // Assert
        Assert.False(removed);
        Assert.Equal(1, sut.TrackCount);
    }

    /// <summary>
    /// 有効なインデックスを指定してRemoveAtを呼び出した場合、指定位置のトラックが削除されtrueが返されるかを検証します。
    /// </summary>
    [Fact]
    public void RemoveAt_有効なインデックス_指定位置のトラックが削除されtrueを返すこと()
    {
        // Arrange
        var track1 = TrackId.New();
        var track2 = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track1, track2]);

        // Act
        var removed = sut.RemoveAt(0);

        // Assert
        Assert.True(removed);
        Assert.Equal(1, sut.TrackCount);
        Assert.Equal(track2, sut.TrackIds[0]);
    }

    /// <summary>
    /// 範囲外のインデックスを指定してRemoveAtを呼び出した場合、falseが返されリストが変化しないかを検証します。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void RemoveAt_範囲外インデックス_削除されずfalseを返すこと(int invalidIndex)
    {
        // Arrange
        var track1 = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track1]);

        // Act
        var removed = sut.RemoveAt(invalidIndex);

        // Assert
        Assert.False(removed);
        Assert.Equal(1, sut.TrackCount);
    }

    /// <summary>
    /// Reorderを呼び出した場合、トラックの並び順が正しく入れ替わるかを検証します。
    /// </summary>
    [Fact]
    public void Reorder_正常な移動_古い位置から新しい位置へ移動し順序が更新されること()
    {
        // Arrange
        var track0 = TrackId.New();
        var track1 = TrackId.New();
        var track2 = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track0, track1, track2]);

        // Act (インデックス0の曲をインデックス2へ移動)
        sut.Reorder(0, 2);

        // Assert
        Assert.Equal(3, sut.TrackCount);
        Assert.Equal(track1, sut.TrackIds[0]);
        Assert.Equal(track2, sut.TrackIds[1]);
        Assert.Equal(track0, sut.TrackIds[2]);
    }

    /// <summary>
    /// Reorderで同一インデックスや範囲外インデックスを指定した場合、順序が変更されないかを検証します。
    /// </summary>
    [Fact]
    public void Reorder_同一インデックスまたは範囲外_順序は変化しないこと()
    {
        // Arrange
        var track0 = TrackId.New();
        var track1 = TrackId.New();
        var sut = new UserPlaylist(PlaylistId.New(), "List", [track0, track1]);

        // Act
        sut.Reorder(0, 0); // 同一
        sut.Reorder(-1, 1); // 範囲外
        sut.Reorder(0, 10); // 範囲外

        // Assert
        Assert.Equal(track0, sut.TrackIds[0]);
        Assert.Equal(track1, sut.TrackIds[1]);
    }

    /// <summary>
    /// Clearを呼び出した場合、すべてのトラックが削除されてTrackCountが0になるかを検証します。
    /// </summary>
    [Fact]
    public void Clear_トラックが存在する場合_全トラックが削除されTrackCountが0になること()
    {
        // Arrange
        var sut = new UserPlaylist(PlaylistId.New(), "List", [TrackId.New(), TrackId.New()]);

        // Act
        sut.Clear();

        // Assert
        Assert.Empty(sut.TrackIds);
        Assert.Equal(0, sut.TrackCount);
    }

    /// <summary>
    /// 同一のPlaylistIdを持つUserPlaylist同士が等価と判定されるかを検証します。
    /// </summary>
    [Fact]
    public void Equals_同一IdのUserPlaylist_等価と判定されること()
    {
        // Arrange
        var id = PlaylistId.New();
        var sut1 = new UserPlaylist(id, "List 1");
        var sut2 = new UserPlaylist(id, "List 1 Modified");

        // Act & Assert
        Assert.True(sut1.Equals(sut2));
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
    }
}
