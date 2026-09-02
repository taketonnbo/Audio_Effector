using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Repositories;

/// <summary>
/// ポータブルデバイス（MTP）との通信および楽曲転送を担当するリポジトリインターフェース
/// </summary>
public interface IDataTransferRepository
{
    /// <summary>
    /// デバイスが接続されているかどうかを確認します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>接続されている場合はtrue</returns>
    Task<bool> IsDeviceConnectedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// デバイス上のすべての楽曲トラック一覧を取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デバイストラック一覧</returns>
    Task<IReadOnlyList<DeviceTrack>> GetDeviceTracksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// デバイス上のアルバムフォルダ一覧を取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デバイスアルバム一覧</returns>
    Task<IReadOnlyList<DeviceAlbum>> GetDeviceAlbumsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたローカルファイルをデバイスの目的フォルダへ転送します
    /// </summary>
    /// <param name="sourceFilePath">転送元ローカルファイルパス</param>
    /// <param name="destinationFolder">転送先デバイスフォルダパス</param>
    /// <param name="progress">進捗通知（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>転送成功時はtrue</returns>
    Task<bool> TransferTrackAsync(
        AudioPath sourceFilePath,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// デバイス上の指定ファイルを削除します
    /// </summary>
    /// <param name="deviceFilePath">デバイス上のファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除成功時はtrue</returns>
    Task<bool> DeleteDeviceTrackAsync(string deviceFilePath, CancellationToken cancellationToken = default);
}
