using System;
using System.IO;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using Moq;
using Xunit;

namespace AudioEffector.Application.Tests.ApplicationServices;

/// <summary>
/// <see cref="DataDirectoryMigrationService"/> のデータ移行モード（Copy / Move / None）、settings.json 除外、および異常系ロールバックを検証するテストクラス
/// </summary>
public sealed class DataDirectoryMigrationServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public DataDirectoryMigrationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"AudioEffector_DataMigration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    private static IJsonFileLockHelper CreateSimpleFileLockHelper()
    {
        var mock = new Mock<IJsonFileLockHelper>();
        mock.Setup(h => h.AcquireLock(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Returns(Mock.Of<IDisposable>());
        mock.Setup(h => h.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) =>
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, content);
            });
        return mock.Object;
    }

    /// <summary>
    /// Copy モードで保存先を変更した際、管理対象 5 ファイルのみが新先へコピーされ settings.json はコピーされず元ファイルも保持されることを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_Copyモード指定時_管理対象ファイルがコピーされsettingsJsonは除外され元ファイルが保持されること()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "Source");
        string targetDir = Path.Combine(_tempRoot, "Target");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "playlists.json"), "[\"p1\"]");
        File.WriteAllText(Path.Combine(sourceDir, "favorites.json"), "[\"f1\"]");
        File.WriteAllText(Path.Combine(sourceDir, "presets.json"), "[\"eq1\"]");
        File.WriteAllText(Path.Combine(sourceDir, "settings.json"), "{\"Theme\":0}");

        var provider = new DataDirectoryProvider(sourceDir);
        var persistedSettings = new AppSettings { DataDirectoryPath = sourceDir };
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(persistedSettings));

        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.Copy);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(targetDir, result.EffectivePath);
        Assert.Equal(3, result.MigratedFileCount);
        Assert.Equal(targetDir, provider.DataDirectory);
        Assert.Equal(targetDir, persistedSettings.DataDirectoryPath);
        Assert.True(File.Exists(Path.Combine(targetDir, "playlists.json")));
        Assert.True(File.Exists(Path.Combine(targetDir, "favorites.json")));
        Assert.True(File.Exists(Path.Combine(targetDir, "presets.json")));
        Assert.False(File.Exists(Path.Combine(targetDir, "settings.json")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "playlists.json")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "settings.json")));
    }

    /// <summary>
    /// Move モードで保存先を変更した際、管理対象ファイルが新先へ移動されて元ファイルが削除され、settings.json は元フォルダに残ることを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_Moveモード指定時_管理対象ファイルが移動し元ファイルが削除されること()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceMove");
        string targetDir = Path.Combine(_tempRoot, "TargetMove");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "tracks.json"), "[{\"Title\":\"SongA\"}]");
        File.WriteAllText(Path.Combine(sourceDir, "RecentTracks.json"), "[]");
        File.WriteAllText(Path.Combine(sourceDir, "settings.json"), "{\"Theme\":1}");

        var provider = new DataDirectoryProvider(sourceDir);
        var persistedSettings = new AppSettings { DataDirectoryPath = sourceDir };
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(persistedSettings));

        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.Move);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(2, result.MigratedFileCount);
        Assert.Null(result.ErrorMessage);
        Assert.True(File.Exists(Path.Combine(targetDir, "tracks.json")));
        Assert.True(File.Exists(Path.Combine(targetDir, "RecentTracks.json")));
        Assert.False(File.Exists(Path.Combine(sourceDir, "tracks.json")));
        Assert.False(File.Exists(Path.Combine(sourceDir, "RecentTracks.json")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "settings.json")));
    }

    /// <summary>
    /// None モード指定時はファイルをコピーせず保存先パスと設定のみ切り替わることを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_Noneモード指定時_ファイル移行せず保存先のみ切り替わること()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceNone");
        string targetDir = Path.Combine(_tempRoot, "TargetNone");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "playlists.json"), "[\"p1\"]");

        var provider = new DataDirectoryProvider(sourceDir);
        var persistedSettings = new AppSettings();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(persistedSettings));

        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.None);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(0, result.MigratedFileCount);
        Assert.Equal(targetDir, provider.DataDirectory);
        Assert.Equal(targetDir, persistedSettings.DataDirectoryPath);
        Assert.False(File.Exists(Path.Combine(targetDir, "playlists.json")));
        Assert.True(File.Exists(Path.Combine(sourceDir, "playlists.json")));
    }

    /// <summary>
    /// 現在の保存先と同一のディレクトリを指定した場合、変更なし（Changed=false）として安全に終了することを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_同一ディレクトリ指定時_変更なしで成功を返すこと()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SameDir");
        var provider = new DataDirectoryProvider(sourceDir);
        var settingsMock = new Mock<ISettingsService>();
        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory(sourceDir + Path.DirectorySeparatorChar, DataMigrationMode.Copy);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.Changed);
        settingsMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Never);
    }

    /// <summary>
    /// settings.json のみが存在するディレクトリでは HasExistingDataFiles が false を返すことを検証します。
    /// </summary>
    [Fact]
    public void HasExistingDataFiles_settingsJsonのみ存在する場合_Falseを返すこと()
    {
        // Arrange
        string dir = Path.Combine(_tempRoot, "SettingsOnly");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");

        var provider = new DataDirectoryProvider(dir);
        var sut = new DataDirectoryMigrationService(provider, Mock.Of<ISettingsService>(), CreateSimpleFileLockHelper());

        // Act
        bool hasData = sut.HasExistingDataFiles(dir);

        // Assert
        Assert.False(hasData);
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
