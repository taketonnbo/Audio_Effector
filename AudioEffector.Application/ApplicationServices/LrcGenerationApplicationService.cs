using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// テキストファイルからの LRC 自動生成・音声からの直接文字起こし LRC 生成・文字コード事前検査・モデル管理・
/// `.lrc` ファイル保存および歌詞表示キャッシュ即時更新を統括するアプリケーションサービスです
/// </summary>
public class LrcGenerationApplicationService
{
    private static readonly string[] SupportedTextExtensions = [".txt", ".lrc"];

    private readonly IAudioAlignmentEngine _alignmentEngine;
    private readonly ITextEncodingService _textEncodingService;
    private readonly ILocalSpeechModelManager _modelManager;
    private readonly ILrcParser _lrcParser;
    private readonly LyricsApplicationService _lyricsApplicationService;
    private readonly IAppLogger? _logger;

    /// <summary>
    /// 現在使用している解析エンジン名を取得します
    /// </summary>
    public string EngineName => _alignmentEngine.EngineName;

    /// <summary>
    /// <see cref="LrcGenerationApplicationService"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="alignmentEngine">音声アライメント／文字起こしエンジン</param>
    /// <param name="textEncodingService">文字コード判定・UTF-8 変換・LRC 保存サービス</param>
    /// <param name="modelManager">ローカル音声認識モデルマネージャー</param>
    /// <param name="lrcParser">LRC パーサー・フォーマッター</param>
    /// <param name="lyricsApplicationService">歌詞表示アプリケーションサービス（保存後キャッシュ更新連携用）</param>
    /// <param name="logger">エラー詳細記録用ロガー（省略可能）</param>
    public LrcGenerationApplicationService(
        IAudioAlignmentEngine alignmentEngine,
        ITextEncodingService textEncodingService,
        ILocalSpeechModelManager modelManager,
        ILrcParser lrcParser,
        LyricsApplicationService lyricsApplicationService,
        IAppLogger? logger = null)
    {
        _alignmentEngine = alignmentEngine ?? throw new ArgumentNullException(nameof(alignmentEngine));
        _textEncodingService = textEncodingService ?? throw new ArgumentNullException(nameof(textEncodingService));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _lrcParser = lrcParser ?? throw new ArgumentNullException(nameof(lrcParser));
        _lyricsApplicationService = lyricsApplicationService ?? throw new ArgumentNullException(nameof(lyricsApplicationService));
        _logger = logger;
    }

    /// <summary>
    /// 現在アクティブなローカルモデル情報を取得します
    /// </summary>
    /// <returns>アクティブな <see cref="LocalSpeechModelInfo"/></returns>
    public LocalSpeechModelInfo GetActiveModelInfo() => _modelManager.GetActiveModelInfo();

    /// <summary>
    /// ダウンロード・切替に対応している標準モデル一覧を取得します
    /// </summary>
    /// <returns>対応モデル情報一覧</returns>
    public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => _modelManager.GetSupportedModels();

