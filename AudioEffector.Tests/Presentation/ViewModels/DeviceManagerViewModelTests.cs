using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
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
            using var sut = new DeviceManagerViewModel(device, tempRoot, new List<Album>());
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

    /// <summary>
    /// ファイルシステム機器からアルバム単位で PC ライブラリフォルダ配下へ逆転送した際、
    /// 既存同名ファイルは上書きされずスキップされ、1 曲欠落エラー時も残り曲が継続取り込みされ、
    /// 結果サマリ通知および PC ライブラリ自動再反映コールバックが発火することを検証します
    /// </summary>
    [Fact]
    public async Task ImportAlbumAsync_ファイルシステム機器からライブラリ配下へ逆転送時_重複スキップとエラー継続のうえライブラリ自動反映されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseFs_{Guid.NewGuid():N}");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string deviceAlbumDir = Path.Combine(deviceRoot, "ArtistR", "AlbumR");
        string pcLibraryDir = Path.Combine(tempRoot, "PcLibrary");
        string destSubDir = Path.Combine(pcLibraryDir, "Imported");
        Directory.CreateDirectory(deviceAlbumDir);
        Directory.CreateDirectory(destSubDir);

        string existingSrc = Path.Combine(deviceAlbumDir, "01_Exist.mp3");
        string missingSrc = Path.Combine(deviceAlbumDir, "02_Missing.mp3");
        string newSrc = Path.Combine(deviceAlbumDir, "03_New.flac");
        File.WriteAllText(existingSrc, "device-version");
        File.WriteAllText(newSrc, "device-flac-bytes");

        // PC 側に既存同名ファイルを配置して上書き防止を検証
        string pcExistingDir = Path.Combine(destSubDir, "ArtistR", "AlbumR");
        Directory.CreateDirectory(pcExistingDir);
        string pcExistingFile = Path.Combine(pcExistingDir, "01_Exist.mp3");
        File.WriteAllText(pcExistingFile, "pc-original-content");

        var settingsService = new FakeImportSettingsService(pcLibraryDir);
        string? refreshedPath = null;
        TransferResultSummary? capturedSummary = null;

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };

            using var sut = new DeviceManagerViewModel(
                device,
                deviceRoot,
                new List<Album>(),
                settingsService: settingsService,
                onLibraryRefreshRequested: path => refreshedPath = path,
                resolveMtpClient: null,
                selectDestinationFolder: _ => destSubDir,
                showImportResult: summary => capturedSummary = summary);
            await sut.InitialLoadTask;

            var deviceAlbum = new DeviceAlbum("AlbumR", "ArtistR", deviceAlbumDir);
            deviceAlbum.Tracks.Add(new DeviceTrack("01_Exist.mp3", "ArtistR", "AlbumR", existingSrc));
            deviceAlbum.Tracks.Add(new DeviceTrack("02_Missing.mp3", "ArtistR", "AlbumR", missingSrc));
            deviceAlbum.Tracks.Add(new DeviceTrack("03_New.flac", "ArtistR", "AlbumR", newSrc));

            // Act
            var summary = await sut.ImportAlbumAsync(deviceAlbum);

            // Assert
            Assert.NotNull(summary);
            Assert.Same(summary, capturedSummary);
            Assert.Equal("取り込み", summary!.OperationLabel);
            Assert.Equal(1, summary.SuccessCount);
            Assert.Equal(1, summary.SkippedCount);
            Assert.Equal(1, summary.FailedCount);
            Assert.False(summary.WasCancelled);
            Assert.Equal("pc-original-content", File.ReadAllText(pcExistingFile));
            Assert.Equal("device-flac-bytes", File.ReadAllText(Path.Combine(pcExistingDir, "03_New.flac")));
            Assert.Equal(destSubDir, refreshedPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 登録済み PC ライブラリフォルダ以外の保存先へ曲単位で逆転送した場合、
    /// ファイルは正しくコピーされる一方でライブラリ自動再反映コールバックは発火しないことを検証します
    /// </summary>
    [Fact]
    public async Task ImportTrackAsync_ライブラリフォルダ外へ曲単位逆転送時_取り込み成功しライブラリ再反映は発火しないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseSingle_{Guid.NewGuid():N}");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string deviceAlbumDir = Path.Combine(deviceRoot, "SoloArtist", "SoloAlbum");
        string pcLibraryDir = Path.Combine(tempRoot, "PcLibrary");
        string outsideDir = Path.Combine(tempRoot, "ExternalExport");
        Directory.CreateDirectory(deviceAlbumDir);
        Directory.CreateDirectory(pcLibraryDir);
        Directory.CreateDirectory(outsideDir);

        string trackSrc = Path.Combine(deviceAlbumDir, "01_Solo.mp3");
        File.WriteAllText(trackSrc, "solo-track-data");

        var settingsService = new FakeImportSettingsService(pcLibraryDir);
        int refreshCount = 0;

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };

            using var sut = new DeviceManagerViewModel(
                device,
                deviceRoot,
                new List<Album>(),
                settingsService: settingsService,
                onLibraryRefreshRequested: _ => refreshCount++,
                resolveMtpClient: null,
                selectDestinationFolder: _ => outsideDir,
                showImportResult: _ => { });
            await sut.InitialLoadTask;

            var album = new DeviceAlbum("SoloAlbum", "SoloArtist", deviceAlbumDir);
            var track = new DeviceTrack("01_Solo.mp3", "SoloArtist", "SoloAlbum", trackSrc);
            album.Tracks.Add(track);
            sut.DeviceAlbums.Add(album);

            // Act
            var summary = await sut.ImportTrackAsync(track);

            // Assert
            Assert.NotNull(summary);
            Assert.Equal(1, summary!.SuccessCount);
            Assert.Equal(0, summary.FailedCount);
            Assert.Equal("solo-track-data", File.ReadAllText(Path.Combine(outsideDir, "SoloArtist", "SoloAlbum", "01_Solo.mp3")));
            Assert.Equal(0, refreshCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// MTP デバイスからのアルバム逆転送中に 2 曲目のダウンロードでキャンセル要求した場合、
    /// 1 曲目は正常保存され、2 曲目の不完全ファイルは残らず WasCancelled=true のサマリが通知されることを検証します
    /// </summary>
    [Fact]
    public async Task ImportAlbumAsync_MTP機器からの逆転送中にキャンセル時_不完全ファイルが残らずキャンセルサマリが通知されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseMtpCancel_{Guid.NewGuid():N}");
        string destDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(destDir);

        using var secondStarted = new ManualResetEventSlim(false);
        using var allowSecondFinish = new ManualResetEventSlim(false);

        var fakeMtp = new FakeImportMtpClient();
        fakeMtp.Connect();
        fakeMtp.AddFile(@"\Music\ArtistM\AlbumM\01.mp3", "mtp-track-1");
        fakeMtp.AddFile(@"\Music\ArtistM\AlbumM\02.mp3", "mtp-track-2-partial");
        fakeMtp.OnDownloadFile = (src, stream) =>
        {
            if (src.EndsWith("02.mp3", StringComparison.OrdinalIgnoreCase))
            {
                byte[] partial = System.Text.Encoding.UTF8.GetBytes("partial");
                stream.Write(partial, 0, partial.Length);
                secondStarted.Set();
                allowSecondFinish.Wait(TimeSpan.FromSeconds(5));
            }
        };

        TransferResultSummary? capturedSummary = null;

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Fake MTP",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };

            using var sut = new DeviceManagerViewModel(
                device,
                @"\Music",
                new List<Album>(),
                settingsService: null,
                onLibraryRefreshRequested: null,
                resolveMtpClient: _ => fakeMtp,
                selectDestinationFolder: _ => destDir,
                showImportResult: summary => capturedSummary = summary);
            await sut.InitialLoadTask;

            var album = new DeviceAlbum("AlbumM", "ArtistM", @"\Music\ArtistM\AlbumM");
            album.Tracks.Add(new DeviceTrack("01.mp3", "ArtistM", "AlbumM", @"\Music\ArtistM\AlbumM\01.mp3"));
            album.Tracks.Add(new DeviceTrack("02.mp3", "ArtistM", "AlbumM", @"\Music\ArtistM\AlbumM\02.mp3"));

            // Act
            var importTask = sut.ImportAlbumAsync(album);
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(sut.CancelImportCommand.CanExecute(null));

            sut.CancelImportCommand.Execute(null);
            Assert.True(sut.IsCancellingImport);
            Assert.False(sut.CancelImportCommand.CanExecute(null));
            allowSecondFinish.Set();

            var summary = await importTask;

            // Assert
            Assert.NotNull(summary);
            Assert.Same(summary, capturedSummary);
            Assert.True(summary!.WasCancelled);
            Assert.Equal(1, summary.SuccessCount);
            Assert.Equal("mtp-track-1", File.ReadAllText(Path.Combine(destDir, "ArtistM", "AlbumM", "01.mp3")));
            Assert.False(File.Exists(Path.Combine(destDir, "ArtistM", "AlbumM", "02.mp3")));
            Assert.False(sut.IsImporting);
            Assert.False(sut.IsCancellingImport);
        }
        finally
        {
            allowSecondFinish.Set();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// MTP 逆転送中にデバイス切断が発生した場合、回復不能エラーとして安全に中断され不完全ファイルが残らないことを検証します
    /// </summary>
    [Fact]
    public async Task ImportTracksToPcAsync_MTP切断発生時_回復不能エラーで安全中断され不完全ファイルが残らないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseMtpDisconnect_{Guid.NewGuid():N}");
        string destDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(destDir);

        var fakeMtp = new FakeImportMtpClient();
        fakeMtp.Connect();
        fakeMtp.AddFile(@"\Music\ArtistD\AlbumD\01.mp3", "track-1");
        fakeMtp.AddFile(@"\Music\ArtistD\AlbumD\02.mp3", "track-2");
        int downloadAttempts = 0;
        fakeMtp.OnDownloadFile = (_, _) =>
        {
            downloadAttempts++;
            fakeMtp.Disconnect();
            throw new IOException("MTP ケーブルが切断されました");
        };

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Fake MTP",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };

            using var sut = new DeviceManagerViewModel(
                device,
                @"\Music",
                new List<Album>(),
                settingsService: null,
                onLibraryRefreshRequested: null,
                resolveMtpClient: _ => fakeMtp,
                selectDestinationFolder: _ => destDir,
                showImportResult: _ => { });
            await sut.InitialLoadTask;

            var album = new DeviceAlbum("AlbumD", "ArtistD", @"\Music\ArtistD\AlbumD");
            var t1 = new DeviceTrack("01.mp3", "ArtistD", "AlbumD", @"\Music\ArtistD\AlbumD\01.mp3");
            var t2 = new DeviceTrack("02.mp3", "ArtistD", "AlbumD", @"\Music\ArtistD\AlbumD\02.mp3");

            // Act
            var summary = await sut.ImportTracksToPcAsync(new[] { (album, t1), (album, t2) }, destDir);

            // Assert
            Assert.True(summary.WasAbortedByFatalError);
            Assert.True(summary.HasFatalError);
            Assert.Equal(0, summary.SuccessCount);
            Assert.Equal(1, summary.FailedCount);
            Assert.Equal(1, downloadAttempts);
            Assert.False(File.Exists(Path.Combine(destDir, "ArtistD", "AlbumD", "01.mp3")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ResolveImportFileName が通常ファイル名を保持し、コロン・禁止文字をサニタイズし、相対セグメントを拒否することを検証します
    /// </summary>
    [Theory]
    [InlineData(@"\Music\Artist\Album\01_Track (Live).flac", "01_Track (Live).flac")]
    [InlineData(@"\Music\Artist\Album\01.日本語.mp3", "01.日本語.mp3")]
    [InlineData(@"\Music\Artist\Album\Re:Zero.mp3", "Re_Zero.mp3")]
    [InlineData(@"\Music\Artist\Album\What?.mp3", "What_.mp3")]
    [InlineData(@"\Music\Artist\Album\Track*1.flac", "Track_1.flac")]
    [InlineData(@"\Music\Artist\Album\..", "")]
    [InlineData(@"\Music\Artist\Album\.", "")]
    [InlineData(@"\Music\Artist\Album\", "")]
    public void ResolveImportFileName_各種ファイル名と境界入力_適切にサニタイズまたは拒否されること(string devicePath, string expected)
    {
        // Arrange
        var track = new DeviceTrack("Title", "Artist", "Album", devicePath);

        // Act
        string actual = DeviceManagerViewModel.ResolveImportFileName(track);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// IsPathWithinDirectory が同一パス・子ディレクトリを true、兄弟プレフィックス不一致ディレクトリを false と判定することを検証します
    /// </summary>
    [Fact]
    public void IsPathWithinDirectory_兄弟プレフィックスや末尾区切りを含む境界値_正しく判定されること()
    {
        // Arrange
        string baseDir = Path.Combine(Path.GetTempPath(), "AE_Boundary_PcLibrary");
        string subDir = Path.Combine(baseDir, "Imported", "ArtistA");
        string siblingDir = Path.Combine(Path.GetTempPath(), "AE_Boundary_PcLibrary2");

        // Act & Assert
        Assert.True(DeviceManagerViewModel.IsPathWithinDirectory(baseDir, baseDir));
        Assert.True(DeviceManagerViewModel.IsPathWithinDirectory(baseDir + Path.DirectorySeparatorChar, baseDir));
        Assert.True(DeviceManagerViewModel.IsPathWithinDirectory(subDir, baseDir));
        Assert.False(DeviceManagerViewModel.IsPathWithinDirectory(siblingDir, baseDir));
        Assert.False(DeviceManagerViewModel.IsPathWithinDirectory(Path.Combine(siblingDir, "Sub"), baseDir));
        Assert.False(DeviceManagerViewModel.IsPathWithinDirectory(string.Empty, baseDir));
    }

    /// <summary>
    /// FileSystem 逆転送で転送元ルートが開始前に不在または途中で消失した場合、回復不能エラーとして安全中断されることを検証します
    /// </summary>
    [Fact]
    public async Task ImportTracksToPcAsync_FileSystem転送元ルート不在時_回復不能エラーで安全中断されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseFsDisconnect_{Guid.NewGuid():N}");
        string missingDeviceRoot = Path.Combine(tempRoot, "RemovedUsb");
        string activeDeviceRoot = Path.Combine(tempRoot, "ActiveUsb");
        string activeAlbumDir = Path.Combine(activeDeviceRoot, "ArtistX", "AlbumX");
        string destDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(activeAlbumDir);
        Directory.CreateDirectory(destDir);

        string track1Path = Path.Combine(activeAlbumDir, "01.mp3");
        string track2Path = Path.Combine(activeAlbumDir, "02.mp3");
        string track3Path = Path.Combine(activeAlbumDir, "03.mp3");
        File.WriteAllText(track1Path, "track1-data");
        File.WriteAllText(track2Path, "track2-data");
        File.WriteAllText(track3Path, "track3-data");

        try
        {
            // ケース 1: 開始前から転送元ルートが不在
            var missingDevice = new MainViewModel.DeviceViewModel
            {
                Name = "Removed USB",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = missingDeviceRoot
            };
            using var missingSut = new DeviceManagerViewModel(
                missingDevice,
                missingDeviceRoot,
                new List<Album>(),
                settingsService: null,
                onLibraryRefreshRequested: null,
                resolveMtpClient: null,
                selectDestinationFolder: _ => destDir,
                showImportResult: _ => { });
            await missingSut.InitialLoadTask;

            var missingAlbum = new DeviceAlbum("AlbumX", "ArtistX", Path.Combine(missingDeviceRoot, "ArtistX", "AlbumX"));
            var missingTrack = new DeviceTrack("01.mp3", "ArtistX", "AlbumX", Path.Combine(missingDeviceRoot, "ArtistX", "AlbumX", "01.mp3"));

            var preDisconnectedSummary = await missingSut.ImportTracksToPcAsync(new[] { (missingAlbum, missingTrack) }, destDir);
            Assert.True(preDisconnectedSummary.WasAbortedByFatalError);
            Assert.Equal(0, preDisconnectedSummary.SuccessCount);

            // ケース 2: 1 曲目完了後に転送元ルートが消失（USB 抜去相当）
            var activeDevice = new MainViewModel.DeviceViewModel
            {
                Name = "Active USB",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = activeDeviceRoot
            };
            using var activeSut = new DeviceManagerViewModel(
                activeDevice,
                activeDeviceRoot,
                new List<Album>(),
                settingsService: null,
                onLibraryRefreshRequested: null,
                resolveMtpClient: null,
                selectDestinationFolder: _ => destDir,
                showImportResult: _ => { });
            await activeSut.InitialLoadTask;

            var activeAlbum = new DeviceAlbum("AlbumX", "ArtistX", activeAlbumDir);
            var t1 = new DeviceTrack("01.mp3", "ArtistX", "AlbumX", track1Path);
            var t2 = new DeviceTrack("02.mp3", "ArtistX", "AlbumX", track2Path);
            var t3 = new DeviceTrack("03.mp3", "ArtistX", "AlbumX", track3Path);

            bool removedRoot = false;
            var disconnectProgress = new SyncTestProgress<TransferProgressInfo>(info =>
            {
                if (!removedRoot && info.CurrentCount == 1 && info.Percent > 0)
                {
                    removedRoot = true;
                    Directory.Delete(activeDeviceRoot, recursive: true);
                }
            });

            var midDisconnectedSummary = await activeSut.ImportTracksToPcAsync(
                new[] { (activeAlbum, t1), (activeAlbum, t2), (activeAlbum, t3) },
                destDir,
                disconnectProgress);

            Assert.True(midDisconnectedSummary.WasAbortedByFatalError);
            Assert.Equal(1, midDisconnectedSummary.SuccessCount);
            Assert.Equal(1, midDisconnectedSummary.FailedCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// FileSystem 逆転送の途中でキャンセルした場合、2 曲目の不完全ファイルが残らず WasCancelled=true で安全終了することを検証します
    /// </summary>
    [Fact]
    public async Task ImportTracksToPcAsync_FileSystem逆転送途中キャンセル時_不完全ファイルが残らないこと()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseFsCancel_{Guid.NewGuid():N}");
        string deviceRoot = Path.Combine(tempRoot, "Device");
        string albumDir = Path.Combine(deviceRoot, "ArtistC", "AlbumC");
        string destDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(albumDir);
        Directory.CreateDirectory(destDir);

        string track1Path = Path.Combine(albumDir, "01.wav");
        string track2Path = Path.Combine(albumDir, "02.wav");
        File.WriteAllBytes(track1Path, new byte[64 * 1024]);
        File.WriteAllBytes(track2Path, new byte[512 * 1024]);

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Test USB",
                Type = MainViewModel.DeviceType.FileSystem,
                RootPath = deviceRoot
            };
            using var sut = new DeviceManagerViewModel(
                device,
                deviceRoot,
                new List<Album>(),
                settingsService: null,
                onLibraryRefreshRequested: null,
                resolveMtpClient: null,
                selectDestinationFolder: _ => destDir,
                showImportResult: _ => { });
            await sut.InitialLoadTask;

            var album = new DeviceAlbum("AlbumC", "ArtistC", albumDir);
            var t1 = new DeviceTrack("01.wav", "ArtistC", "AlbumC", track1Path);
            var t2 = new DeviceTrack("02.wav", "ArtistC", "AlbumC", track2Path);

            using var cts = new CancellationTokenSource();
            var progress = new SyncTestProgress<TransferProgressInfo>(info =>
            {
                if (info.CurrentFileName == "02.wav" && info.PhaseText == "取り込み中...")
                {
                    cts.Cancel();
                }
            });

            // Act
            var summary = await sut.ImportTracksToPcAsync(new[] { (album, t1), (album, t2) }, destDir, progress, cts.Token);

            // Assert
            Assert.True(summary.WasCancelled);
            Assert.Equal(1, summary.SuccessCount);
            Assert.True(File.Exists(Path.Combine(destDir, "ArtistC", "AlbumC", "01.wav")));
            Assert.False(File.Exists(Path.Combine(destDir, "ArtistC", "AlbumC", "02.wav")));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// CancelImport(suppressResultDialog: true) を呼び出した場合、結果通知およびライブラリ再反映が抑止され、
    /// ActiveImportTask で進行中バックグラウンドタスクの完了を追跡できることを検証します
    /// </summary>
    [Fact]
    public async Task CancelImport_抑止フラグ有効時_結果通知とライブラリ再反映が抑止されActiveImportTaskで完了追跡できること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_ReverseSuppress_{Guid.NewGuid():N}");
        string pcLibraryDir = Path.Combine(tempRoot, "PcLibrary");
        Directory.CreateDirectory(pcLibraryDir);

        using var downloadStarted = new ManualResetEventSlim(false);
        using var allowDownloadFinish = new ManualResetEventSlim(false);

        var fakeMtp = new FakeImportMtpClient();
        fakeMtp.Connect();
        fakeMtp.AddFile(@"\Music\ArtistS\AlbumS\01.mp3", "track-1");
        fakeMtp.AddFile(@"\Music\ArtistS\AlbumS\02.mp3", "track-2");
        fakeMtp.OnDownloadFile = (src, _) =>
        {
            if (src.EndsWith("02.mp3", StringComparison.OrdinalIgnoreCase))
            {
                downloadStarted.Set();
                allowDownloadFinish.Wait(TimeSpan.FromSeconds(5));
            }
        };

        int resultDialogCalls = 0;
        int refreshCalls = 0;

        try
        {
            var device = new MainViewModel.DeviceViewModel
            {
                Name = "Fake MTP",
                Type = MainViewModel.DeviceType.MTP,
                RootPath = @"\"
            };

            using var sut = new DeviceManagerViewModel(
                device,
                @"\Music",
                new List<Album>(),
                settingsService: new FakeImportSettingsService(pcLibraryDir),
                onLibraryRefreshRequested: _ => refreshCalls++,
                resolveMtpClient: _ => fakeMtp,
                selectDestinationFolder: _ => pcLibraryDir,
                showImportResult: _ => resultDialogCalls++);
            await sut.InitialLoadTask;

            var album = new DeviceAlbum("AlbumS", "ArtistS", @"\Music\ArtistS\AlbumS");
            album.Tracks.Add(new DeviceTrack("01.mp3", "ArtistS", "AlbumS", @"\Music\ArtistS\AlbumS\01.mp3"));
            album.Tracks.Add(new DeviceTrack("02.mp3", "ArtistS", "AlbumS", @"\Music\ArtistS\AlbumS\02.mp3"));

            // Act
            var importTask = sut.ImportAlbumAsync(album);
            Assert.True(downloadStarted.Wait(TimeSpan.FromSeconds(5)));

            Task? trackedTask = sut.ActiveImportTask;
            Assert.NotNull(trackedTask);
            Assert.False(trackedTask!.IsCompleted);

            sut.CancelImport(suppressResultDialog: true);
            allowDownloadFinish.Set();

            var summary = await importTask;
            await trackedTask;

            // Assert
            Assert.NotNull(summary);
            Assert.True(summary!.WasCancelled);
            Assert.Equal(1, summary.SuccessCount);
            Assert.Equal(0, resultDialogCalls);
            Assert.Equal(0, refreshCalls);
            Assert.Null(sut.ActiveImportTask);
        }
        finally
        {
            allowDownloadFinish.Set();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class SyncTestProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncTestProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    private sealed class FakeImportSettingsService : ISettingsService
    {
        private AppSettings _settings;

        public FakeImportSettingsService(string lastLibraryPath)
        {
            _settings = new AppSettings { LastLibraryPath = lastLibraryPath };
        }

        public AppSettings LoadSettings() => _settings;

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public void SaveSettings(AppSettings settings) => _settings = settings;

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        public void UpdateSettings(Action<AppSettings> updateAction) => updateAction(_settings);

        public Task UpdateSettingsAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
        {
            updateAction(_settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeImportMtpClient : DeviceBrowserViewModel.IMtpDeviceClient
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private bool _isConnected;

        public Action<string, Stream>? OnDownloadFile { get; set; }

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                {
                    return _isConnected;
                }
            }
        }

        public void Connect()
        {
            lock (_sync)
            {
                _isConnected = true;
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                _isConnected = false;
            }
        }

        public void AddFile(string path, string content)
        {
            lock (_sync)
            {
                _files[path] = content;
            }
        }

        public bool DirectoryExists(string path) => true;

        public void CreateDirectory(string path)
        {
        }

        public IEnumerable<string> GetDirectories(string path) => Array.Empty<string>();

        public IEnumerable<string> GetFiles(string path) => Array.Empty<string>();

        public bool FileExists(string path)
        {
            lock (_sync)
            {
                return _files.ContainsKey(path);
            }
        }

        public void DeleteFile(string path)
        {
            lock (_sync)
            {
                _files.Remove(path);
            }
        }

        public void UploadFile(string sourceFilePath, string destinationFilePath)
        {
        }

        public void DownloadFile(string sourceFilePath, Stream outputStream)
        {
            OnDownloadFile?.Invoke(sourceFilePath, outputStream);
            string content;
            lock (_sync)
            {
                if (!_files.TryGetValue(sourceFilePath, out content!))
                {
                    throw new FileNotFoundException("MTP file not found.", sourceFilePath);
                }
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            outputStream.Write(bytes, 0, bytes.Length);
        }
    }
}
