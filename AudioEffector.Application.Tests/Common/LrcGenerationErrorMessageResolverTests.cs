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
}
