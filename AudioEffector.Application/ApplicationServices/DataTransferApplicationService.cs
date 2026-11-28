using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// ポータブル機器とのデータ同期、楽曲ファイル転送、デバイス内探索を統括するアプリケーションサービス
/// </summary>
public class DataTransferApplicationService
{
    private readonly IDataTransferRepository _dataTransferRepository;
    private readonly IAudioTranscodeService? _audioTranscodeService;
    private readonly ISettingsService? _settingsService;
    private readonly ILrcParser? _lrcParser;

    /// <summary>
    /// DataTransferApplicationServiceを初期化します
    /// </summary>
    /// <param name="dataTransferRepository">データ転送リポジトリ</param>
    /// <param name="audioTranscodeService">音声変換サービス（省略時は変換なし）</param>
    /// <param name="settingsService">設定サービス（省略時は変換・LRC同梱無効）</param>
    /// <param name="lrcParser">LRC 生成元種別判別パーサー（省略時は LRC 同梱無効）</param>
    public DataTransferApplicationService(
        IDataTransferRepository dataTransferRepository,
        IAudioTranscodeService? audioTranscodeService = null,
        ISettingsService? settingsService = null,
        ILrcParser? lrcParser = null)
    {
        _dataTransferRepository = dataTransferRepository ?? throw new ArgumentNullException(nameof(dataTransferRepository));
        _audioTranscodeService = audioTranscodeService;
        _settingsService = settingsService;
        _lrcParser = lrcParser;
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
        var summary = await TransferTracksWithResultAsync(
            tracks,
            destinationFolder,
            progress: progress,
            detailProgress: null,
            cancellationToken: cancellationToken);
        return summary.SuccessCount;
    }

    /// <summary>
    /// 選択されたトラックコレクションを 1 曲単位のエラーハンドリング・詳細進捗・キャンセル対応付きで順次転送し、結果サマリを返します
    /// </summary>
    /// <param name="tracks">転送対象トラックのコレクション</param>
    /// <param name="destinationFolder">転送先フォルダパス</param>
    /// <param name="progress">全体進捗通知（0.0〜1.0）</param>
    /// <param name="detailProgress">詳細進捗通知（件数・曲名・フェーズ）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>転送結果サマリ</returns>
    public async Task<TransferResultSummary> TransferTracksWithResultAsync(
        IEnumerable<Track> tracks,
        string destinationFolder,
        IProgress<double>? progress = null,
        IProgress<TransferProgressInfo>? detailProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var trackList = new List<Track>(tracks);
        if (trackList.Count == 0)
        {
            return new TransferResultSummary(0, 0);
        }

        int successCount = 0;
        var failedItems = new List<TransferFailedItem>();
        bool wasCancelled = false;
        int total = trackList.Count;

        var (transcodeOptions, lrcTransferOptions) = ResolveTransferOptions();

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            var track = trackList[i];
            string sourceFilePath = track.FilePath ?? string.Empty;
            string sourceFileName = Path.GetFileName(sourceFilePath);
            string displayTitle = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : sourceFileName;

            try
            {
                bool success;
                string transferredFileName;
                if (_audioTranscodeService != null && _audioTranscodeService.NeedsTranscode(sourceFilePath, transcodeOptions))
                {
                    string phaseText = ResolveTranscodePhaseText(sourceFilePath);
                    detailProgress?.Report(new TransferProgressInfo(i + 1, total, (double)i / total * 100.0, sourceFileName, phaseText));

                    using var prepared = await _audioTranscodeService.PrepareForTransferAsync(
                        sourceFilePath,
                        transcodeOptions,
                        cancellationToken);
                    transferredFileName = !string.IsNullOrWhiteSpace(prepared.TargetFileName)
                        ? prepared.TargetFileName
                        : Path.GetFileName(prepared.OutputFilePath);

                    detailProgress?.Report(new TransferProgressInfo(i + 1, total, (double)i / total * 100.0, transferredFileName, "コピー中..."));
                    success = await _dataTransferRepository.TransferTrackAsync(
                        prepared.OutputFilePath,
                        destinationFolder,
                        null,
                        cancellationToken);
                }
                else
                {
                    transferredFileName = sourceFileName;
                    detailProgress?.Report(new TransferProgressInfo(i + 1, total, (double)i / total * 100.0, transferredFileName, "コピー中..."));
                    success = await _dataTransferRepository.TransferTrackAsync(
                        sourceFilePath,
                        destinationFolder,
                        null,
                        cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    break;
                }

                if (success)
                {
                    successCount++;
                    await TryTransferCompanionLrcAsync(
                        sourceFilePath,
                        transferredFileName,
                        destinationFolder,
                        lrcTransferOptions,
                        cancellationToken);
                }
                else
                {
                    failedItems.Add(new TransferFailedItem(displayTitle, sourceFileName, "ファイルの転送に失敗しました。"));
                }
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"トラック {track.FilePath} の転送中にエラーが発生しました: {ex.Message}");
                failedItems.Add(new TransferFailedItem(displayTitle, sourceFileName, ex.Message));
            }

            double ratio = (double)(i + 1) / total;
            progress?.Report(ratio);
            detailProgress?.Report(new TransferProgressInfo(i + 1, total, ratio * 100.0, sourceFileName, "処理完了"));
        }

