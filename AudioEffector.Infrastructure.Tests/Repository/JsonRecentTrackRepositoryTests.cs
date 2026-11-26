using System;
using System.IO;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

/// <summary>
/// JsonRecentTrackRepositoryの保存・読み込み・破損JSONフォールバックを検証するテストクラス
/// </summary>
public sealed class JsonRecentTrackRepositoryTests
{
    /// <summary>
    /// 再生履歴エントリを保存した後に読み込むと、同じ順序・メタデータ・再生日時で復元されることを検証します。
    /// </summary>
    [Fact]
    public void SaveAndLoad_有効な履歴エントリ保存時_同じ内容で復元される()
    {
        // Arrange
        string tempPath = Path.Combine(Path.GetTempPath(), $"recent_test_{Guid.NewGuid():N}.json");
        try
        {
            var sut = new JsonRecentTrackRepository(tempPath);
            var playedAt = new DateTime(2026, 9, 11, 21, 15, 0);
            var track = new Track
            {
                FilePath = @"C:\music\m83\midnight_city.flac",
                Title = "Midnight City",
                Artist = "M83",
                Album = "Hurry Up",
                Duration = TimeSpan.FromSeconds(243),
            };
            var entries = new[] { new RecentTrackEntry(track, playedAt) };

            // Act
            sut.Save(entries);
            var loaded = sut.Load();

            // Assert
            Assert.Single(loaded);
            Assert.Equal(@"C:\music\m83\midnight_city.flac", loaded[0].FilePath);
            Assert.Equal("Midnight City", loaded[0].Title);
            Assert.Equal("M83", loaded[0].Artist);
            Assert.Equal("2026/09/11 21:15", loaded[0].PlayedAtDisplay);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 保存ファイルが破損している場合でも例外を送出せず空リストを返すことを検証します。
    /// </summary>
    [Fact]
    public void Load_破損したJSONファイルの場合_空リストを返す()
    {
        // Arrange
        string tempPath = Path.Combine(Path.GetTempPath(), $"recent_broken_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempPath, "{ broken json");
            var sut = new JsonRecentTrackRepository(tempPath);

            // Act
            var loaded = sut.Load();

            // Assert
            Assert.Empty(loaded);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// JSON配列内にnull要素やファイルパス欠損要素が混入していても、不正要素のみスキップし有効な履歴を復元することを検証します。
    /// </summary>
    [Fact]
    public void Load_配列内にnull要素が混入している場合_有効な履歴のみ復元される()
    {
        // Arrange
        string tempPath = Path.Combine(Path.GetTempPath(), $"recent_null_item_{Guid.NewGuid():N}.json");
        try
        {
            string json = """
                [
                  null,
                  {
                    "FilePath": "",
                    "Title": "Invalid",
                    "Artist": "Unknown",
                    "Album": "Unknown",
                    "DurationSeconds": 100,
                    "PlayedAt": "2026-09-11T21:00:00"
                  },
                  {
                    "FilePath": "C:\\music\\daft\\get_lucky.flac",
                    "Title": "Get Lucky",
                    "Artist": "Daft Punk",
                    "Album": "RAM",
                    "DurationSeconds": 248,
                    "PlayedAt": "2026-09-11T21:15:00"
                  }
                ]
                """;
            File.WriteAllText(tempPath, json);
            var sut = new JsonRecentTrackRepository(tempPath);

            // Act
            var loaded = sut.Load();

            // Assert
            Assert.Single(loaded);
            Assert.Equal(@"C:\music\daft\get_lucky.flac", loaded[0].FilePath);
            Assert.Equal("Get Lucky", loaded[0].Title);
            Assert.Equal("Daft Punk", loaded[0].Artist);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
