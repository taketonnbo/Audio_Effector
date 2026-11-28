using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// 永続化JSONファイル群を格納するデータディレクトリの解決を提供するインターフェース
/// </summary>
public interface IDataDirectoryProvider
{
    /// <summary>
    /// 既定のデータディレクトリ（%APPDATA%\AudioEffector）の絶対パスを取得します
    /// </summary>
    string DefaultDataDirectory { get; }

    /// <summary>
    /// 解決された現在のデータディレクトリの絶対パスを取得します
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// 既定ディレクトリではなく明示的なカスタムディレクトリが指定されているかどうかを取得します
    /// </summary>
    bool IsCustomDirectory { get; }

    /// <summary>
    /// データディレクトリが動的に変更された際に発火するイベント（引数は変更後の絶対パス）
    /// </summary>
    event Action<string>? DataDirectoryChanged;

    /// <summary>
    /// 指定されたファイル名を現在のデータディレクトリ配下のフルパスへ結合して返します
    /// </summary>
    /// <param name="fileName">データディレクトリ配下のファイル名</param>
    /// <returns>結合されたファイルパス</returns>
    string GetFilePath(string fileName);

    /// <summary>
    /// データディレクトリを実行時に新しいパス（null または空白時は既定ディレクトリ）へ更新します
    /// </summary>
    /// <param name="dataDirectory">新しいデータディレクトリパス（未指定時は既定ディレクトリへ復帰）</param>
    void UpdateDataDirectory(string? dataDirectory);
}
