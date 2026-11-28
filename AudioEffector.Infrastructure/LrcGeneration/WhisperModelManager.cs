using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using NLog;
using Whisper.net.Ggml;

namespace AudioEffector.Infrastructure.LrcGeneration;

/// <summary>
/// 無料・MIT ライセンスの Whisper GGML ローカルモデルの配置確認・手動指定・自動ダウンロードを管理する <see cref="ILocalSpeechModelManager"/> 実装です
/// </summary>
public sealed class WhisperModelManager : ILocalSpeechModelManager
{
    /// <summary>
    /// 既定の標準モデル識別子（速度と日本語精度のバランスに優れた base モデル）
    /// </summary>
    public const string DEFAULT_MODEL_ID = "base";

    private const int DOWNLOAD_BUFFER_SIZE = 81920;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly ModelCatalogEntry[] Catalog =
    [
        new("tiny", "Whisper (tiny / 軽量・高速)", "ggml-tiny.bin", GgmlType.Tiny, 77_700_000L),
        new("base", "Whisper (base / 標準・推奨)", "ggml-base.bin", GgmlType.Base, 147_964_000L),
        new("small", "Whisper (small / 高精度)", "ggml-small.bin", GgmlType.Small, 487_600_000L),
    ];

    private readonly string _modelsDirectory;
    private readonly Func<GgmlType, CancellationToken, Task<Stream>> _downloaderFunc;
    private readonly object _stateLock = new();

    private string _selectedModelId = DEFAULT_MODEL_ID;
    private string? _customModelPath;

    /// <summary>
    /// 既定のモデル保存先（<c>%LocalAppData%\AudioEffector\models</c>）で <see cref="WhisperModelManager"/> を初期化します
    /// </summary>
    public WhisperModelManager()
        : this(ResolveDefaultModelsDirectory(), downloaderFunc: null)
    {
    }

    /// <summary>
    /// モデル格納ディレクトリを指定して <see cref="WhisperModelManager"/> を初期化します
    /// </summary>
    /// <param name="modelsDirectory">モデルファイルを保存・検索するディレクトリパス</param>
    public WhisperModelManager(string modelsDirectory)
        : this(modelsDirectory, downloaderFunc: null)
    {
    }

    /// <summary>
    /// テスト用ダウンローダー関数を注入して <see cref="WhisperModelManager"/> を初期化します
    /// </summary>
    /// <param name="modelsDirectory">モデルファイルを保存・検索するディレクトリパス</param>
    /// <param name="downloaderFunc">GGML モデルストリーム取得デリゲート（省略時は既定ダウンローダーを使用）</param>
    internal WhisperModelManager(
        string modelsDirectory,
        Func<GgmlType, CancellationToken, Task<Stream>>? downloaderFunc)
    {
        if (string.IsNullOrWhiteSpace(modelsDirectory))
        {
            throw new ArgumentException("モデル保存ディレクトリが指定されていません。", nameof(modelsDirectory));
        }

        _modelsDirectory = Path.GetFullPath(modelsDirectory);
        _downloaderFunc = downloaderFunc ?? DownloadFromDefaultGgmlAsync;
    }

    /// <summary>
    /// 現在アクティブなモデル情報および利用可否を取得します
    /// </summary>
    /// <returns>アクティブな <see cref="LocalSpeechModelInfo"/></returns>
    public LocalSpeechModelInfo GetActiveModelInfo()
    {
        string? customPath;
        string selectedId;
        lock (_stateLock)
        {
            customPath = _customModelPath;
            selectedId = _selectedModelId;
        }

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            bool exists = File.Exists(customPath);
            long size = exists ? new FileInfo(customPath).Length : 0L;
            bool isAvailable = exists && size > 0L;
            string fileName = Path.GetFileName(customPath);
            return new LocalSpeechModelInfo(
                modelId: "custom",
                displayName: $"カスタムモデル ({fileName})",
                fileName: fileName,
                filePath: customPath,
                isAvailable: isAvailable,
                estimatedSizeBytes: size);
        }

