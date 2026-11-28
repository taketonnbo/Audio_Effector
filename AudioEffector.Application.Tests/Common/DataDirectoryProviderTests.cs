using System;
using System.IO;
using AudioEffector.Application.Common;
using Xunit;

namespace AudioEffector.Application.Tests.Common;

/// <summary>
/// <see cref="DataDirectoryProvider"/> の既定パス解決、動的切替、既定復帰および変更イベント発火を検証するテストクラス
/// </summary>
public sealed class DataDirectoryProviderTests : IDisposable
{
    private readonly string _tempRoot;

    public DataDirectoryProviderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"AudioEffector_DataDirProvider_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// カスタムディレクトリ指定時に絶対パスが正規化され、IsCustomDirectory が true かつ GetFilePath が配下パスを返すことを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_カスタムディレクトリ指定時_正規化パスとIsCustomDirectoryTrueを返すこと()
    {
        // Arrange
        string customDir = Path.Combine(_tempRoot, "CustomData") + Path.DirectorySeparatorChar;

        // Act
        var sut = new DataDirectoryProvider(customDir);

        // Assert
        Assert.True(sut.IsCustomDirectory);
        Assert.Equal(Path.Combine(_tempRoot, "CustomData"), sut.DataDirectory);
        Assert.Equal(Path.Combine(_tempRoot, "CustomData", "playlists.json"), sut.GetFilePath("playlists.json"));
        Assert.True(Directory.Exists(sut.DataDirectory));
    }

    /// <summary>
    /// UpdateDataDirectory でカスタムパスへ変更および null で既定へ復帰した際、プロパティと変更イベントが期待通り動作することを検証します。
    /// </summary>
    [Fact]
    public void UpdateDataDirectory_カスタム切替と既定復帰時_パスとイベント通知が正しく更新されること()
    {
        // Arrange
        string dirA = Path.Combine(_tempRoot, "DirA");
        string dirB = Path.Combine(_tempRoot, "DirB");
        var sut = new DataDirectoryProvider(dirA);
        string? lastNotified = null;
        int notifyCount = 0;
        sut.DataDirectoryChanged += path =>
        {
            notifyCount++;
            lastNotified = path;
        };

        // Act
        sut.UpdateDataDirectory(dirB);
        sut.UpdateDataDirectory(dirB + Path.DirectorySeparatorChar); // 同パス再指定ではイベント再発火しない
        string pathAfterB = sut.DataDirectory;
        bool isCustomAfterB = sut.IsCustomDirectory;
        int countAfterB = notifyCount;

        sut.UpdateDataDirectory(null);

        // Assert
        Assert.Equal(dirB, pathAfterB);
        Assert.True(isCustomAfterB);
        Assert.Equal(1, countAfterB);
        Assert.Equal(sut.DefaultDataDirectory, sut.DataDirectory);
        Assert.False(sut.IsCustomDirectory);
        Assert.Equal(2, notifyCount);
        Assert.Equal(sut.DefaultDataDirectory, lastNotified);
    }

    /// <summary>
    /// 既定ディレクトリと同一のパスを明示指定した場合、IsCustomDirectory が false へ正規化されることを検証します。
    /// </summary>
    [Fact]
    public void UpdateDataDirectory_既定ディレクトリと同一パス指定時_IsCustomDirectoryがFalseへ正規化されること()
    {
        // Arrange
        string dirA = Path.Combine(_tempRoot, "DirA");
        var sut = new DataDirectoryProvider(dirA);

        // Act
        sut.UpdateDataDirectory(sut.DefaultDataDirectory + Path.DirectorySeparatorChar);

        // Assert
        Assert.False(sut.IsCustomDirectory);
        Assert.Equal(sut.DefaultDataDirectory, sut.DataDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // 一時ディレクトリ削除失敗はテスト結果へ影響させない
            }
        }
    }
}
