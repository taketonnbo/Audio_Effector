using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;

namespace AudioEffector.Application.Tests.Common;

[SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "外部ライブラリや OS 起因の予約済み例外の分類・日本語変換動作を単体テストで検証するため")]
public sealed class LrcGenerationErrorMessageResolverTests
{
    /// <summary>
    /// キャンセル要求済みのトークンに起因する OperationCanceledException のみユーザーキャンセルと判定し、
    /// 未キャンセル時の TaskCanceledException（タイムアウト等）はユーザーキャンセルと判定しないことを検証します。
    /// </summary>
    [Fact]
    public void IsUserCancellation_ユーザーキャンセルと内部タイムアウト_正しく区別する()
    {
        // Arrange
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();
        using var activeCts = new CancellationTokenSource();

        var userCanceledEx = new OperationCanceledException(cancelledCts.Token);
        var timeoutCanceledEx = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        // Act
        bool isUserCanceled = LrcGenerationErrorMessageResolver.IsUserCancellation(userCanceledEx, cancelledCts.Token);
        bool isTimeoutUserCanceled = LrcGenerationErrorMessageResolver.IsUserCancellation(timeoutCanceledEx, activeCts.Token);

        // Assert
        Assert.True(isUserCanceled);
        Assert.False(isTimeoutUserCanceled);
    }

    /// <summary>
    /// ネイティブライブラリ欠損を示す各種例外（DllNotFoundException、BadImageFormatException、
    /// Native Library not found を含む FileNotFoundException、TypeInitializationException 経由）を検知することを検証します。
    /// </summary>
    [Fact]
    public void IsNativeLibraryException_各種ネイティブライブラリ例外_すべてTrueを返す()
    {
        // Arrange
        var dllNotFound = new DllNotFoundException("Unable to load DLL 'whisper.dll'");
        var badImage = new BadImageFormatException("An attempt was made to load a program with an incorrect format.");
        var whisperFileNotFound = new FileNotFoundException(
            "Native Library not found in default paths.Verify you have have included the native Whisper library in your application, or install the default libraries with the Whisper.net.Runtime NuGet.");
        var typeInit = new TypeInitializationException("Whisper.net.Native.NativeMethods", dllNotFound);
        var audioFileMissing = new FileNotFoundException("対象の音声ファイルが見つかりません。", @"E:\Music\Song.flac");

        // Act & Assert
        Assert.True(LrcGenerationErrorMessageResolver.IsNativeLibraryException(dllNotFound));
        Assert.True(LrcGenerationErrorMessageResolver.IsNativeLibraryException(badImage));
        Assert.True(LrcGenerationErrorMessageResolver.IsNativeLibraryException(whisperFileNotFound));
        Assert.True(LrcGenerationErrorMessageResolver.IsNativeLibraryException(typeInit));
        Assert.False(LrcGenerationErrorMessageResolver.IsNativeLibraryException(audioFileMissing));
    }

    /// <summary>
    /// 日本語の安全なドメインメッセージは許容し、英語生メッセージやパス露出・OS 既定例外文言は非安全と判定することを検証します。
    /// </summary>
    [Theory]
    [InlineData("発話・歌唱区間を検出できませんでした（無音またはインストゥルメンタル楽曲の可能性があります）。", true)]
    [InlineData("ローカル音声認識モデルが配置されていません。先にモデルをダウンロードまたは指定してください。", true)]
    [InlineData("Native Library not found in default paths.", false)]
    [InlineData("Access to the path 'E:\\Music\\Song.lrc' is denied.", false)]
    [InlineData("パス 'E:\\Music\\Song.lrc' へのアクセスが拒否されました。", false)]
    [InlineData("デバイスが準備できていません。", false)]
    [InlineData("", false)]
    public void IsSafeUserMessage_各種メッセージ文字列_期待される安全性判定を返す(string message, bool expectedSafe)
    {
        // Arrange & Act
        bool actual = LrcGenerationErrorMessageResolver.IsSafeUserMessage(message);

        // Assert
        Assert.Equal(expectedSafe, actual);
    }