        ModelCatalogEntry entry = FindCatalogEntry(selectedId);
        return BuildModelInfo(entry);
    }

    /// <summary>
    /// ダウンロードまたは切替に対応している標準モデル一覧を取得します
    /// </summary>
    /// <returns>対応モデル情報一覧</returns>
    public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels()
    {
        return Catalog.Select(BuildModelInfo).ToArray();
    }

    /// <summary>
    /// 使用する既定の標準モデル識別子（<c>tiny</c>, <c>base</c>, <c>small</c>）を選択します
    /// </summary>
    /// <param name="modelId">モデル識別子</param>
    public void SelectModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("モデル識別子を指定してください。", nameof(modelId));
        }

        ModelCatalogEntry entry = FindCatalogEntry(modelId);
        lock (_stateLock)
        {
            _selectedModelId = entry.ModelId;
            _customModelPath = null;
        }
    }

    /// <summary>
    /// ユーザーが手動配置したローカルモデルファイル（<c>.bin</c> 等）のパスを設定します
    /// </summary>
    /// <param name="modelFilePath">モデルファイルパス（解除時は <see langword="null"/>）</param>
    public void SetCustomModelPath(string? modelFilePath)
    {
        if (string.IsNullOrWhiteSpace(modelFilePath))
        {
            lock (_stateLock)
            {
                _customModelPath = null;
            }

            return;
        }

        string fullPath = Path.GetFullPath(modelFilePath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("指定されたローカルモデルファイルが見つかりません。", fullPath);
        }

        if (new FileInfo(fullPath).Length <= 0L)
        {
            throw new InvalidDataException("指定されたローカルモデルファイルが空（0 バイト）のため使用できません。");
        }

        lock (_stateLock)
        {
            _customModelPath = fullPath;
        }
    }

    /// <summary>
    /// 指定された標準モデルをローカル格納ディレクトリへ非同期ダウンロードします
    /// </summary>
    /// <param name="modelId">ダウンロード対象のモデル識別子（省略時は現在選択中の標準モデル）</param>
    /// <param name="progress">ダウンロード進捗（0.0〜1.0、任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ダウンロード完了後の <see cref="LocalSpeechModelInfo"/></returns>
    public async Task<LocalSpeechModelInfo> DownloadModelAsync(
        string? modelId = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string targetId;
        lock (_stateLock)
        {
            targetId = string.IsNullOrWhiteSpace(modelId) ? _selectedModelId : modelId.Trim();
        }

        ModelCatalogEntry entry = FindCatalogEntry(targetId);

        string destinationPath = Path.Combine(_modelsDirectory, entry.FileName);
        string tempPath = $"{destinationPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.download";

        Logger.Info("Whisper ローカルモデルをダウンロードします: {ModelId} -> {DestinationPath}", entry.ModelId, destinationPath);
        progress?.Report(0.0);

        try
        {
            Directory.CreateDirectory(_modelsDirectory);

            await using Stream remoteStream = await _downloaderFunc(entry.GgmlType, cancellationToken).ConfigureAwait(false);
            long totalBytes = remoteStream.CanSeek && remoteStream.Length > 0
                ? remoteStream.Length
                : entry.EstimatedSizeBytes;

            long downloaded = 0;
            await using (var fs = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: DOWNLOAD_BUFFER_SIZE,
                useAsync: true))
            {
                byte[] buffer = new byte[DOWNLOAD_BUFFER_SIZE];
                double lastReported = 0.0;

                int read;
                while ((read = await remoteStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;

                    if (progress is not null && totalBytes > 0)
                    {
                        double ratio = Math.Clamp((double)downloaded / totalBytes, 0.0, 0.99);
                        if (ratio - lastReported >= 0.01)
                        {
                            progress.Report(ratio);
                            lastReported = ratio;
                        }
                    }
                }

                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (downloaded <= 0L)
            {
                throw new InvalidDataException("ダウンロードされたモデルデータが空（0 バイト）のため保存を中止しました。");
            }

            File.Move(tempPath, destinationPath, overwrite: true);

            lock (_stateLock)
            {
                _selectedModelId = entry.ModelId;
                _customModelPath = null;
            }

            progress?.Report(1.0);
            Logger.Info(CultureInfo.InvariantCulture, "Whisper ローカルモデルのダウンロードが完了しました: {0}", destinationPath);
            return BuildModelInfo(entry);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            Logger.Info(CultureInfo.InvariantCulture, "Whisper ローカルモデルのダウンロードがユーザーによりキャンセルされました: ModelId={0}", entry.ModelId);
            throw;
        }
        catch (LrcGenerationException)
        {
            throw;
        }
        catch (InvalidDataException ex) when (LrcGenerationErrorMessageResolver.IsSafeUserMessage(ex.Message))
        {
            Logger.Warn(ex, "Whisper ローカルモデルのダウンロード結果が不正です: ModelId={ModelId}", entry.ModelId);
            throw;
        }
        catch (Exception ex)
        {
            bool isTimeout = ex is OperationCanceledException or TimeoutException
                || LrcGenerationErrorMessageResolver.ContainsException<TimeoutException>(ex)
                || LrcGenerationErrorMessageResolver.ContainsException<TaskCanceledException>(ex);
            Logger.Error(
                ex,
                "Whisper ローカルモデルのダウンロードに失敗しました: ModelId={ModelId}, DestinationPath={DestinationPath}, IsTimeout={IsTimeout}, ExceptionType={ExceptionType}",
                entry.ModelId,
                destinationPath,
                isTimeout,
                ex.GetType().FullName);
            throw LrcInfrastructureExceptionTranslator.TranslateModelDownloadException(ex, isTimeout, isLogged: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // 一時ファイル削除失敗で元の例外を隠蔽しない
                }
            }
        }
    }

    private LocalSpeechModelInfo BuildModelInfo(ModelCatalogEntry entry)
    {
        string path = Path.Combine(_modelsDirectory, entry.FileName);
        bool exists = File.Exists(path);
        long actualSize = exists ? new FileInfo(path).Length : 0L;
        bool isAvailable = exists && actualSize > 0L;

        return new LocalSpeechModelInfo(
            modelId: entry.ModelId,
            displayName: entry.DisplayName,
            fileName: entry.FileName,
            filePath: path,
            isAvailable: isAvailable,
            estimatedSizeBytes: isAvailable ? actualSize : entry.EstimatedSizeBytes);
    }

    private static ModelCatalogEntry FindCatalogEntry(string modelId)
    {
        ModelCatalogEntry? match = Catalog.FirstOrDefault(
            c => string.Equals(c.ModelId, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new ArgumentException($"未対応のローカルモデル識別子です: {modelId}", nameof(modelId));
        }

        return match;
    }

    private static Task<Stream> DownloadFromDefaultGgmlAsync(GgmlType ggmlType, CancellationToken cancellationToken)
    {
        return WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType, QuantizationType.NoQuantization, cancellationToken);
    }

    private static string ResolveDefaultModelsDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "AudioEffector", "models");
    }

    private sealed record ModelCatalogEntry(
        string ModelId,
        string DisplayName,
        string FileName,
        GgmlType GgmlType,
        long EstimatedSizeBytes);
}
