using System;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Tests.Domain.ValueObjects;

/// <summary>
/// AlbumId値オブジェクトの生成・バリデーション・決定論的ID生成を検証するテストクラス
/// </summary>
public sealed class AlbumIdTests
{
    /// <summary>
    /// 有効なGUIDを指定してAlbumIdを生成した場合、その値が保持されるかを検証します。
    /// </summary>
    [Fact]
    public void From_有効なGUID_指定GUIDで生成されること()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var sut = AlbumId.From(guid);

        // Assert
        Assert.Equal(guid, sut.Value);
        Assert.Equal(guid.ToString(), sut.ToString());
    }

    /// <summary>
    /// Guid.Emptyを指定して生成した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void From_空のGUID_ArgumentExceptionをスローすること()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new AlbumId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => AlbumId.From(Guid.Empty));
    }

    /// <summary>
    /// 有効なGUID文字列を指定して生成した場合、正しくパースされてインスタンスが生成されるかを検証します。
    /// </summary>
    [Fact]
    public void From_有効なGUID文字列_正しくパースして生成されること()
    {
        // Arrange
        var guid = Guid.NewGuid();
        string guidStr = guid.ToString();

        // Act
        var sut = AlbumId.From(guidStr);

        // Assert
        Assert.Equal(guid, sut.Value);
    }

    /// <summary>
    /// 不正な文字列や空GUID文字列を指定して生成した場合、ArgumentExceptionがスローされるかを検証します。
    /// </summary>
    [Theory]
    [InlineData("invalid-guid")]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void From_不正なGUID文字列_ArgumentExceptionをスローすること(string invalidStr)
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => AlbumId.From(invalidStr));
    }

    /// <summary>
    /// 同一のアルバム名とアーティスト名の組み合わせに対して、大文字小文字や前後空白の違いを吸収し常に同一のAlbumIdが生成されることを検証します。
    /// </summary>
    [Fact]
    public void FromAlbumKey_大文字小文字や前後空白が異なる同一キー_常に同一のAlbumIdを生成すること()
    {
        // Arrange
        const string albumA = "Greatest Hits";
        const string artistA = "The Band";

        // Act
        var sut1 = AlbumId.FromAlbumKey(albumA, artistA);
        var sut2 = AlbumId.FromAlbumKey("  greatest hits  ", "  THE BAND ");

        // Assert
        Assert.NotEqual(Guid.Empty, sut1.Value);
        Assert.Equal(sut1, sut2);
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
    }

    /// <summary>
    /// 同名アルバムであってもアーティストが異なる場合、異なるAlbumIdが生成されることを検証します。
    /// </summary>
    [Fact]
    public void FromAlbumKey_同名アルバムで別アーティスト_異なるAlbumIdを生成すること()
    {
        // Arrange & Act
        var sut1 = AlbumId.FromAlbumKey("Greatest Hits", "Artist A");
        var sut2 = AlbumId.FromAlbumKey("Greatest Hits", "Artist B");

        // Assert
        Assert.NotEqual(sut1, sut2);
    }

    /// <summary>
    /// nullや空文字・空白のみの入力であっても例外とならず、同一の正規化結果から決定論的なAlbumIdが生成されることを検証します。
    /// </summary>
    [Fact]
    public void FromAlbumKey_nullや空白入力_空でない同一のAlbumIdを生成すること()
    {
        // Arrange & Act
        var sutNull = AlbumId.FromAlbumKey(null, null);
        var sutEmpty = AlbumId.FromAlbumKey("", "");
        var sutWhitespace = AlbumId.FromAlbumKey("   ", "\t ");

        // Assert
        Assert.NotEqual(Guid.Empty, sutNull.Value);
        Assert.Equal(sutNull, sutEmpty);
        Assert.Equal(sutNull, sutWhitespace);
    }

    /// <summary>
    /// FromAlbumKeyで生成されたGUIDがRFC 4122 UUID v5相当のバージョンおよびバリアントビットを持つことを検証します。
    /// </summary>
    [Fact]
    public void FromAlbumKey_生成GUID_RFC4122のバージョン5およびバリアントビットを持つこと()
    {
        // Arrange & Act
        var sut = AlbumId.FromAlbumKey("Blue Train", "John Coltrane");
        byte[] bytes = sut.Value.ToByteArray(bigEndian: true);

        // Assert
        Assert.Equal(0x50, bytes[6] & 0xF0);
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    /// <summary>
    /// 同一のGUIDを持つAlbumId同士が等価と判定されるかを検証します。
    /// </summary>
    [Fact]
    public void Equals_同一GUID同士_等価と判定されること()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var sut1 = AlbumId.From(guid);
        var sut2 = AlbumId.From(guid);

        // Act & Assert
        Assert.True(sut1.Equals(sut2));
        Assert.True(sut1 == sut2);
        Assert.Equal(sut1.GetHashCode(), sut2.GetHashCode());
    }
}