    /// <summary>
    /// LRC 生成時の各種未ラップ例外が英語生メッセージを露出せず、原因カテゴリ別の日本語案内へ変換されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveGenerationErrorMessage_各種未ラップ例外_原因別の日本語メッセージへ変換する()
    {
        // Arrange
        var nativeMissing = new FileNotFoundException(
            "Native Library not found in default paths.Verify you have have included the native Whisper library.");
        var oom = new OutOfMemoryException("Insufficient memory to continue the execution of the program.");
        var comError = new COMException("MF_E_INVALIDMEDIATYPE", unchecked((int)0xC00D36B4));
        var usbRemoved = new IOException("The device is not ready.", unchecked((int)0x80070015));
        var unknownEnglish = new Exception("Unexpected native exception occurred.");
        var domainNoSpeech = new InvalidOperationException("発話・歌唱区間を検出できませんでした（無音またはインストゥルメンタル楽曲の可能性があります）。");

        // Act
        string nativeMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(nativeMissing);
        string oomMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(oom);
        string comMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(comError);
        string usbMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(usbRemoved);
        string unknownMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(unknownEnglish);
        string domainMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(domainNoSpeech);

        // Assert
        Assert.Equal(LrcGenerationErrorMessageResolver.NATIVE_RUNTIME_UNAVAILABLE_MESSAGE, nativeMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.OUT_OF_MEMORY_MESSAGE, oomMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_DECODE_FAILED_MESSAGE, comMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_FILE_ACCESS_FAILED_MESSAGE, usbMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.DEFAULT_GENERATION_ERROR_MESSAGE, unknownMsg);
        Assert.Equal(domainNoSpeech.Message, domainMsg);
    }

