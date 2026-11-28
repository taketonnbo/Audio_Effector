using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Infrastructure.DataTransfer;

/// <summary>
/// ポータブルオーディオ機器およびリムーバブルストレージとの通信・楽曲データ転送を行うアダプタークラス
/// </summary>
public class MtpDataTransferAdapter : IDataTransferRepository
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// ポータブルデバイスまたはリムーバブルドライブが接続されているかを確認します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>接続されている場合はtrue</returns>
    public Task<bool> IsDeviceConnectedAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .ToList();
                return drives.Count > 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "デバイス接続状態の確認中にエラーが発生しました");
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// デバイス上の楽曲トラック一覧を非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デバイストラックのリスト</returns>
    public Task<IReadOnlyList<DeviceTrack>> GetDeviceTracksAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new List<DeviceTrack>();
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady);

                foreach (var drive in drives)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var files = Directory.EnumerateFiles(drive.RootDirectory.FullName, "*.*", SearchOption.AllDirectories)
                            .Where(f => IsAudioFile(f));

                        foreach (var file in files)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            var fileInfo = new FileInfo(file);
                            result.Add(new DeviceTrack(
                                title: Path.GetFileNameWithoutExtension(file),
                                artist: "Unknown Artist",
                                album: Path.GetFileName(Path.GetDirectoryName(file) ?? "Unknown"),
                                path: file,
                                fileSizeBytes: fileInfo.Length));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"ドライブ {drive.Name} のスキャン中にエラーが発生しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "デバイストラック一覧の取得中にエラーが発生しました");
            }

            return (IReadOnlyList<DeviceTrack>)result;
        }, cancellationToken);
    }

    /// <summary>
    /// デバイス上のアルバムフォルダ一覧を非同期で取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>デバイスアルバムのリスト</returns>
    public Task<IReadOnlyList<DeviceAlbum>> GetDeviceAlbumsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new List<DeviceAlbum>();
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady);

                foreach (var drive in drives)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var dirs = Directory.EnumerateDirectories(drive.RootDirectory.FullName, "*", SearchOption.TopDirectoryOnly);
                        foreach (var dir in dirs)
                        {
                            result.Add(new DeviceAlbum(
                                title: Path.GetFileName(dir),
                                artist: drive.VolumeLabel,
                                path: dir));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"ドライブ {drive.Name} のアルバム探索中にエラーが発生しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "デバイスアルバム一覧の取得中にエラーが発生しました");
            }

            return (IReadOnlyList<DeviceAlbum>)result;
        }, cancellationToken);
    }

    /// <summary>
    /// 指定されたローカル音声ファイルをデバイスへ転送します
    /// </summary>
    /// <param name="sourceFilePath">転送元ファイルパス</param>
    /// <param name="destinationFolder">転送先フォルダパス</param>
    /// <param name="progress">進捗通知</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>転送成功時はtrue</returns>
    public Task<bool> TransferTrackAsync(
        AudioPath sourceFilePath,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(sourceFilePath.Value))
            {
                Logger.Warn($"転送元ファイルが存在しません: {sourceFilePath.Value}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(destinationFolder) || !Directory.Exists(destinationFolder))
            {
                Logger.Warn($"転送先フォルダが存在しません: {destinationFolder}");
                return false;
            }

            try
            {
                string fileName = Path.GetFileName(sourceFilePath.Value);
                string destFilePath = Path.Combine(destinationFolder, fileName);

                // バッファ転送による進捗通知
                const int BUFFER_SIZE = 64 * 1024;
                var buffer = new byte[BUFFER_SIZE];

                using var sourceStream = new FileStream(sourceFilePath.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var destStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

                long totalBytes = sourceStream.Length;
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        destStream.Dispose();
                        File.Delete(destFilePath);
                        return false;
                    }

                    destStream.Write(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)totalRead / totalBytes);
                    }
                }

                progress?.Report(1.0);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"ファイル {sourceFilePath.Value} の転送中にエラーが発生しました");
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 指定されたローカルの補助ファイル（.lrc 歌詞ファイル等）をデバイスへ転送します
    /// </summary>
    /// <param name="sourceFilePath">転送元ローカルファイルパス</param>
    /// <param name="destinationFolder">転送先フォルダパス</param>
    /// <param name="destinationFileName">転送先ファイル名（省略時は転送元ファイル名を使用）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>転送成功時はtrue</returns>
    public Task<bool> TransferCompanionFileAsync(
        string sourceFilePath,
        string destinationFolder,
        string? destinationFileName = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                Logger.Warn($"転送元の補助ファイルが存在しません: {sourceFilePath}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(destinationFolder) || !Directory.Exists(destinationFolder))
            {
                Logger.Warn($"補助ファイルの転送先フォルダが存在しません: {destinationFolder}");
                return false;
            }

            string targetFileName;
            if (string.IsNullOrWhiteSpace(destinationFileName))
            {
                targetFileName = Path.GetFileName(sourceFilePath);
            }
            else
            {
                string trimmedName = destinationFileName.Trim();
                string fileNameOnly = Path.GetFileName(trimmedName);
                if (trimmedName is "." or ".."
                    || !string.Equals(trimmedName, fileNameOnly, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(fileNameOnly))
                {
                    Logger.Warn($"不正な補助ファイル名が指定されました: {destinationFileName}");
                    return false;
                }

                targetFileName = fileNameOnly;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullDestDir = Path.GetFullPath(destinationFolder);
                string destDirPrefix = fullDestDir.EndsWith(Path.DirectorySeparatorChar)
                    ? fullDestDir
                    : fullDestDir + Path.DirectorySeparatorChar;
                string destFilePath = Path.GetFullPath(Path.Combine(fullDestDir, targetFileName));
                if (!destFilePath.StartsWith(destDirPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn($"転送先フォルダ外への補助ファイル書き込みを拒否しました: {destinationFileName}");
                    return false;
                }

                File.Copy(sourceFilePath, destFilePath, overwrite: true);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"補助ファイル {sourceFilePath} の転送中にエラーが発生しました");
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// デバイス上の指定ファイルを PC 側の保存先パスへ非同期ダウンロード（逆転送）します
    /// </summary>
    /// <param name="deviceFilePath">デバイス上の転送元ファイルパス</param>
    /// <param name="destinationFilePath">PC 側の保存先フルファイルパス</param>
    /// <param name="progress">進捗通知（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ダウンロード成功時はtrue</returns>
    public async Task<bool> DownloadTrackFromDeviceAsync(
        string deviceFilePath,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceFilePath) || !File.Exists(deviceFilePath))
        {
            Logger.Warn($"逆転送元のデバイスファイルが存在しません: {deviceFilePath}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationFilePath))
        {
            Logger.Warn("逆転送先の保存ファイルパスが空です。");
            return false;
        }

        string? parentDir = Path.GetDirectoryName(destinationFilePath);
        if (string.IsNullOrWhiteSpace(parentDir))
        {
            Logger.Warn($"逆転送先の親ディレクトリを解決できません: {destinationFilePath}");
            return false;
        }

        if (File.Exists(destinationFilePath))
        {
            Logger.Warn($"逆転送先に同名ファイルが既に存在するため上書きを中止します: {destinationFilePath}");
            return false;
        }

        const int BUFFER_SIZE = 64 * 1024;
        var buffer = new byte[BUFFER_SIZE];
        bool destCreated = false;
        bool completed = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            await using var sourceStream = new FileStream(deviceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, useAsync: true);
            await using var destStream = new FileStream(destinationFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_SIZE, useAsync: true);
            destCreated = true;

            long totalBytes = sourceStream.Length;
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    progress?.Report((double)totalRead / totalBytes);
                }
            }

            await destStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            completed = true;
            progress?.Report(1.0);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"デバイスファイル {deviceFilePath} の逆転送中にエラーが発生しました");
            return false;
        }
        finally
        {
            if (!completed && destCreated)
            {
                try
                {
                    if (File.Exists(destinationFilePath))
                    {
                        File.Delete(destinationFilePath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    Logger.Warn(cleanupEx, $"不完全な逆転送ファイルの削除に失敗しました: {destinationFilePath}");
                }
            }
        }
    }

    /// <summary>
    /// デバイス上の指定ファイルを削除します
    /// </summary>
    /// <param name="deviceFilePath">削除対象ファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除成功時はtrue</returns>
    public Task<bool> DeleteDeviceTrackAsync(string deviceFilePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(deviceFilePath))
            {
                return false;
            }

            try
            {
                File.Delete(deviceFilePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"デバイスファイル {deviceFilePath} の削除中にエラーが発生しました");
                return false;
            }
        }, cancellationToken);
    }

    private static bool IsAudioFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".wma" or ".ogg" or ".alac";
    }
}
