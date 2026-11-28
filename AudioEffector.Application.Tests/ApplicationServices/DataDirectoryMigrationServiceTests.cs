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

    /// <summary>
    /// 無効なパス文字を含むディレクトリを指定した場合、変更されず失敗結果を返すことを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_無効パス指定時_設定変更されず失敗を返すこと()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceInvalid");
        var provider = new DataDirectoryProvider(sourceDir);
        var settingsMock = new Mock<ISettingsService>();
        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory("Z:\0invalid\\path", DataMigrationMode.Copy);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(sourceDir, result.EffectivePath);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        settingsMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Never);
    }

    /// <summary>
    /// 複数ファイルのコピー途中で IOException が発生した場合、新規作成ファイルが削除され既存上書きファイルが元内容へロールバックされることを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_コピー途中失敗時_新規ファイル削除と既存ファイル復元へロールバックされること()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceRollback");
        string targetDir = Path.Combine(_tempRoot, "TargetRollback");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(sourceDir, "tracks.json"), "[\"new-track\"]");
        File.WriteAllText(Path.Combine(sourceDir, "playlists.json"), "[\"new-playlist\"]");
        File.WriteAllText(Path.Combine(sourceDir, "favorites.json"), "[\"new-fav\"]");
        File.WriteAllText(Path.Combine(targetDir, "tracks.json"), "[\"original-target-track\"]");

        var provider = new DataDirectoryProvider(sourceDir);
        var settingsMock = new Mock<ISettingsService>();

        var lockHelperMock = new Mock<IJsonFileLockHelper>();
        lockHelperMock.Setup(h => h.AcquireLock(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Returns(Mock.Of<IDisposable>());
        lockHelperMock.Setup(h => h.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) =>
            {
                if (path.EndsWith("favorites.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("disk full");
                }

                File.WriteAllText(path, content);
            });

        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, lockHelperMock.Object);

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.Copy);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(sourceDir, provider.DataDirectory);
        Assert.Equal("[\"original-target-track\"]", File.ReadAllText(Path.Combine(targetDir, "tracks.json")));
        Assert.False(File.Exists(Path.Combine(targetDir, "playlists.json")));
        Assert.False(File.Exists(Path.Combine(targetDir, "favorites.json")));
        Assert.Empty(Directory.GetFiles(targetDir, "*.bak_*"));
        settingsMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Never);
    }

    /// <summary>
    /// UpdateSettings がロックタイムアウト例外を送出した場合、コピー済みファイルがロールバックされ失敗結果が返ることを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_UpdateSettings失敗時_コピー済みファイルがロールバックされ失敗を返すこと()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceSettingsFail");
        string targetDir = Path.Combine(_tempRoot, "TargetSettingsFail");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "playlists.json"), "[\"p1\"]");

        var provider = new DataDirectoryProvider(sourceDir);
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Throws(new FileLockTimeoutException("settings.json", TimeSpan.FromSeconds(5)));

        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.Copy);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(sourceDir, provider.DataDirectory);
        Assert.False(File.Exists(Path.Combine(targetDir, "playlists.json")));
    }

    /// <summary>
    /// UpdateDataDirectory が例外を送出した場合、settings.json の値が元へ戻されコピーファイルもロールバックされることを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_UpdateDataDirectory失敗時_設定値とファイルがロールバックされること()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceProviderFail");
        string targetDir = Path.Combine(_tempRoot, "TargetProviderFail");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "playlists.json"), "[\"p1\"]");

        var providerMock = new Mock<IDataDirectoryProvider>();
        providerMock.SetupGet(p => p.DefaultDataDirectory).Returns(Path.Combine(_tempRoot, "DefaultDir"));
        providerMock.SetupGet(p => p.DataDirectory).Returns(sourceDir);
        providerMock.SetupGet(p => p.IsCustomDirectory).Returns(true);
        providerMock.Setup(p => p.UpdateDataDirectory(It.IsAny<string?>()))
            .Throws(new IOException("switch failed"));

        var persistedSettings = new AppSettings { DataDirectoryPath = sourceDir };
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(persistedSettings));

        var sut = new DataDirectoryMigrationService(providerMock.Object, settingsMock.Object, CreateSimpleFileLockHelper());

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.Copy);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(sourceDir, persistedSettings.DataDirectoryPath);
        Assert.False(File.Exists(Path.Combine(targetDir, "playlists.json")));
    }

    /// <summary>
    /// Move モードでコピーと切替完了後に元ファイル削除のみ失敗した場合、切替成功を維持しつつ警告メッセージを返すことを検証します。
    /// </summary>
    [Fact]
    public void ChangeDataDirectory_Moveモードで元ファイル削除失敗時_切替成功を維持し警告メッセージを返すこと()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempRoot, "SourceMoveWarn");
        string targetDir = Path.Combine(_tempRoot, "TargetMoveWarn");
        Directory.CreateDirectory(sourceDir);
        string sourcePlaylist = Path.Combine(sourceDir, "playlists.json");
        File.WriteAllText(sourcePlaylist, "[\"p1\"]");

        var provider = new DataDirectoryProvider(sourceDir);
        var persistedSettings = new AppSettings { DataDirectoryPath = sourceDir };
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(persistedSettings));

        int sourceLockCalls = 0;
        var lockHelperMock = new Mock<IJsonFileLockHelper>();
        lockHelperMock.Setup(h => h.AcquireLock(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .Returns<string, TimeSpan?>((path, _) =>
            {
                if (string.Equals(path, sourcePlaylist, StringComparison.OrdinalIgnoreCase))
                {
                    sourceLockCalls++;
                    if (sourceLockCalls >= 2)
                    {
                        throw new IOException("file in use");
                    }
                }

                return Mock.Of<IDisposable>();
            });
        lockHelperMock.Setup(h => h.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => File.WriteAllText(path, content));

        var sut = new DataDirectoryMigrationService(provider, settingsMock.Object, lockHelperMock.Object);

        // Act
        var result = sut.ChangeDataDirectory(targetDir, DataMigrationMode.Move);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Equal(targetDir, provider.DataDirectory);
        Assert.True(File.Exists(Path.Combine(targetDir, "playlists.json")));
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("playlists.json", result.ErrorMessage);
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