    /// <summary>
    /// 標準モデル識別子を選択します
    /// </summary>
    /// <param name="modelId">モデル識別子（<c>tiny</c>, <c>base</c>, <c>small</c>）</param>
    /// <returns>実行成否 <see cref="Result"/></returns>
    public Result SelectModel(string modelId)
    {
        try
        {
            _modelManager.SelectModel(modelId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException($"標準モデルの選択に失敗しました (ModelId={modelId})", ex);
            return Result.Failure(LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(ex));
        }
    }

    /// <summary>
    /// 手動配置されたローカルモデルファイルのパスを設定します
    /// </summary>
    /// <param name="modelFilePath">モデルファイルパス（標準モデルへ戻す場合は <see langword="null"/>）</param>
    /// <returns>実行成否 <see cref="Result"/></returns>
    public Result SetCustomModelPath(string? modelFilePath)
    {
        try
        {
            _modelManager.SetCustomModelPath(modelFilePath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException($"カスタムモデルパスの設定に失敗しました (Path={modelFilePath})", ex);
            return Result.Failure(LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(ex));
        }
    }

    /// <summary>
    /// 指定された標準ローカルモデルを非同期ダウンロードします
    /// </summary>
    /// <param name="modelId">モデル識別子（省略時は現在選択中のモデル）</param>
    /// <param name="progress">進捗通知（0.0〜1.0、任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ダウンロード後のモデル情報を含む <see cref="Result{T}"/></returns>
    public async Task<Result<LocalSpeechModelInfo>> DownloadModelAsync(
        string? modelId = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LocalSpeechModelInfo info = await _modelManager
                .DownloadModelAsync(modelId, progress, cancellationToken)
                .ConfigureAwait(false);
            return Result.Success(info);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            bool isTimeout = ex is OperationCanceledException or TimeoutException
                || LrcGenerationErrorMessageResolver.ContainsException<TimeoutException>(ex)
                || LrcGenerationErrorMessageResolver.ContainsException<TaskCanceledException>(ex);
            LogException($"ローカルモデルのダウンロードに失敗しました (ModelId={modelId ?? "default"}, IsTimeout={isTimeout})", ex);
            return Result.Failure<LocalSpeechModelInfo>(LrcGenerationErrorMessageResolver.ResolveModelDownloadErrorMessage(ex, isTimeout));
        }
    }

    /// <summary>
    /// ステップ 1: 入力歌詞テキストファイルの拡張子・存在・空行・文字コードを事前検査します
    /// </summary>
    /// <param name="textFilePath">検査対象のテキストファイルパス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検査結果を含む <see cref="Result{T}"/></returns>
    public async Task<Result<TextEncodingInspectionResult>> InspectLyricsTextAsync(
        string textFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(textFilePath))
        {
            return Result.Failure<TextEncodingInspectionResult>("歌詞テキストファイルが選択されていません。");
        }

        if (!IsSupportedTextExtension(textFilePath))
        {
            return Result.Failure<TextEncodingInspectionResult>(
                "未対応のファイル形式です。プレーンテキストファイル（.txt）を指定してください。");
        }

        try
        {
            TextEncodingInspectionResult inspection = await _textEncodingService
                .InspectAsync(textFilePath, cancellationToken)
                .ConfigureAwait(false);

            if (inspection.IsEmpty)
            {
                return Result.Failure<TextEncodingInspectionResult>(
                    "選択されたテキストファイルが空か、有効な歌詞行が含まれていません。");
            }

            return Result.Success(inspection);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            LogException($"テキストファイルが見つかりませんでした (Path={textFilePath})", ex);
            return Result.Failure<TextEncodingInspectionResult>("指定されたテキストファイルが見つかりません。");
        }
        catch (Exception ex)
        {
            LogException($"テキストファイルの事前検査に失敗しました (Path={textFilePath})", ex);
            return Result.Failure<TextEncodingInspectionResult>(
                LrcGenerationErrorMessageResolver.ResolveTextReadErrorMessage(ex, isExistingLrc: false));
        }
    }

    /// <summary>
    /// ステップ 2-A: 歌詞テキストファイル（.txt）と楽曲音声から強制アライメントにより LRC を非同期生成します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <param name="textFilePath">入力歌詞テキストファイルパス</param>
    /// <param name="allowEncodingConversion">非 UTF-8 テキストの UTF-8 変換をユーザーが承諾済みかどうか</param>
    /// <param name="progress">進捗通知（任意）</param>
    /// <param name="languageCode">音声認識の言語コード（省略時は自動判定）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成結果を含む <see cref="Result{T}"/></returns>
    public async Task<Result<AlignmentResult>> GenerateLrcAsync(
        Track track,
        string textFilePath,
        bool allowEncodingConversion,
        IProgress<AlignmentProgress>? progress = null,
        string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return Result.Failure<AlignmentResult>("対象の楽曲が選択されていません。");
        }

        if (string.IsNullOrWhiteSpace(textFilePath))
        {
            return Result.Failure<AlignmentResult>("歌詞テキストファイルが選択されていません。");
        }

        if (!IsSupportedTextExtension(textFilePath))
        {
            return Result.Failure<AlignmentResult>(
                "未対応のファイル形式です。プレーンテキストファイル（.txt）を指定してください。");
        }

