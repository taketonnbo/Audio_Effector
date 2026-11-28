using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Presentation.ViewModels;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// DeviceManagerViewModel のアルバム内楽曲ソート（PC側トラック番号優先・自然順ソート・複数フォルダ結合アルバム整列）を検証するテストクラス
/// </summary>
public sealed class DeviceManagerViewModelTests
{
    /// <summary>
    /// MTP返却順のような順不同の楽曲リストが、ゼロ埋めトラック番号の昇順で正しく整列されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_MTP返却順のような順不同リスト_楽曲番号の昇順で整列されること()
    {
        // Arrange
        var sut = CreateDeviceAlbum(
            "Sample Album",
            "Sample Artist",
            "08-SongH.mp3",
            "11-SongK.mp3",
            "02-SongB.mp3",
            "01-SongA.mp3",
            "10-SongJ.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums: null);

        // Assert
        Assert.Equal(
            new[] { "01-SongA.mp3", "02-SongB.mp3", "08-SongH.mp3", "10-SongJ.mp3", "11-SongK.mp3" },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// ゼロ埋めなしの連番ファイル名が辞書順ではなく自然順（数値昇順）で整列されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_ゼロ埋めなしの連番ファイル名_辞書順ではなく自然順で整列されること()
    {
        // Arrange
        var sut = CreateDeviceAlbum(
            "Natural Order Album",
            "Sample Artist",
            "10-曲C.mp3",
            "2-曲B.mp3",
            "1-曲A.mp3",
            "20-曲D.mp3",
            "3-曲E.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums: Array.Empty<Album>());

        // Assert
        Assert.Equal(
            new[] { "1-曲A.mp3", "2-曲B.mp3", "3-曲E.mp3", "10-曲C.mp3", "20-曲D.mp3" },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// PC側アルバムにトラック番号が設定されている場合、ファイル名の辞書順や自然順よりもPC側トラック番号順が優先されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_PC側アルバムにトラック番号がある場合_ファイル名順よりトラック番号順が優先されること()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Chronicle",
                "Artist A",
                (@"C:\Music\Artist A\Chronicle\z_opening.mp3", 1),
                (@"C:\Music\Artist A\Chronicle\m_interlude.mp3", 2),
                (@"C:\Music\Artist A\Chronicle\a_finale.mp3", 3))
        };

        var sut = CreateDeviceAlbum(
            "Chronicle",
            "Artist A",
            "a_finale.mp3",
            "Z_OPENING.MP3",
            "m_interlude.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(
            new[] { "Z_OPENING.MP3", "m_interlude.mp3", "a_finale.mp3" },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// トランスコード転送などで拡張子が異なる場合でも、拡張子を除いたファイル名一致によりPC側トラック番号順が優先されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_トランスコード等で拡張子が異なる場合_拡張子除去一致でPC側トラック番号順が優先されること()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "HiRes Album",
                "Artist B",
                (@"C:\Music\Artist B\HiRes Album\zeta.flac", 1),
                (@"C:\Music\Artist B\HiRes Album\beta.flac", 2),
                (@"C:\Music\Artist B\HiRes Album\alpha.flac", 3))
        };

        var sut = CreateDeviceAlbum(
            "HiRes Album",
            "Artist B",
            "alpha.m4a",
            "beta.m4a",
            "zeta.m4a");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(
            new[] { "zeta.m4a", "beta.m4a", "alpha.m4a" },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// PC一致トラック・トラック番号未設定トラック・PC未一致トラックが混在する場合、トラック番号ありを先頭に並べ、残りをファイル名自然順で整列することを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_トラック番号ありと未設定およびPC未一致が混在する場合_番号あり優先の後に自然順で並ぶこと()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Mixed Album",
                "Artist C",
                (@"C:\Music\Artist C\Mixed Album\z_first.mp3", 1),
                (@"C:\Music\Artist C\Mixed Album\a_second.mp3", 2),
                (@"C:\Music\Artist C\Mixed Album\10_unset.mp3", 0),
                (@"C:\Music\Artist C\Mixed Album\2_unset.mp3", 0))
        };

        var sut = CreateDeviceAlbum(
            "Mixed Album",
            "Artist C",
            "10_unset.mp3",
            "12_device_only.mp3",
            "a_second.mp3",
            "3_device_only.mp3",
            "z_first.mp3",
            "2_unset.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(
            new[]
            {
                "z_first.mp3",
                "a_second.mp3",
                "2_unset.mp3",
                "3_device_only.mp3",
                "10_unset.mp3",
                "12_device_only.mp3"
            },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// PC側アルバムタイトルに禁止文字が含まれデバイス側フォルダ名がサニタイズされている場合でも、一致判定してPC側トラック番号順が優先されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_サニタイズ済みアルバムフォルダ名の場合_PC側トラック番号順が優先されること()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Live: Vol.1?",
                "AC/DC",
                (@"C:\Music\AC_DC\Live\z_intro.mp3", 1),
                (@"C:\Music\AC_DC\Live\a_encore.mp3", 2))
        };

        var sut = CreateDeviceAlbum(
            "Live_ Vol.1_",
            "AC_DC",
            "a_encore.mp3",
            "z_intro.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(
            new[] { "z_intro.mp3", "a_encore.mp3" },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 別アルバムに同名ファイルが存在しても、無関係なアルバムのトラック番号が誤適用されないことを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_他アルバムに同名ファイルがある場合_別アルバムのトラック番号を誤適用しないこと()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Other Album",
                "Other Artist",
                (@"C:\Music\Other\10-track.mp3", 1),
                (@"C:\Music\Other\2-track.mp3", 2))
        };