        return new TransferResultSummary(
            successCount: successCount,
            skippedCount: 0,
            failedItems: failedItems,
            wasCancelled: wasCancelled);
    }

    private static string ResolveTranscodePhaseText(string? sourceFilePath)
    {
        string ext = Path.GetExtension(sourceFilePath ?? string.Empty);
        if (ext.Equals(".flac", StringComparison.OrdinalIgnoreCase))
        {
            return "変換中 (FLAC → MP3)...";
        }

        if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return "変換中 (MP4 → M4A)...";
        }

        return "変換中...";
    }

    private async Task TryTransferCompanionLrcAsync(
        string sourceAudioFilePath,
        string transferredAudioFileName,
        string destinationFolder,
        LrcTransferOptions lrcTransferOptions,
        CancellationToken cancellationToken)
    {
        if (!CompanionLrcResolver.TryResolveCompanionLrcPath(
            sourceAudioFilePath,
            lrcTransferOptions,
            _lrcParser,
            out string lrcSourceFilePath,
            onWarning: (msg, ex) => Debug.WriteLine($"{msg}: {ex.Message}")))
        {
            return;
        }

        string lrcDestinationFileName = Path.ChangeExtension(transferredAudioFileName, ".lrc");
        try
        {
            await _dataTransferRepository.TransferCompanionFileAsync(
                lrcSourceFilePath,
                destinationFolder,
                lrcDestinationFileName,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 同梱 .lrc の転送失敗は音声ファイル本体の転送成功・後続トラックの転送を妨げない
            Debug.WriteLine($"同梱 LRC ファイルの転送に失敗しました ({lrcSourceFilePath}): {ex.Message}");
        }
    }

    private (AudioTranscodeOptions TranscodeOptions, LrcTransferOptions LrcTransferOptions) ResolveTransferOptions()
    {
        if (_settingsService == null)
        {
            return (new AudioTranscodeOptions(), new LrcTransferOptions());
        }

        try
        {
            var settings = _settingsService.LoadSettings();
            if (settings == null)
            {
                return (new AudioTranscodeOptions(), new LrcTransferOptions());
            }

            var transcodeOptions = new AudioTranscodeOptions
            {
                ConvertMp4ToM4a = settings.ConvertMp4ToM4a,
                ConvertFlacToMp3 = settings.ConvertFlacToMp3
            };
            var lrcTransferOptions = LrcTransferOptions.FromSettings(settings);
            return (transcodeOptions, lrcTransferOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"転送設定の読み込みに失敗したため既定値へフォールバックします: {ex.Message}");
            return (new AudioTranscodeOptions(), new LrcTransferOptions());
        }
    }

    /// <summary>
    /// デバイス上の指定されたトラックを PC 側の指定パスへダウンロード（逆転送）します
    /// </summary>
    /// <param name="deviceFilePath">デバイス上のファイルパス</param>
    /// <param name="destinationFilePath">PC 側の保存先ファイルパス</param>
    /// <param name="progress">進捗通知（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ダウンロード成功時はtrue</returns>
    public async Task<bool> DownloadTrackFromDeviceAsync(
        string deviceFilePath,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await _dataTransferRepository.DownloadTrackFromDeviceAsync(
            deviceFilePath,
            destinationFilePath,
            progress,
            cancellationToken);
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