    /// <summary>
    /// モデルダウンロードおよび LRC 保存時のネットワーク・タイムアウト・ディスク容量不足・アクセス権エラーが
    /// 適切な日本語メッセージへ変換されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveModelDownloadAndSaveErrorMessage_各種障害例外_原因別の日本語メッセージへ変換する()
    {
        // Arrange
        var httpError = new HttpRequestException("No such host is known.");
        var timeoutError = new TaskCanceledException("HttpClient.Timeout exceeded.");
        var diskFullError = new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
        var accessDenied = new UnauthorizedAccessException("Access to the path 'E:\\Song.lrc' is denied.");
        var usbIoError = new IOException("The device is not ready.", unchecked((int)0x80070015));

        // Act
        string networkMsg = LrcGenerationErrorMessageResolver.ResolveModelDownloadErrorMessage(httpError);
        string timeoutMsg = LrcGenerationErrorMessageResolver.ResolveModelDownloadErrorMessage(timeoutError, isTimeout: true);
        string dlDiskFullMsg = LrcGenerationErrorMessageResolver.ResolveModelDownloadErrorMessage(diskFullError);
        string saveDeniedMsg = LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(accessDenied);
        string saveDiskFullMsg = LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(diskFullError);
        string saveUsbMsg = LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(usbIoError);

        // Assert
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_NETWORK_ERROR_MESSAGE, networkMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_TIMEOUT_MESSAGE, timeoutMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_DISK_FULL_MESSAGE, dlDiskFullMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.SAVE_LRC_ACCESS_DENIED_MESSAGE, saveDeniedMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.SAVE_LRC_DISK_FULL_MESSAGE, saveDiskFullMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.SAVE_LRC_IO_ERROR_MESSAGE, saveUsbMsg);
    }

    /// <summary>
    /// FileNotFoundException（既定 HResult 0x80070002）でモデル未配置・テキスト未存在の既存日本語ドメインメッセージが保持され、
    /// OS 既定の相対パス欠損文言は AUDIO_FILE_ACCESS_FAILED_MESSAGE へ変換されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveGenerationErrorMessage_モデル未配置やテキスト未存在のFileNotFoundExceptionとOS既定文言_適切に判別する()
    {
        // Arrange
        var modelMissing = new FileNotFoundException(
            "ローカル音声認識モデルが配置されていません。先にモデルをダウンロードまたは指定してください。",
            @"C:\models\ggml-base.bin");
        var textMissing = new FileNotFoundException(
            "指定されたテキストファイルが見つかりません。",
            @"C:\lyrics\song.txt");
        var osRelativeMissing = new FileNotFoundException("ファイル 'foo.wav' が見つかりませんでした。");
        var osDirMissing = new DirectoryNotFoundException("パス 'foo' の一部が見つかりませんでした。");
        var truncatedAudio = new EndOfStreamException("Unable to read beyond the end of the stream.");

        // Act
        string modelMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(modelMissing);
        string textMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(textMissing);
        string osFileMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(osRelativeMissing);
        string osDirMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(osDirMissing);
        string eofMsg = LrcGenerationErrorMessageResolver.ResolveGenerationErrorMessage(truncatedAudio);

        // Assert
        Assert.Equal(modelMissing.Message, modelMsg);
        Assert.Equal(textMissing.Message, textMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_FILE_ACCESS_FAILED_MESSAGE, osFileMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_FILE_ACCESS_FAILED_MESSAGE, osDirMsg);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_DECODE_FAILED_MESSAGE, eofMsg);
    }

    /// <summary>
    /// ResolveModelSelectionErrorMessage で FileNotFoundException・ArgumentException（paramName 付き）・InvalidDataException の
    /// 既存日本語ドメインメッセージが保持され、英語例外や I/O 例外は安全な日本語案内へ変換されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveModelSelectionErrorMessage_FileNotFoundとArgumentとIO例外_ドメインメッセージを保持し生例外を遮断する()
    {
        // Arrange
        var customNotFound = new FileNotFoundException("指定されたローカルモデルファイルが見つかりません。", @"C:\models\missing.bin");
        var invalidModelId = new ArgumentException("未対応のローカルモデル識別子です: unknown", "modelId");
        var emptyModel = new InvalidDataException("指定されたローカルモデルファイルが空（0 バイト）のため使用できません。");
        var englishNotFound = new FileNotFoundException("Could not find file 'C:\\models\\missing.bin'.");
        var pathTooLong = new PathTooLongException("The path is too long.");
        var ioError = new IOException("The device is not ready.", unchecked((int)0x80070015));

        // Act
        string customNotFoundMsg = LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(customNotFound);
        string invalidModelIdMsg = LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(invalidModelId);
        string emptyModelMsg = LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(emptyModel);
        string englishNotFoundMsg = LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(englishNotFound);
        string pathTooLongMsg = LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(pathTooLong);
        string ioErrorMsg = LrcGenerationErrorMessageResolver.ResolveModelSelectionErrorMessage(ioError);

        // Assert
        Assert.Equal("指定されたローカルモデルファイルが見つかりません。", customNotFoundMsg);
        Assert.Equal("未対応のローカルモデル識別子です: unknown", invalidModelIdMsg);
        Assert.DoesNotContain("Parameter", invalidModelIdMsg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(emptyModel.Message, emptyModelMsg);
        Assert.DoesNotContain("Could not find", englishNotFoundMsg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("パス", pathTooLongMsg, StringComparison.Ordinal);
        Assert.DoesNotContain("not ready", ioErrorMsg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PathTooLongException が IOException に吸収されずパス長超過専用メッセージへ到達すること、および
    /// AggregateException に入れ子になった TaskCanceledException がタイムアウト案内へ分類されることを検証します。
    /// </summary>
    [Fact]
    public void ResolveSaveAndTextAndDownload_PathTooLongと入れ子TaskCanceled_期待する専用メッセージへ到達する()
    {
        // Arrange
        var pathTooLong = new PathTooLongException("The specified path, file name, or both are too long.");
        var dirNotFound = new DirectoryNotFoundException("Could not find a part of the path 'E:\\Music'.");
        var nestedTimeout = new AggregateException(new TaskCanceledException("HttpClient.Timeout exceeded."));

        // Act
        string savePathTooLong = LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(pathTooLong);
        string saveDirNotFound = LrcGenerationErrorMessageResolver.ResolveSaveLrcErrorMessage(dirNotFound);
        string textPathTooLong = LrcGenerationErrorMessageResolver.ResolveTextReadErrorMessage(pathTooLong, isExistingLrc: false);
        string existingLrcPathTooLong = LrcGenerationErrorMessageResolver.ResolveTextReadErrorMessage(pathTooLong, isExistingLrc: true);
        string downloadNestedTimeout = LrcGenerationErrorMessageResolver.ResolveModelDownloadErrorMessage(nestedTimeout);

        // Assert
        Assert.Contains("長すぎる", savePathTooLong, StringComparison.Ordinal);
        Assert.Equal(LrcGenerationErrorMessageResolver.SAVE_LRC_IO_ERROR_MESSAGE, saveDirNotFound);
        Assert.Contains("パスが不正", textPathTooLong, StringComparison.Ordinal);
        Assert.Contains("パスが不正", existingLrcPathTooLong, StringComparison.Ordinal);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_TIMEOUT_MESSAGE, downloadNestedTimeout);
    }
}
