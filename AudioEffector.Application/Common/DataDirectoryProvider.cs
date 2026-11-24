using System;
using System.IO;

namespace AudioEffector.Application.Common;

/// <summary>
/// 永続化データディレクトリを解決する標準プロバイダ実装
/// </summary>
public sealed class DataDirectoryProvider : IDataDirectoryProvider
{
    /// <summary>
    /// 指定されたデータディレクトリ（未指定時は既定の %APPDATA%\AudioEffector）で初期化します
    /// </summary>
    /// <param name="dataDirectory">明示指定するデータディレクトリ。null または空白の場合は既定ディレクトリを使用します</param>
    public DataDirectoryProvider(string? dataDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            DataDirectory = Path.Combine(appData, "AudioEffector");
            IsCustomDirectory = false;
        }
        else
        {
            DataDirectory = Path.GetFullPath(dataDirectory.Trim());
            IsCustomDirectory = true;
        }

        Directory.CreateDirectory(DataDirectory);
    }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <inheritdoc />
    public bool IsCustomDirectory { get; }

    /// <inheritdoc />
    public string GetFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(DataDirectory, fileName);
    }
}
