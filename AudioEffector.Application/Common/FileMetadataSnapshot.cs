using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// ファイルの存在有無・最終更新日時（UTC）・バイトサイズを表す不変スナップショット
/// </summary>
/// <param name="Exists">ファイルが存在するかどうか</param>
/// <param name="LastWriteTimeUtc">最終更新日時（UTC）。存在しない場合は <see cref="DateTime.MinValue"/></param>
/// <param name="Length">ファイルサイズ（バイト）。存在しない場合は 0</param>
public readonly record struct FileMetadataSnapshot(bool Exists, DateTime LastWriteTimeUtc, long Length)
{
    /// <summary>
    /// ファイルが存在しない状態を表すスナップショットを取得します
    /// </summary>
    public static FileMetadataSnapshot Missing { get; } = new(false, DateTime.MinValue, 0);
}
