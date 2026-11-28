using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace AudioEffector.Infrastructure.Audio;

/// <summary>
/// Windows Media Foundation（NAudio）および TagLibSharp を用いて機器転送用の音声フォーマット変換を行う共通サービス
/// </summary>
public sealed class MediaFoundationAudioTranscodeService : IAudioTranscodeService
{
    private const int DefaultAacBitrate = 192000;
    private static readonly object MediaFoundationLock = new();
    private static bool _mediaFoundationInitialized;

    /// <inheritdoc />
    public bool NeedsTranscode(string sourceFilePath, AudioTranscodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return false;
        }

        string ext = Path.GetExtension(sourceFilePath);
        if (options.ConvertMp4ToM4a && string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public string ResolveTargetFileName(string sourceFilePath, AudioTranscodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileName(sourceFilePath);
        string ext = Path.GetExtension(fileName);

        if (options.ConvertMp4ToM4a && string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(fileName, ".m4a");
        }

        return fileName;
    }

    /// <inheritdoc />
    public Task<TranscodedAudioResult> PrepareForTransferAsync(
        string sourceFilePath,
        AudioTranscodeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        if (!NeedsTranscode(sourceFilePath, options))
        {
            return Task.FromResult(TranscodedAudioResult.Passthrough(sourceFilePath));
        }

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"変換対象の音声ファイルが見つかりません: {sourceFilePath}", sourceFilePath);
        }

        string targetFileName = ResolveTargetFileName(sourceFilePath, options);
        return Task.Run(() => TranscodeToTemporaryFile(sourceFilePath, targetFileName, cancellationToken), cancellationToken);
    }

    private static TranscodedAudioResult TranscodeToTemporaryFile(
        string sourceFilePath,
        string targetFileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string tempDir = Path.Combine(Path.GetTempPath(), "AudioEffector_Transcode", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string outputPath = Path.Combine(tempDir, targetFileName);

        try
        {
            EnsureMediaFoundationInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            string targetExt = Path.GetExtension(targetFileName);
            if (string.Equals(targetExt, ".m4a", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new MediaFoundationReader(sourceFilePath);
                cancellationToken.ThrowIfCancellationRequested();
                MediaFoundationEncoder.EncodeToAac(reader, outputPath, DefaultAacBitrate);
            }
            else
            {
                throw new NotSupportedException($"未対応の変換先フォーマットです: {targetExt}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            CopyMetadataBestEffort(sourceFilePath, outputPath);

            return new TranscodedAudioResult(
                sourceFilePath,
                outputPath,
                targetFileName,
                wasTranscoded: true,
                temporaryDirectoryPath: tempDir);
        }
        catch
        {
            CleanupDirectorySafe(tempDir);
            throw;
        }
    }

    private static void EnsureMediaFoundationInitialized()
    {
        if (_mediaFoundationInitialized)
        {
            return;
        }

        lock (MediaFoundationLock)
        {
            if (!_mediaFoundationInitialized)
            {
                MediaFoundationApi.Startup();
                _mediaFoundationInitialized = true;
            }
        }
    }

    private static void CopyMetadataBestEffort(string sourceFilePath, string destinationFilePath)
    {
        try
        {
            using var sourceTagFile = TagLib.File.Create(sourceFilePath);
            using var destTagFile = TagLib.File.Create(destinationFilePath);

            var srcTag = sourceTagFile.Tag;
            var dstTag = destTagFile.Tag;
            if (srcTag == null || dstTag == null)
            {
                return;
            }

            dstTag.Title = srcTag.Title;
            dstTag.Performers = srcTag.Performers;
            dstTag.AlbumArtists = srcTag.AlbumArtists;
            dstTag.Album = srcTag.Album;
            dstTag.Genres = srcTag.Genres;
            dstTag.Year = srcTag.Year;
            dstTag.Track = srcTag.Track;
            dstTag.TrackCount = srcTag.TrackCount;
            dstTag.Disc = srcTag.Disc;
            dstTag.DiscCount = srcTag.DiscCount;
            dstTag.Comment = srcTag.Comment;
            if (srcTag.Pictures != null && srcTag.Pictures.Length > 0)
            {
                dstTag.Pictures = srcTag.Pictures;
            }

            destTagFile.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Metadata copy skipped for {destinationFilePath}: {ex.Message}");
        }
    }

    private static void CleanupDirectorySafe(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // 後始末時の例外は無視
        }
    }
}
