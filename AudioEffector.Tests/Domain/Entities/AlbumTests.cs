using AudioEffector.Domain.Entities;
using Xunit;

namespace AudioEffector.Tests.Domain.Entities;

/// <summary>
/// Albumエンティティの代表トラックパス取得などドメインロジックを検証するテストクラス
/// </summary>
public sealed class AlbumTests
{
    /// <summary>
    /// トラックが空のアルバムでRepresentativeTrackFilePathを参照した場合、例外を送出せず空文字を返すことを検証します
    /// </summary>
    [Fact]
    public void RepresentativeTrackFilePath_トラックが空の場合_空文字を返す()
    {
        // Arrange
        var sut = new Album("Empty Album", "Artist A");

        // Act
        var path = sut.RepresentativeTrackFilePath;

        // Assert
        Assert.Equal(string.Empty, path);
    }

    /// <summary>
    /// 有効なファイルパスを持つトラックが含まれる場合、最初の有効なファイルパスを返すことを検証します
    /// </summary>
    [Fact]
    public void RepresentativeTrackFilePath_有効なパスのトラックがある場合_先頭の有効パスを返す()
    {
        // Arrange
        var sut = new Album("Test Album", "Artist A", 2024, new[]
        {
            new Track { Title = "No Path", FilePath = string.Empty },
            new Track { Title = "Valid Path", FilePath = @"C:\music\track1.flac" },
            new Track { Title = "Second Path", FilePath = @"C:\music\track2.flac" },
        });

        // Act
        var path = sut.RepresentativeTrackFilePath;

        // Assert
        Assert.Equal(@"C:\music\track1.flac", path);
    }
}