        try
        {
            TextEncodingInspectionResult inspection = await _textEncodingService
                .InspectAsync(textFilePath, cancellationToken)
                .ConfigureAwait(false);

            if (inspection.IsEmpty)
            {
                return Result.Failure<AlignmentResult>("選択されたテキストファイルが空か、有効な歌詞行が含まれていません。");
            }

            if (inspection.RequiresConversion && !allowEncodingConversion)
            {
                return Result.Failure<AlignmentResult>(
                    $"入力ファイルは {inspection.DetectedEncodingName} です。UTF-8 への変換承諾が必要です。");
            }

            string utf8Text = await _textEncodingService
                .ReadAsUtf8TextAsync(textFilePath, allowEncodingConversion, cancellationToken)
                .ConfigureAwait(false);

            string[] lyricsLines = utf8Text
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToArray();

            if (lyricsLines.Length == 0)
            {
                return Result.Failure<AlignmentResult>("有効な歌詞行が見つかりませんでした。");
            }

            var request = new AlignmentRequest(
                audioFilePath: track.FilePath,
                mode: LrcGenerationMode.TextAlignment,
                lyricsLines: lyricsLines,
                languageCode: languageCode,
                title: track.Title,
                artist: track.Artist,
                album: track.Album);

            AlignmentResult result = await _alignmentEngine
                .GenerateAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(result);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            LogException($"テキスト強制アライメントによる LRC 生成に失敗しました (Track={track.FilePath})", ex);
            return Result.Failure<AlignmentResult>(LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(ex));
        }
    }

    /// <summary>
    /// ステップ 2-B: 歌詞テキストを用いず、楽曲音声から直接文字起こしして LRC を非同期生成します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <param name="progress">進捗通知（任意）</param>
    /// <param name="languageCode">音声認識の言語コード（省略時は自動判定）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成結果を含む <see cref="Result{T}"/></returns>
    public async Task<Result<AlignmentResult>> TranscribeAudioToLrcAsync(
        Track track,
        IProgress<AlignmentProgress>? progress = null,
        string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return Result.Failure<AlignmentResult>("対象の楽曲が選択されていません。");
        }

        try
        {
            var request = new AlignmentRequest(
                audioFilePath: track.FilePath,
                mode: LrcGenerationMode.AudioTranscription,
                lyricsLines: null,
                languageCode: languageCode,
                title: track.Title,
                artist: track.Artist,
                album: track.Album);

            AlignmentResult result = await _alignmentEngine
                .GenerateAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(result);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            LogException($"音声直接文字起こしによる LRC 生成に失敗しました (Track={track.FilePath})", ex);
            return Result.Failure<AlignmentResult>(LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(ex));
        }
    }

    /// <summary>
    /// 対象楽曲の既定 `.lrc` 保存先パス（音声ファイルと同階層・同名 `.lrc`）を解決します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <returns>既定の `.lrc` ファイルパス</returns>
    public string ResolveDefaultLrcPath(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (string.IsNullOrWhiteSpace(track.FilePath))
        {
            throw new ArgumentException("楽曲のファイルパスが設定されていません。", nameof(track));
        }

        return Path.ChangeExtension(track.FilePath, ".lrc");
    }

    /// <summary>
    /// 保存先に既に `.lrc` ファイルが存在するかどうかを確認します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <param name="destinationPath">明示的な保存先パス（省略時は既定の `.lrc` パス）</param>
    /// <returns>既に存在する場合は <see langword="true"/></returns>
    public bool LrcFileExists(Track track, string? destinationPath = null)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return false;
        }

        string targetPath = string.IsNullOrWhiteSpace(destinationPath)
            ? ResolveDefaultLrcPath(track)
            : destinationPath.Trim();

        return File.Exists(targetPath);
    }

    /// <summary>
    /// 対象楽曲の既定保存先に存在する `.lrc` ファイルを読み込み、<see cref="LyricsDocument"/> として復元します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>
    /// 既存 `.lrc` の読み込み結果を含む <see cref="Result{T}"/>（ファイルが存在しない場合は成功かつ <see langword="null"/>）
    /// </returns>
    public async Task<Result<LyricsDocument?>> LoadExistingLrcAsync(
        Track track,
        CancellationToken cancellationToken = default)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return Result.Success<LyricsDocument?>(null);
        }

        string lrcPath;
        try
        {
            lrcPath = ResolveDefaultLrcPath(track);
        }
        catch (ArgumentException ex)
        {
            LogException($"既存の .lrc パス解決に失敗しました (Track={track.FilePath})", ex);
            return Result.Failure<LyricsDocument?>(
                LrcGenerationErrorMessageResolver.ResolveTextReadErrorMessage(ex, isExistingLrc: true));
        }

        if (!File.Exists(lrcPath))
        {
            return Result.Success<LyricsDocument?>(null);
        }

        try
        {
            string lrcContent = await _textEncodingService
                .ReadAsUtf8TextAsync(lrcPath, allowEncodingConversion: true, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(lrcContent))
            {
                return Result.Failure<LyricsDocument?>("既存の .lrc ファイルが空のため、編集画面を読み込めませんでした。");
            }

            LyricsDocument parsed = _lrcParser.Parse(lrcContent);
            if (!parsed.IsSynced || !parsed.HasVisibleLyrics)
            {
                return Result.Failure<LyricsDocument?>("既存の .lrc ファイルに有効な同期歌詞行が含まれていないため、編集画面を読み込めませんでした。");
            }

            LyricsDocument loaded = parsed.WithSource(LyricsSourceType.LrcFile, lrcPath);
            return Result.Success<LyricsDocument?>(loaded);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return Result.Success<LyricsDocument?>(null);
        }
        catch (Exception ex)
        {
            LogException($"既存の .lrc ファイルの読み込みに失敗しました (LrcPath={lrcPath})", ex);
            return Result.Failure<LyricsDocument?>(
                LrcGenerationErrorMessageResolver.ResolveTextReadErrorMessage(ex, isExistingLrc: true));
        }
    }

    /// <summary>
    /// <see cref="LyricsDocument"/> を LRC 形式の文字列へ整形します
    /// </summary>
    /// <param name="document">整形対象の歌詞ドキュメント</param>
    /// <returns>LRC 文字列</returns>
    public string FormatDocument(LyricsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _lrcParser.Format(document);
    }

    /// <summary>
    /// ステップ 3: 生成・調整済みの <see cref="LyricsDocument"/> を UTF-8（BOM なし）の `.lrc` ファイルとして保存し、
    /// <see cref="LyricsApplicationService"/> へキャッシュ無効化・即時反映通知を発行します
    /// </summary>
    /// <param name="track">対象楽曲</param>
    /// <param name="document">保存対象の歌詞ドキュメント</param>
    /// <param name="destinationPath">保存先パス（省略時は楽曲と同階層・同名 `.lrc`）</param>
    /// <param name="overwrite">既存ファイルの上書きを許可するかどうか</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>保存先ファイルパスを含む <see cref="Result{T}"/></returns>
    public async Task<Result<string>> SaveLrcAsync(
        Track track,
        LyricsDocument document,
        string? destinationPath = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (track is null || string.IsNullOrWhiteSpace(track.FilePath))
        {
            return Result.Failure<string>("対象の楽曲が選択されていません。");
        }

        if (document is null || !document.HasVisibleLyrics)
        {
            return Result.Failure<string>("保存対象の歌詞データが空です。");
        }

        string targetPath;
        try
        {
            targetPath = string.IsNullOrWhiteSpace(destinationPath)
                ? ResolveDefaultLrcPath(track)
                : Path.GetFullPath(destinationPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            LogException($"LRC 保存先パスが不正です (DestinationPath={destinationPath ?? track.FilePath})", ex);
            return Result.Failure<string>(LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(ex));
        }

        try
        {
            string lrcContent = _lrcParser.Format(document);
            await _textEncodingService
                .SaveLrcFileAsync(targetPath, lrcContent, overwrite, cancellationToken)
                .ConfigureAwait(false);

            // 既定の探索先（楽曲と同階層・同名 .lrc）へ保存した場合のみ歌詞表示キャッシュを更新通知する
            string defaultLrcPath = Path.GetFullPath(ResolveDefaultLrcPath(track));
            if (string.Equals(targetPath, defaultLrcPath, StringComparison.OrdinalIgnoreCase))
            {
                LyricsDocument savedDocument = document.WithSource(LyricsSourceType.LrcFile, targetPath);
                await _lyricsApplicationService
                    .InvalidateCacheAsync(track.FilePath, savedDocument, cancellationToken)
                    .ConfigureAwait(false);
            }

            return Result.Success(targetPath);
        }
        catch (OperationCanceledException oce) when (LrcGenerationErrorMessageResolver.IsUserCancellation(oce, cancellationToken))
        {
            throw;
        }
        catch (Exception ex)
        {
            LogException($"LRC ファイルの保存に失敗しました (TargetPath={targetPath})", ex);
            return Result.Failure<string>(LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(ex));
        }
    }

    private void LogException(string context, Exception ex)
    {
        if (ex is LrcGenerationException { IsLogged: true })
        {
            return;
        }

        string message = $"{context} [ExceptionType={ex.GetType().FullName}]";
        if (_logger is not null)
        {
            _logger.Error(message, ex);
        }
        else
        {
            Debug.WriteLine($"[LrcGenerationApplicationService] {context} ({ex.GetType().Name})");
        }
    }

    private static bool IsSupportedTextExtension(string textFilePath)
    {
        string extension = Path.GetExtension(textFilePath);
        return SupportedTextExtensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase));
    }
}
