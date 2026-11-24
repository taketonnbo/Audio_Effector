namespace AudioEffector.Application.Common;

/// <summary>
/// 永続化JSONファイル群を格納するデータディレクトリの解決を提供するインターフェース
/// </summary>
public interface IDataDirectoryProvider
{
    /// <summary>
    /// 解決されたデータディレクトリの絶対パスを取得します
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// 既定ディレクトリではなく明示的なカスタムディレクトリが指定されているかどうかを取得します
    /// </summary>
    bool IsCustomDirectory { get; }

    /// <summary>
    /// 指定されたファイル名をデータディレクトリ配下のフルパスへ結合して返します
    /// </summary>
    /// <param name="fileName">データディレクトリ配下のファイル名</param>
    /// <returns>結合されたファイルパス</returns>
    string GetFilePath(string fileName);
}
