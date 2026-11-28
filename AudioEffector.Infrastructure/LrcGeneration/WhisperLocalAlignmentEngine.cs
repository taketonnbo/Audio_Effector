using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using NLog;
using Whisper.net;

namespace AudioEffector.Infrastructure.LrcGeneration;

/// <summary>
/// ローカル Whisper モデル（<c>Whisper.net</c>）と DP 強制アライメントを用いて
/// テキスト同期 LRC 生成および音声直接文字起こし LRC 生成を行う <see cref="IAudioAlignmentEngine"/> 実装です
/// </summary>
public sealed class WhisperLocalAlignmentEngine : IAudioAlignmentEngine
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ILocalSpeechModelManager _modelManager;
    private readonly ILrcParser _lrcParser;
    private readonly LyricsForcedAligner _forcedAligner;
    private readonly IAudioPcmDecoder _audioDecoder;
    private readonly Func<string, float[], string?, Action<int>?, CancellationToken, Task<IReadOnlyList<AlignmentSegment>>> _transcribeFunc;

    /// <summary>
    /// 解析エンジンの識別名を取得します
    /// </summary>
    public string EngineName => "Whisper.net (Local)";

    /// <summary>
    /// <see cref="WhisperLocalAlignmentEngine"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="modelManager">ローカル音声認識モデルマネージャー</param>
    /// <param name="lrcParser">LRC フォーマッター</param>
    /// <param name="forcedAligner">DP 強制アライメントサービス</param>
    public WhisperLocalAlignmentEngine(
        ILocalSpeechModelManager modelManager,
        ILrcParser lrcParser,
        LyricsForcedAligner forcedAligner)
        : this(modelManager, lrcParser, forcedAligner, new AudioPcmDecoder(), transcribeFunc: null)
    {
    }

    /// <summary>
    /// テスト用デコーダおよび文字起こし関数を注入して <see cref="WhisperLocalAlignmentEngine"/> を初期化します
    /// </summary>
    /// <param name="modelManager">ローカル音声認識モデルマネージャー</param>
    /// <param name="lrcParser">LRC フォーマッター</param>
    /// <param name="forcedAligner">DP 強制アライメントサービス</param>
    /// <param name="audioDecoder">16kHz モノラル PCM デコーダ</param>
    /// <param name="transcribeFunc">音声認識セグメント抽出デリゲート（省略時は Whisper.net 実行）</param>
    internal WhisperLocalAlignmentEngine(
        ILocalSpeechModelManager modelManager,
        ILrcParser lrcParser,
        LyricsForcedAligner forcedAligner,
        IAudioPcmDecoder audioDecoder,
        Func<string, float[], string?, Action<int>?, CancellationToken, Task<IReadOnlyList<AlignmentSegment>>>? transcribeFunc)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _lrcParser = lrcParser ?? throw new ArgumentNullException(nameof(lrcParser));
        _forcedAligner = forcedAligner ?? throw new ArgumentNullException(nameof(forcedAligner));
        _audioDecoder = audioDecoder ?? throw new ArgumentNullException(nameof(audioDecoder));
        _transcribeFunc = transcribeFunc ?? TranscribeWithWhisperAsync;
    }

    /// <summary>
    /// 指定されたリクエスト（テキスト強制アライメントまたは音声直接文字起こし）に基づいて同期歌詞を非同期生成します
    /// </summary>
    /// <param name="request">生成リクエスト</param>
    /// <param name="progress">進捗通知レシーバー（任意）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成された同期歌詞ドキュメントおよび LRC 文字列を含む <see cref="AlignmentResult"/></returns>
    public async Task<AlignmentResult> GenerateAsync(
        AlignmentRequest request,
        IProgress<AlignmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(request.AudioFilePath))
        {
            throw new FileNotFoundException("対象の音声ファイルが見つかりません。", request.AudioFilePath);
        }

        if (request.Mode == LrcGenerationMode.TextAlignment && request.LyricsLines.Count == 0)
        {
            throw new InvalidOperationException("テキストからの生成には 1 行以上の歌詞テキストが必要です。");
        }

        string modelPath = ResolveAndValidateModelPath(request.ModelPath);

        // Phase 1: 音声デコード (0% -> 20%)
        progress?.Report(new AlignmentProgress(AlignmentPhase.DecodingAudio, 0.02, "音声ファイルをデコードしています..."));
        var decodeProgress = new Progress<double>(p =>
            progress?.Report(new AlignmentProgress(AlignmentPhase.DecodingAudio, 0.02 + (p * 0.18), "音声ファイルを 16kHz モノラルへ変換中...")));

        DecodedAudioPcm decoded = await _audioDecoder
            .DecodeToMono16kHzAsync(request.AudioFilePath, decodeProgress, cancellationToken)
            .ConfigureAwait(false);

        if (decoded.Samples.Length == 0)
        {
            throw new InvalidOperationException("音声データが空のため LRC を生成できませんでした。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: モデル読み込み (20% -> 25%)
        progress?.Report(new AlignmentProgress(AlignmentPhase.LoadingModel, 0.22, "ローカル音声認識モデルを読み込んでいます..."));

        // Phase 3: Whisper 音声認識・文字起こし (25% -> 85%)
        progress?.Report(new AlignmentProgress(AlignmentPhase.Transcribing, 0.25, "音声認識・文字起こしを実行しています..."));
        int lastPercent = -1;
        IReadOnlyList<AlignmentSegment> segments = await _transcribeFunc(
            modelPath,
            decoded.Samples,
            request.LanguageCode,
            whisperPercent =>
            {
                int clamped = Math.Clamp(whisperPercent, 0, 100);
                if (clamped != lastPercent)
                {
                    lastPercent = clamped;
                    double overall = 0.25 + ((clamped / 100.0) * 0.60);
                    progress?.Report(new AlignmentProgress(
                        AlignmentPhase.Transcribing,
                        overall,
                        $"音声認識・文字起こし中 ({clamped}%)..."));
                }
            },
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (segments.Count == 0)
        {
            throw new InvalidOperationException(
                "発話・歌唱区間を検出できませんでした（無音またはインストゥルメンタル楽曲の可能性があります）。");
        }

        // Phase 4: モード別ドキュメント構築 (85% -> 95%)
        LyricsDocument document;
        if (request.Mode == LrcGenerationMode.TextAlignment)
        {
            progress?.Report(new AlignmentProgress(AlignmentPhase.AligningLyrics, 0.88, "歌詞テキストと音声タイミングを合わせています..."));
            document = _forcedAligner.Align(
                request.LyricsLines,
                segments,
                decoded.Duration,
                request.Title,
                request.Artist,
                request.Album);
        }
        else
        {
            progress?.Report(new AlignmentProgress(AlignmentPhase.AligningLyrics, 0.88, "文字起こしセグメントから同期歌詞を構築しています..."));
            document = _forcedAligner.BuildDocumentFromSegments(
                segments,
                request.Title,
                request.Artist,
                request.Album);
        }

        if (!document.HasVisibleLyrics)
        {
            throw new InvalidOperationException("有効な歌詞行を生成できませんでした。");
        }

        // Phase 5: LRC フォーマット (95% -> 100%)
        progress?.Report(new AlignmentProgress(AlignmentPhase.FormattingLrc, 0.96, "LRC 形式へ整形しています..."));
        string lrcContent = _lrcParser.Format(document);

        progress?.Report(new AlignmentProgress(AlignmentPhase.Completed, 1.0, "LRC の生成が完了しました。"));
        Logger.Info(
            "LRC 生成が完了しました: Mode={Mode}, Lines={LineCount}, Segments={SegmentCount}",
            request.Mode,
            document.Lines.Count,
            segments.Count);

        return new AlignmentResult(document, lrcContent, segments, request.Mode, EngineName);
    }

    private string ResolveAndValidateModelPath(string? explicitModelPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitModelPath))
        {
            if (!File.Exists(explicitModelPath))
            {
                throw new FileNotFoundException("指定されたローカルモデルファイルが見つかりません。", explicitModelPath);
            }

            return explicitModelPath;
        }

        LocalSpeechModelInfo active = _modelManager.GetActiveModelInfo();
        if (!active.IsAvailable || string.IsNullOrWhiteSpace(active.FilePath) || !File.Exists(active.FilePath))
        {
            throw new FileNotFoundException(
                "ローカル音声認識モデルが配置されていません。先にモデルをダウンロードまたは指定してください。",
                active.FilePath);
        }

        return active.FilePath;
    }

    private static async Task<IReadOnlyList<AlignmentSegment>> TranscribeWithWhisperAsync(
        string modelPath,
        float[] samples,
        string? languageCode,
        Action<int>? onProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using WhisperFactory factory = WhisperFactory.FromPath(modelPath);
        WhisperProcessorBuilder builder = factory.CreateBuilder();

        if (string.IsNullOrWhiteSpace(languageCode)
            || string.Equals(languageCode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithLanguageDetection();
        }
        else
        {
            builder = builder.WithLanguage(languageCode.Trim());
        }

        if (onProgress is not null)
        {
            builder = builder.WithProgressHandler(percent => onProgress(percent));
        }

        using WhisperProcessor processor = builder.Build();

        var segments = new List<AlignmentSegment>();
        await foreach (SegmentData seg in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(seg.Text))
            {
                segments.Add(new AlignmentSegment(seg.Start, seg.End, seg.Text));
            }
        }

        return segments;
    }
}
