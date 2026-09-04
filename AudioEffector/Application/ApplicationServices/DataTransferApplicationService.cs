using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Domain.Repositories;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// ポータブル機器とのデータ同期、楽曲ファイル転送、デバイス内探索を統括するアプリケーションサービス
/// </summary>
public class DataTransferApplicationService
{
    private readonly IDataTransferRepository _dataTransferRepository;

    /// <summary>
    /// DataTransferApplicationServiceを初期化します
    /// </summary>
    /// <param name="dataTransferRepository">データ転送リポジトリ</param>
    public DataTransferApplicationService(IDataTransferRepository dataTransferRepository)
    {
        _dataTransferRepository = dataTransferRepository ?? throw new ArgumentNullException(nameof(dataTransferRepository));
    }

    /// <summary>
    /// ポータブル機器またはリムーバブルドライブが接続されているかを確認します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>接続されている場合はtrue</returns>
    public async Task<bool> IsDeviceConnectedAsync(CancellationToken cancellationToken = default)
    {
        return await _dataTransferRepository.IsDeviceConnectedAsync(cancellationToken);
    }

    /// <summary>
    /// デバイス上のすべての楽曲トラック一覧を取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デバイストラックのリスト</returns>
    public async Task<IReadOnlyList<DeviceTrack>> GetDeviceTracksAsync(CancellationToken cancellationToken = default)
    {
        return await _dataTransferRepository.GetDeviceTracksAsync(cancellationToken);
    }

    /// <summary>
    /// デバイス上のアルバムフォルダ一覧を取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デバイスアルバムのリスト</returns>
    public async Task<IReadOnlyList<DeviceAlbum>> GetDeviceAlbumsAsync(CancellationToken cancellationToken = default)
    {
        return await _dataTransferRepository.GetDeviceAlbumsAsync(cancellationToken);
    }

    /// <summary>
    /// 選択されたトラックコレクションをデバイスの指定先へ順次転送します
    /// </summary>
    /// <param name="tracks">転送対象トラックのコレクション</param>
    /// <param name="destinationFolder">転送先フォルダパス</param>
    /// <param name="progress">全体進捗通知（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>成功した転送ファイル数</returns>
    public async Task<int> TransferTracksAsync(
        IEnumerable<Track> tracks,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var trackList = new List<Track>(tracks);
        if (trackList.Count == 0) return 0;

        int successCount = 0;
        int total = trackList.Count;

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var track = trackList[i];
            bool success = await _dataTransferRepository.TransferTrackAsync(
                track.FilePath,
                destinationFolder,
                null,
                cancellationToken);

            if (success)
            {
                successCount++;
            }

            progress?.Report((double)(i + 1) / total);
        }

        return successCount;
    }

    /// <summary>
    /// デバイス上の指定されたトラックを削除します
    /// </summary>
    /// <param name="deviceFilePath">削除対象ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除成功時はtrue</returns>
    public async Task<bool> DeleteDeviceTrackAsync(string deviceFilePath, CancellationToken cancellationToken = default)
    {
        return await _dataTransferRepository.DeleteDeviceTrackAsync(deviceFilePath, cancellationToken);
    }
}
