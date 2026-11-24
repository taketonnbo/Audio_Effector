using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.Common;

/// <summary>
/// 音声ファイルからメタデータおよびカバーアートを抽出するインターフェース
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// 音声ファイルからメタデータを非同期で抽出し、Trackエンティティを生成します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成されたTrackエンティティ（ファイルが存在しない・読み込めない場合はnull）</returns>
    Task<Track?> ExtractMetadataAsync(AudioPath filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 音声ファイルに埋め込まれたカバーアート画像のバイト配列を非同期で抽出します
    /// </summary>
    /// <param name="filePath">音声ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>画像のバイト配列（画像が存在しない・読み込めない場合はnull）</returns>
    Task<byte[]?> ExtractAlbumArtBytesAsync(AudioPath filePath, CancellationToken cancellationToken = default);
}
