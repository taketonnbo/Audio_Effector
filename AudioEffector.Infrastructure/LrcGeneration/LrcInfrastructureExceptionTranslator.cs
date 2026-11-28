using System;
using System.IO;
using System.Runtime.InteropServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.ValueObjects;
using NAudio;

namespace AudioEffector.Infrastructure.LrcGeneration;

/// <summary>
/// Infrastructure 層（<c>Whisper.net</c>・<c>NAudio</c>・OS ファイル I/O・HTTP 通信）で発生する外部例外を
/// 原因カテゴリ別の <see cref="LrcGenerationException"/> へ変換する内部トランスレーターです
/// </summary>
internal static class LrcInfrastructureExceptionTranslator
{
    private static readonly string[] ModelLoadMessageIndicators =
    [
        "model",
        "ggml",
        "whisper_init",
        "failed to load",
        "invalid magic",
        "corrupt",
    ];

    /// <summary>
    /// 音声デコードフェーズ（<c>NAudio</c> / PCM 変換）で発生した例外を分類・ラップします
    /// </summary>
    /// <param name="exception">捕捉した元の例外</param>
    /// <param name="isLogged">呼び出し元で NLog 記録済みかどうか</param>
    /// <returns>ラップされた <see cref="LrcGenerationException"/></returns>
    public static LrcGenerationException TranslateDecodeException(Exception exception, bool isLogged = true)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException existing)
        {
            return existing;
        }

        if (LrcGenerationErrorMessageResolver.IsNativeLibraryException(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.NativeRuntimeUnavailable,
                LrcGenerationErrorMessageResolver.NATIVE_RUNTIME_UNAVAILABLE_MESSAGE,
                exception,
                isLogged);
        }

        if (LrcGenerationErrorMessageResolver.ContainsException<OutOfMemoryException>(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.OutOfMemory,
                LrcGenerationErrorMessageResolver.OUT_OF_MEMORY_MESSAGE,
                exception,
                isLogged);
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.AudioFileAccessFailed,
                LrcGenerationErrorMessageResolver.AUDIO_FILE_ACCESS_FAILED_MESSAGE,
                exception,
                isLogged);
        }

        if (IsAudioDecodeFailure(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.AudioDecodeFailed,
                LrcGenerationErrorMessageResolver.AUDIO_DECODE_FAILED_MESSAGE,
                exception,
                isLogged);
        }

        return new LrcGenerationException(
            LrcGenerationErrorCategory.Unexpected,
            LrcGenerationErrorMessageResolver.DEFAULT_GENERATION_ERROR_MESSAGE,
            exception,
            isLogged);
    }

    /// <summary>
    /// ローカルモデル読み込みフェーズ（<c>WhisperFactory.FromPath</c> 等）で発生した例外を分類・ラップします
    /// </summary>
    /// <param name="exception">捕捉した元の例外</param>
    /// <param name="isLogged">呼び出し元で NLog 記録済みかどうか</param>
    /// <returns>ラップされた <see cref="LrcGenerationException"/></returns>
    public static LrcGenerationException TranslateModelLoadException(Exception exception, bool isLogged = true)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException existing)
        {
            return existing;
        }

        if (LrcGenerationErrorMessageResolver.IsNativeLibraryException(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.NativeRuntimeUnavailable,
                LrcGenerationErrorMessageResolver.NATIVE_RUNTIME_UNAVAILABLE_MESSAGE,
                exception,
                isLogged);
        }

        if (LrcGenerationErrorMessageResolver.ContainsException<OutOfMemoryException>(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.OutOfMemory,
                LrcGenerationErrorMessageResolver.OUT_OF_MEMORY_MESSAGE,
                exception,
                isLogged);
        }

        return new LrcGenerationException(
            LrcGenerationErrorCategory.ModelLoadFailed,
            LrcGenerationErrorMessageResolver.MODEL_LOAD_FAILED_MESSAGE,
            exception,
            isLogged);
    }

    /// <summary>
    /// 音声認識・文字起こしフェーズで発生した例外を分類・ラップします
    /// </summary>
    /// <param name="exception">捕捉した元の例外</param>
    /// <param name="phase">例外発生時の解析フェーズ</param>
    /// <param name="isLogged">呼び出し元で NLog 記録済みかどうか</param>
    /// <returns>ラップされた <see cref="LrcGenerationException"/></returns>
    public static LrcGenerationException TranslateTranscriptionException(
        Exception exception,
        AlignmentPhase phase,
        bool isLogged = true)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException existing)
        {
            return existing;
        }

        if (LrcGenerationErrorMessageResolver.IsNativeLibraryException(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.NativeRuntimeUnavailable,
                LrcGenerationErrorMessageResolver.NATIVE_RUNTIME_UNAVAILABLE_MESSAGE,
                exception,
                isLogged);
        }

        if (LrcGenerationErrorMessageResolver.ContainsException<OutOfMemoryException>(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.OutOfMemory,
                LrcGenerationErrorMessageResolver.OUT_OF_MEMORY_MESSAGE,
                exception,
                isLogged);
        }

        if (phase == AlignmentPhase.LoadingModel && IsModelLoadFailure(exception))
        {
            return new LrcGenerationException(
                LrcGenerationErrorCategory.ModelLoadFailed,
                LrcGenerationErrorMessageResolver.MODEL_LOAD_FAILED_MESSAGE,
                exception,
                isLogged);
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return phase == AlignmentPhase.LoadingModel
                ? new LrcGenerationException(
                    LrcGenerationErrorCategory.ModelLoadFailed,
                    LrcGenerationErrorMessageResolver.MODEL_LOAD_FAILED_MESSAGE,
                    exception,
                    isLogged)
                : new LrcGenerationException(
                    LrcGenerationErrorCategory.AudioFileAccessFailed,
                    LrcGenerationErrorMessageResolver.AUDIO_FILE_ACCESS_FAILED_MESSAGE,
                    exception,
                    isLogged);
        }

        return new LrcGenerationException(
            LrcGenerationErrorCategory.Unexpected,
            LrcGenerationErrorMessageResolver.DEFAULT_GENERATION_ERROR_MESSAGE,
            exception,
            isLogged);
    }

    /// <summary>
    /// モデルダウンロード処理で発生した例外を分類・ラップします
    /// </summary>
    /// <param name="exception">捕捉した元の例外</param>
    /// <param name="isTimeout">タイムアウト由来の失敗かどうか</param>
    /// <param name="isLogged">呼び出し元で NLog 記録済みかどうか</param>
    /// <returns>ラップされた <see cref="LrcGenerationException"/></returns>
    public static LrcGenerationException TranslateModelDownloadException(
        Exception exception,
        bool isTimeout,
        bool isLogged = true)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException existing)
        {
            return existing;
        }

        LrcGenerationErrorCategory category = LrcGenerationErrorMessageResolver.ContainsException<OutOfMemoryException>(exception)
            ? LrcGenerationErrorCategory.OutOfMemory
            : LrcGenerationErrorCategory.ModelDownloadFailed;

        string message = LrcGenerationErrorMessageResolver.ResolveModelDownloadErrorMessage(exception, isTimeout);
        return new LrcGenerationException(category, message, exception, isLogged);
    }

    private static bool IsAudioDecodeFailure(Exception exception)
    {
        if (LrcGenerationErrorMessageResolver.ContainsException<FormatException>(exception)
            || LrcGenerationErrorMessageResolver.ContainsException<InvalidDataException>(exception)
            || LrcGenerationErrorMessageResolver.ContainsException<COMException>(exception)
            || LrcGenerationErrorMessageResolver.ContainsException<MmException>(exception)
            || LrcGenerationErrorMessageResolver.ContainsException<NotSupportedException>(exception))
        {
            return true;
        }

        string? ns = exception.GetType().Namespace;
        return !string.IsNullOrEmpty(ns) && ns.StartsWith("NAudio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModelLoadFailure(Exception exception)
    {
        if (LrcGenerationErrorMessageResolver.ContainsException<InvalidDataException>(exception)
            || LrcGenerationErrorMessageResolver.ContainsException<FileLoadException>(exception))
        {
            return true;
        }

        string? fullName = exception.GetType().FullName;
        if (!string.IsNullOrEmpty(fullName)
            && fullName.Contains("Whisper", StringComparison.OrdinalIgnoreCase)
            && fullName.Contains("Load", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(exception.Message))
        {
            return false;
        }

        foreach (string indicator in ModelLoadMessageIndicators)
        {
            if (exception.Message.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