        var sut = CreateDeviceAlbum(
            "Target Album",
            "Target Artist",
            "10-track.mp3",
            "2-track.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(
            new[] { "2-track.mp3", "10-track.mp3" },
            sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 複数フォルダが結合されてアーティスト名がVarious Artistsへ変化したアルバムでも、タイトル一致および同一トラック番号時のタイブレーカーで曲順が崩れないことを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_複数フォルダ結合アルバムおよび同一トラック番号タイブレーカー_曲順が確定的に整列されること()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Compilation Best",
                "Compilation Artist",
                (@"C:\Music\Comp\disc2_01.mp3", 1),
                (@"C:\Music\Comp\disc1_01.mp3", 1),
                (@"C:\Music\Comp\disc1_02.mp3", 2))
        };

        var sut = new DeviceAlbum
        {
            Title = "Compilation Best",
            Artist = "Various Artists",
            Path = @"\Music\ArtistA\Compilation Best"
        };
        sut.Tracks.Add(new DeviceTrack { Title = "disc1_02.mp3", Path = @"\Music\ArtistB\Compilation Best\disc1_02.mp3" });
        sut.Tracks.Add(new DeviceTrack { Title = "disc2_01.mp3", Path = @"\Music\ArtistB\Compilation Best\disc2_01.mp3" });
        sut.Tracks.Add(new DeviceTrack { Title = "disc1_01.mp3", Path = @"\Music\ArtistA\Compilation Best\disc1_01.mp3" });
        sut.Tracks.Add(new DeviceTrack { Title = "shared.mp3", Path = @"\Music\Disc 10\Compilation Best\shared.mp3" });
        sut.Tracks.Add(new DeviceTrack { Title = "shared.mp3", Path = @"\Music\Disc 2\Compilation Best\shared.mp3" });

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(
            new[]
            {
                @"\Music\ArtistA\Compilation Best\disc1_01.mp3",
                @"\Music\ArtistB\Compilation Best\disc2_01.mp3",
                @"\Music\ArtistB\Compilation Best\disc1_02.mp3",
                @"\Music\Disc 2\Compilation Best\shared.mp3",
                @"\Music\Disc 10\Compilation Best\shared.mp3"
            },
            sut.Tracks.Select(t => t.Path).ToArray());
    }

    /// <summary>
    /// 同名アルバムが複数アーティストに存在する場合、アーティスト名まで一致するPC側アルバムのトラック番号が優先参照されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_同名アルバムが複数アーティストに存在する場合_一致アーティストのトラック番号を優先すること()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Greatest Hits",
                "Artist X",
                (@"C:\Music\Artist X\Greatest Hits\b.mp3", 1),
                (@"C:\Music\Artist X\Greatest Hits\a.mp3", 2)),
            CreatePcAlbum(
                "Greatest Hits",
                "Artist Y",
                (@"C:\Music\Artist Y\Greatest Hits\a.mp3", 1),
                (@"C:\Music\Artist Y\Greatest Hits\b.mp3", 2))
        };

        var albumX = CreateDeviceAlbum("Greatest Hits", "Artist X", "a.mp3", "b.mp3");
        var albumY = CreateDeviceAlbum("Greatest Hits", "Artist Y", "b.mp3", "a.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumsTracks(new[] { albumX, albumY }, pcAlbums);

        // Assert
        Assert.Equal(new[] { "b.mp3", "a.mp3" }, albumX.Tracks.Select(t => t.Title).ToArray());
        Assert.Equal(new[] { "a.mp3", "b.mp3" }, albumY.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 同名アルバムが複数アーティストに存在し、一致アーティスト側の対象曲がTrackNumber未設定（0）または端末独自ファイルの場合に、別アーティストのトラック番号が誤適用されないことを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_同名タイトルで一致アーティスト側の曲がTrackNumber未設定の場合_別アーティストのトラック番号を誤適用しないこと()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Greatest Hits",
                "Artist X",
                (@"C:\Music\Artist X\Greatest Hits\a.mp3", 0),
                (@"C:\Music\Artist X\Greatest Hits\b.mp3", 1)),
            CreatePcAlbum(
                "Greatest Hits",
                "Artist Y",
                (@"C:\Music\Artist Y\Greatest Hits\a.mp3", 1),
                (@"C:\Music\Artist Y\Greatest Hits\b.mp3", 2))
        };

        var sut = CreateDeviceAlbum("Greatest Hits", "Artist X", "a.mp3", "b.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(new[] { "b.mp3", "a.mp3" }, sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 同名アルバムで一致アーティスト側の全曲がTrackNumber未設定（0）の場合でも、別アーティストのトラック番号が誤適用されず自然順になることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_同名タイトルで一致アーティスト側が全曲TrackNumber未設定の場合_別アーティストのトラック番号を誤適用しないこと()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Greatest Hits",
                "Artist X",
                (@"C:\Music\Artist X\Greatest Hits\01.mp3", 0),
                (@"C:\Music\Artist X\Greatest Hits\02.mp3", 0)),
            CreatePcAlbum(
                "Greatest Hits",
                "Artist Y",
                (@"C:\Music\Artist Y\Greatest Hits\02.mp3", 1),
                (@"C:\Music\Artist Y\Greatest Hits\01.mp3", 2))
        };

        var sut = CreateDeviceAlbum("Greatest Hits", "Artist X", "02.mp3", "01.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(new[] { "01.mp3", "02.mp3" }, sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// PC未登録の別アーティストによる同名アルバムに対して、PC登録済み他アーティストのトラック番号が誤適用されないことを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_PC未登録の別アーティストによる同名アルバムの場合_他アーティストのトラック番号を誤適用しないこと()
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Best",
                "Artist Y",
                (@"C:\Music\Artist Y\Best\02.mp3", 1),
                (@"C:\Music\Artist Y\Best\01.mp3", 2))
        };

        var sut = CreateDeviceAlbum("Best", "Artist Z", "02.mp3", "01.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(new[] { "01.mp3", "02.mp3" }, sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 旧曲別アーティスト配置（PC側アルバムアーティストと異なる演奏者アーティストフォルダ配下）でも、該当演奏者の一致によりトラック番号順が優先されることを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_旧曲別アーティスト配置の場合_演奏者アーティスト一致でPC側トラック番号順が優先されること()
    {
        // Arrange
        var pcAlbum = new Album
        {
            Title = "Anthology",
            Artist = "Various Artists"
        };
        pcAlbum.Tracks.Add(new Track
        {
            FilePath = @"C:\Music\Anthology\z_first.mp3",
            TrackNumber = 1,
            Album = "Anthology",
            Artist = "Solo Artist",
            Title = "z_first"
        });
        pcAlbum.Tracks.Add(new Track
        {
            FilePath = @"C:\Music\Anthology\a_second.mp3",
            TrackNumber = 2,
            Album = "Anthology",
            Artist = "Solo Artist",
            Title = "a_second"
        });

        var sut = CreateDeviceAlbum("Anthology", "Solo Artist", "a_second.mp3", "z_first.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, new[] { pcAlbum });

        // Assert
        Assert.Equal(new[] { "z_first.mp3", "a_second.mp3" }, sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 旧フォルダ名サニタイズ規則（禁止文字除去・旧末尾ピリオド保持）で保存された既存デバイスフォルダでもPC側トラック番号順が優先されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("ACDC", "Live Vol.1")]
    [InlineData("AC_DC", "Live_ Vol.1_")]
    public void SortAlbumTracks_旧禁止文字除去を含むサニタイズフォルダ名の場合_PC側トラック番号順が優先されること(string deviceArtist, string deviceAlbumTitle)
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Live: Vol.1?",
                "AC/DC",
                (@"C:\Music\AC_DC\Live\z_intro.mp3", 1),
                (@"C:\Music\AC_DC\Live\a_encore.mp3", 2))
        };

        var sut = CreateDeviceAlbum(deviceAlbumTitle, deviceArtist, "a_encore.mp3", "z_intro.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(new[] { "z_intro.mp3", "a_encore.mp3" }, sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// 末尾ピリオドを含むアルバム名について、新規則（末尾ピリオド除去）と旧規則（末尾ピリオド保持・禁止文字除去）のいずれのフォルダ名でもPC側トラック番号順が優先されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("Live_ Vol")]
    [InlineData("Live Vol")]
    [InlineData("Live_ Vol.")]
    [InlineData("Live Vol.")]
    public void SortAlbumTracks_末尾ピリオド新旧サニタイズ互換フォルダ名の場合_PC側トラック番号順が優先されること(string deviceAlbumTitle)
    {
        // Arrange
        var pcAlbums = new List<Album>
        {
            CreatePcAlbum(
                "Live: Vol.",
                "Band",
                (@"C:\Music\Band\Live\z_open.mp3", 1),
                (@"C:\Music\Band\Live\a_close.mp3", 2))
        };

        var sut = CreateDeviceAlbum(deviceAlbumTitle, "Band", "a_close.mp3", "z_open.mp3");

        // Act
        DeviceManagerViewModel.SortAlbumTracks(sut, pcAlbums);

        // Assert
        Assert.Equal(new[] { "z_open.mp3", "a_close.mp3" }, sut.Tracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// SortAlbumTracksおよびSortAlbumsTracksがnull・空アルバム・単一曲アルバムに対して例外を送出せず安全に処理することを検証します。
    /// </summary>
    [Fact]
    public void SortAlbumTracks_Nullおよび空アルバムと単一曲アルバム_例外なく安全に処理されること()
    {
        // Arrange
        var emptyAlbum = CreateDeviceAlbum("Empty", "Artist");
        var singleTrackAlbum = CreateDeviceAlbum("Single", "Artist", "01-only.mp3");
        var sut = new DeviceAlbum?[] { null, emptyAlbum, singleTrackAlbum };

        // Act
        DeviceManagerViewModel.SortAlbumTracks(null, pcAlbums: null);
        DeviceManagerViewModel.SortAlbumTracks(emptyAlbum, pcAlbums: null);
        DeviceManagerViewModel.SortAlbumTracks(singleTrackAlbum, pcAlbums: null);
        DeviceManagerViewModel.SortAlbumsTracks(null, pcAlbums: null);
        DeviceManagerViewModel.SortAlbumsTracks(sut!, pcAlbums: null);

        // Assert
        Assert.Empty(emptyAlbum.Tracks);
        var onlyTrack = Assert.Single(singleTrackAlbum.Tracks);
        Assert.Equal("01-only.mp3", onlyTrack.Title);
    }

    /// <summary>
    /// 自然順比較がnull・空文字・大文字小文字非区別・先頭ゼロ差・長桁数値に対してオーバーフローせず一貫した順序を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData(null, "1.mp3", -1)]
    [InlineData("1.mp3", null, 1)]
    [InlineData(null, null, 0)]
    [InlineData("track2.mp3", "Track10.mp3", -1)]
    [InlineData("01-song.mp3", "1-song.mp3", -1)]
    [InlineData("10000000000000000000000001.mp3", "10000000000000000000000002.mp3", -1)]
    [InlineData("Disc 1 - 10.mp3", "Disc 2 - 1.mp3", -1)]
    public void CompareNatural_各種境界値と数値表現_期待される符号の比較結果を返すこと(string? left, string? right, int expectedSign)
    {
        // Arrange
        string? sutLeft = left;
        string? sutRight = right;

        // Act
        int actual = DeviceManagerViewModel.CompareNatural(sutLeft, sutRight);
        int reversed = DeviceManagerViewModel.CompareNatural(sutRight, sutLeft);

        // Assert
        Assert.Equal(expectedSign, Math.Sign(actual));
        Assert.Equal(-expectedSign, Math.Sign(reversed));
    }

    /// <summary>
    /// 自然順比較が全ペア・全三つ組に対して厳密弱順序（反射律・反対称律・推移律）を満たすことを検証します。
    /// </summary>
    [Fact]
    public void CompareNatural_境界文字列の全三つ組_反射律と反対称律および推移律を満たすこと()
    {
        // Arrange
        string?[] sut =
        {
            null,
            string.Empty,
            "0",
            "00",
            "0-a",
            "00-a",
            "01-a",
            "1-a",
            "01-b",
            "1-b",
            "1",
            "001",
            "2",
            "10",
            "a",
            "A"
        };

        // Act
        int n = sut.Length;
        var signs = new int[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                signs[i, j] = Math.Sign(DeviceManagerViewModel.CompareNatural(sut[i], sut[j]));
            }
        }

        // Assert
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(0, signs[i, i]);

            for (int j = 0; j < n; j++)
            {
                Assert.Equal(-signs[i, j], signs[j, i]);

                for (int k = 0; k < n; k++)
                {
                    if (signs[i, j] < 0 && signs[j, k] < 0)
                    {
                        Assert.True(signs[i, k] < 0, $"推移律違反: '{sut[i]}' < '{sut[j]}' < '{sut[k]}'");
                    }
                    else if (signs[i, j] == 0 && signs[j, k] == 0)
                    {
                        Assert.Equal(0, signs[i, k]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// ファイルシステム上の複数フォルダ結合アルバムをLoadAlbumsAsyncで読み込んだ際、全スキャン後に各アルバム内トラックが楽曲順へ整列されることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadAlbumsAsync_ファイルシステム上の複数フォルダ結合アルバム読込時_楽曲順へ整列されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_DeviceManager_{Guid.NewGuid():N}");
        string folderA = Path.Combine(tempRoot, "ArtistA", "SharedAlbum");
        string folderB = Path.Combine(tempRoot, "ArtistB", "SharedAlbum");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);

        try
        {
            File.WriteAllText(Path.Combine(folderA, "10-TrackJ.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(folderA, "2-TrackB.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(folderB, "1-TrackA.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(folderB, "3-TrackC.mp3"), string.Empty);

            string rootDrive = Path.GetPathRoot(tempRoot) ?? @"C:\";
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                Type = MainViewModel.DeviceType.FileSystem,
                Drive = new DriveInfo(rootDrive),
                RootPath = tempRoot
            };

            // Act
            var sut = new DeviceManagerViewModel(device, tempRoot, new List<Album>());
            await sut.InitialLoadTask;

            // Assert
            Assert.False(sut.IsLoading);
            var album = Assert.Single(sut.DeviceAlbums);
            Assert.Equal("SharedAlbum", album.Title);
            Assert.Equal("Various Artists", album.Artist);
            Assert.Equal(
                new[] { "1-TrackA.mp3", "2-TrackB.mp3", "3-TrackC.mp3", "10-TrackJ.mp3" },
                album.Tracks.Select(t => t.Title).ToArray());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static DeviceAlbum CreateDeviceAlbum(string title, string artist, params string[] fileNames)
    {
        var album = new DeviceAlbum
        {
            Title = title,
            Artist = artist,
            Path = $@"\Music\{artist}\{title}"
        };

        foreach (string fileName in fileNames)
        {
            album.Tracks.Add(new DeviceTrack
            {
                Title = fileName,
                Path = $@"\Music\{artist}\{title}\{fileName}"
            });
        }

        return album;
    }

    private static Album CreatePcAlbum(string title, string artist, params (string FilePath, uint TrackNumber)[] tracks)
    {
        var album = new Album
        {
            Title = title,
            Artist = artist
        };

        foreach (var (filePath, trackNumber) in tracks)
        {
            album.Tracks.Add(new Track
            {
                FilePath = filePath,
                TrackNumber = trackNumber,
                Album = title,
                Artist = artist,
                Title = Path.GetFileNameWithoutExtension(filePath)
            });
        }

        return album;
    }
}
