using System;
using System.IO;

namespace AudioEffector.Application.Common;

/// <summary>
/// 永続化データディレクトリを解決する標準プロバイダ実装
/// </summary>
public sealed class DataDirectoryProvider : IDataDirectoryProvider
{
    private readonly object _sync = new();
    private string _dataDirectory;
    private bool _isCustomDirectory;

    /// <summary>
    /// 指定されたデータディレクトリ（未指定時は既定の %APPDATA%\AudioEffector）で初期化します
    /// </summary>
    /// <param name="dataDirectory">明示指定するデータディレクトリ。null または空白の場合は既定ディレクトリを使用します</param>
    public DataDirectoryProvider(string? dataDirectory = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DefaultDataDirectory = NormalizeDirectoryPath(Path.Combine(appData, "AudioEffector"));

        (string resolved, bool isCustom) = ResolveDirectory(dataDirectory, DefaultDataDirectory);
        _dataDirectory = resolved;
        _isCustomDirectory = isCustom;

        Directory.CreateDirectory(_dataDirectory);
    }

    /// <inheritdoc />
    public string DefaultDataDirectory { get; }

    /// <inheritdoc />
    public string DataDirectory
    {
        get
        {
            lock (_sync)
            {
                return _dataDirectory;
            }
        }
    }

    /// <inheritdoc />
    public bool IsCustomDirectory
    {
        get
        {
            lock (_sync)
            {
                return _isCustomDirectory;
            }
        }
    }

    /// <inheritdoc />
    public event Action<string>? DataDirectoryChanged;

    /// <inheritdoc />
    public string GetFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(DataDirectory, fileName);
    }

    /// <inheritdoc />
    public void UpdateDataDirectory(string? dataDirectory)
    {
        (string resolved, bool isCustom) = ResolveDirectory(dataDirectory, DefaultDataDirectory);
        Directory.CreateDirectory(resolved);

        bool changed;
        lock (_sync)
        {
            changed = !string.Equals(_dataDirectory, resolved, StringComparison.OrdinalIgnoreCase)
                || _isCustomDirectory != isCustom;
            _dataDirectory = resolved;
            _isCustomDirectory = isCustom;
        }

        if (changed)
        {
            DataDirectoryChanged?.Invoke(resolved);
        }
    }

    /// <summary>
    /// ディレクトリパスを絶対パスへ展開し、ルート以外では末尾セパレータを除去した正規化パスを返します
    /// </summary>
    /// <param name="path">正規化対象のディレクトリパス</param>
    /// <returns>正規化済みディレクトリパス</returns>
    internal static string NormalizeDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path.Trim());
        string? root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static (string ResolvedPath, bool IsCustom) ResolveDirectory(string? dataDirectory, string defaultDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return (defaultDirectory, false);
        }

        string normalized = NormalizeDirectoryPath(dataDirectory);
        if (string.Equals(normalized, defaultDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return (defaultDirectory, false);
        }

        return (normalized, true);
    }
}
