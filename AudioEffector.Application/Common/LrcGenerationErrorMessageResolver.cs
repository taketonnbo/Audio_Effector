using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;

namespace AudioEffector.Application.Common;

/// <summary>
/// LRC 自動生成機能で発生する例外をユーザー向けの安全な日本語エラーメッセージへ分類・正規化するヘルパークラスです
/// </summary>
public static class LrcGenerationErrorMessageResolver
{
    /// <summary>
    /// ネイティブライブラリ欠損・読込失敗時の既定メッセージ
    /// </summary>
    public const string NATIVE_RUNTIME_UNAVAILABLE_MESSAGE =
        "音声認識エンジンの実行ファイルが見つからないか読み込めませんでした。アプリケーションの再起動または再インストールをお試しください。";

    /// <summary>
    /// 音声ファイルアクセス失敗時の既定メッセージ
    /// </summary>
    public const string AUDIO_FILE_ACCESS_FAILED_MESSAGE =
        "音声ファイルへアクセスできませんでした。ファイルの存在や、USB メモリ・ドライブの接続状態およびアクセス権限をご確認ください。";

    /// <summary>
    /// 音声デコード失敗時の既定メッセージ
    /// </summary>
    public const string AUDIO_DECODE_FAILED_MESSAGE =
        "音声ファイルの読み込み・デコードに失敗しました。ファイルが破損していないか、対応形式であることをご確認ください。";

    /// <summary>
    /// モデルファイル破損・読込失敗時の既定メッセージ
    /// </summary>
    public const string MODEL_LOAD_FAILED_MESSAGE =
        "音声認識モデルの読み込みに失敗しました。モデルを再ダウンロードするか、別のモデルファイルを選択してください。";

    /// <summary>
    /// メモリ不足時の既定メッセージ
    /// </summary>
    public const string OUT_OF_MEMORY_MESSAGE =
        "メモリが不足しているため処理を完了できませんでした。他のアプリを終了するか、軽量なモデル（tiny）へ切り替えてお試しください。";

    /// <summary>
    /// 音声解析の予期しないエラー時の既定メッセージ
    /// </summary>
    public const string DEFAULT_GENERATION_ERROR_MESSAGE =
        "音声解析中に予期しないエラーが発生しました。時間をおいて再試行するか、ログファイルをご確認ください。";

    /// <summary>
    /// モデルダウンロードのネットワーク障害時の既定メッセージ
    /// </summary>
    public const string MODEL_DOWNLOAD_NETWORK_ERROR_MESSAGE =
        "モデルのダウンロード中にネットワークエラーが発生しました。インターネット接続やプロキシ・ファイアウォール設定をご確認のうえ、再試行してください。";

    /// <summary>
    /// モデルダウンロードのタイムアウト時の既定メッセージ
    /// </summary>
    public const string MODEL_DOWNLOAD_TIMEOUT_MESSAGE =
        "モデルのダウンロードがタイムアウトしました。インターネット接続をご確認のうえ、時間をおいて再試行してください。";

    /// <summary>
    /// モデルダウンロードのディスク容量不足時の既定メッセージ
    /// </summary>
    public const string MODEL_DOWNLOAD_DISK_FULL_MESSAGE =
        "ドライブの空き容量が不足しているためモデルを保存できませんでした。空き容量を確保してから再試行してください。";

    /// <summary>
    /// モデルダウンロードの権限不足時の既定メッセージ
    /// </summary>
    public const string MODEL_DOWNLOAD_ACCESS_DENIED_MESSAGE =
        "モデル保存先フォルダへの書き込み権限がありません。保存先フォルダのアクセス権限をご確認のうえ、再試行してください。";

    /// <summary>
    /// モデルダウンロードの保存 I/O 失敗時の既定メッセージ
    /// </summary>
    public const string MODEL_DOWNLOAD_IO_ERROR_MESSAGE =
        "モデルファイルの保存に失敗しました。保存先ドライブの接続状態や空き容量をご確認のうえ、再試行してください。";

    /// <summary>
    /// モデルダウンロードの予期しないエラー時の既定メッセージ
    /// </summary>
    public const string DEFAULT_MODEL_DOWNLOAD_ERROR_MESSAGE =
        "モデルのダウンロード中に予期しないエラーが発生しました。インターネット接続をご確認のうえ、時間をおいて再試行してください。";

    /// <summary>
    /// LRC 保存時の書き込み権限不足メッセージ
    /// </summary>
    public const string SAVE_LRC_ACCESS_DENIED_MESSAGE =
        "LRC ファイルを保存できませんでした。保存先フォルダや USB メモリ・ドライブの書き込み権限（読み取り専用・書き込み保護）をご確認ください。";

    /// <summary>
    /// LRC 保存時のディスク容量不足メッセージ
    /// </summary>
    public const string SAVE_LRC_DISK_FULL_MESSAGE =
        "保存先ドライブの空き容量が不足しているため、LRC ファイルを保存できませんでした。空き容量を確保してから再試行してください。";

    /// <summary>
    /// LRC 保存時の I/O・接続エラーメッセージ
    /// </summary>
    public const string SAVE_LRC_IO_ERROR_MESSAGE =
        "LRC ファイルを保存できませんでした。保存先フォルダや USB メモリ・ドライブの接続状態、および他アプリでファイルが開かれていないかご確認ください。";

    /// <summary>
    /// LRC 保存時の予期しないエラーメッセージ
    /// </summary>
    public const string DEFAULT_SAVE_LRC_ERROR_MESSAGE =
        "LRC ファイルの保存中に予期しないエラーが発生しました。時間をおいて再試行するか、ログファイルをご確認ください。";

    private const int HRESULT_FACILITY_MASK = unchecked((int)0xFFFF0000);
    private const int HRESULT_FACILITY_WIN32 = unchecked((int)0x80070000);
    private const int HRESULT_ERROR_DISK_FULL = unchecked((int)0x80070070);
    private const int HRESULT_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
    private const int MAX_SAFE_MESSAGE_LENGTH = 300;

    private static readonly string[] NativeLibraryIndicators =
    [
        "native library not found",
        "native whisper library",
        "whisper.net.runtime",
        "whisper.dll",
        "ggml.dll",
        "ggml-base.dll",
        "ggml-cpu.dll",
        "unable to load dll",
        "unable to load shared library",
        "failed to load native",
    ];

    private static readonly string[] TechnicalOrSystemMessageIndicators =
    [
        "native library",
        "whisper.net",
        "whisper.dll",
        "nuget",
        "hresult",
        "0x80",
        "stacktrace",
        "exception",
        " at ",
        ":\\",
        "\\\\",
        "へのアクセスが拒否されました",
        "別のプロセスで使用されているため",
        "デバイスが準備できていません",
        "指定されたパスが見つかりません",
        "指定されたファイルが見つかりません",
        "ディスクに十分な空き領域がありません",
        "メディアは書き込み禁止",
        "ネットワーク パスが見つかりません",
        "ファイル '",
        "パス '",
        "ファイルまたはアセンブリ",
    ];

    /// <summary>
    /// 捕捉した <see cref="OperationCanceledException"/> がユーザーキャンセル（対象トークンのキャンセル要求）由来かどうかを判定します
    /// </summary>
    /// <param name="exception">捕捉したキャンセル例外</param>
    /// <param name="cancellationToken">呼び出し元のキャンセレーショントークン</param>
    /// <returns>ユーザーキャンセル由来の場合は <see langword="true"/>、内部タイムアウト等の場合は <see langword="false"/></returns>
    public static bool IsUserCancellation(OperationCanceledException exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        return exception.CancellationToken != CancellationToken.None
            && exception.CancellationToken == cancellationToken;
    }

    /// <summary>
    /// 指定された <see cref="IOException"/> がディスク容量不足エラーかどうかを判定します
    /// </summary>
    /// <param name="exception">判定対象の I/O 例外</param>
    /// <returns>ディスク容量不足の場合は <see langword="true"/></returns>
    public static bool IsDiskFullException(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.HResult == HRESULT_ERROR_DISK_FULL
            || exception.HResult == HRESULT_ERROR_HANDLE_DISK_FULL;
    }

    /// <summary>
    /// 指定された例外または内部例外がネイティブライブラリ欠損・読み込み失敗に該当するかどうかを判定します
    /// </summary>
    /// <param name="exception">判定対象の例外</param>
    /// <returns>ネイティブライブラリ欠損・読込失敗の場合は <see langword="true"/></returns>
    public static bool IsNativeLibraryException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return MatchesAnyInChain(exception, static current =>
        {
            if (current is DllNotFoundException or BadImageFormatException)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(current.Message))
            {
                return false;
            }

            foreach (string indicator in NativeLibraryIndicators)
            {
                if (current.Message.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        });
    }

    /// <summary>
    /// 指定された例外チェーン内に特定の例外型が含まれるかどうかを判定します
    /// </summary>
    /// <typeparam name="TException">検索する例外型</typeparam>
    /// <param name="exception">起点となる例外</param>
    /// <returns>該当する例外が含まれる場合は <see langword="true"/></returns>
    public static bool ContainsException<TException>(Exception exception)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        return MatchesAnyInChain(exception, static current => current is TException);
    }

    /// <summary>
    /// 指定されたメッセージが英語生例外や技術的パス情報を含まない安全な日本語ユーザーメッセージかどうかを判定します
    /// </summary>
    /// <param name="message">判定対象のメッセージ文字列</param>
    /// <returns>UI へ安全に表示できる日本語メッセージの場合は <see langword="true"/></returns>
    public static bool IsSafeUserMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string trimmed = message.Trim();
        if (trimmed.Length > MAX_SAFE_MESSAGE_LENGTH || trimmed.Contains('\n', StringComparison.Ordinal) || trimmed.Contains('\r', StringComparison.Ordinal))
        {
            return false;
        }

        if (!ContainsJapaneseCharacter(trimmed))
        {
            return false;
        }

        foreach (string indicator in TechnicalOrSystemMessageIndicators)
        {
            if (trimmed.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// LRC 生成・音声直接文字起こしで発生した例外をユーザー向けの安全な日本語メッセージへ変換します
    /// </summary>
    /// <param name="exception">発生した例外</param>
    /// <returns>ユーザー向け日本語エラーメッセージ</returns>
    public static string ResolveGenerationErrorMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException lrcEx)
        {
            return IsSafeUserMessage(lrcEx.Message) ? lrcEx.Message : DEFAULT_GENERATION_ERROR_MESSAGE;
        }

        if (IsNativeLibraryException(exception))
        {
            return NATIVE_RUNTIME_UNAVAILABLE_MESSAGE;
        }

        if (ContainsException<OutOfMemoryException>(exception))
        {
            return OUT_OF_MEMORY_MESSAGE;
        }

        if (exception is FileNotFoundException fnf)
        {
            return IsSafeUserMessage(fnf.Message)
                ? fnf.Message
                : AUDIO_FILE_ACCESS_FAILED_MESSAGE;
        }

        if (ContainsException<EndOfStreamException>(exception))
        {
            return AUDIO_DECODE_FAILED_MESSAGE;
        }

        if (exception is PathTooLongException)
        {
            return AUDIO_FILE_ACCESS_FAILED_MESSAGE;
        }

        if (exception is IOException ioEx && IsDiskFullException(ioEx))
        {
            return "ドライブの空き容量が不足しているため処理を完了できませんでした。空き容量を確保してから再試行してください。";
        }

        if (exception is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return !IsWin32HResult(exception) && IsSafeUserMessage(exception.Message)
                ? exception.Message
                : AUDIO_FILE_ACCESS_FAILED_MESSAGE;
        }

        if (ContainsException<COMException>(exception))
        {
            return AUDIO_DECODE_FAILED_MESSAGE;
        }

        if (exception is FormatException or InvalidDataException)
        {
            return IsSafeUserMessage(exception.Message)
                ? exception.Message
                : AUDIO_DECODE_FAILED_MESSAGE;
        }

        if (exception is InvalidOperationException)
        {
            return IsSafeUserMessage(exception.Message)
                ? exception.Message
                : DEFAULT_GENERATION_ERROR_MESSAGE;
        }

        return DEFAULT_GENERATION_ERROR_MESSAGE;
    }

    /// <summary>
    /// モデルダウンロード処理で発生した例外をユーザー向けの安全な日本語メッセージへ変換します
    /// </summary>
    /// <param name="exception">発生した例外</param>
    /// <param name="isTimeout">タイムアウト由来の失敗と判定済みかどうか</param>
    /// <returns>ユーザー向け日本語エラーメッセージ</returns>
    public static string ResolveModelDownloadErrorMessage(Exception exception, bool isTimeout = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException lrcEx)
        {
            return IsSafeUserMessage(lrcEx.Message) ? lrcEx.Message : DEFAULT_MODEL_DOWNLOAD_ERROR_MESSAGE;
        }

        if (isTimeout || ContainsException<TimeoutException>(exception) || ContainsException<TaskCanceledException>(exception))
        {
            return MODEL_DOWNLOAD_TIMEOUT_MESSAGE;
        }

        if (ContainsException<HttpRequestException>(exception))
        {
            return MODEL_DOWNLOAD_NETWORK_ERROR_MESSAGE;
        }

        if (ContainsException<OutOfMemoryException>(exception))
        {
            return "メモリが不足しているためモデルのダウンロードを完了できませんでした。他のアプリを終了してから再試行してください。";
        }

        if (exception is UnauthorizedAccessException)
        {
            return MODEL_DOWNLOAD_ACCESS_DENIED_MESSAGE;
        }

        if (exception is IOException ioEx && IsDiskFullException(ioEx))
        {
            return MODEL_DOWNLOAD_DISK_FULL_MESSAGE;
        }

        if (exception is DirectoryNotFoundException or IOException)
        {
            return MODEL_DOWNLOAD_IO_ERROR_MESSAGE;
        }

        if (exception is InvalidDataException or ArgumentException && IsSafeUserMessage(exception.Message))
        {
            return exception.Message;
        }

        return DEFAULT_MODEL_DOWNLOAD_ERROR_MESSAGE;
    }

    /// <summary>
    /// LRC ファイル保存処理で発生した例外をユーザー向けの安全な日本語メッセージへ変換します
    /// </summary>
    /// <param name="exception">発生した例外</param>
    /// <returns>ユーザー向け日本語エラーメッセージ</returns>
    public static string ResolveSaveLrcErrorMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException lrcEx)
        {
            return IsSafeUserMessage(lrcEx.Message) ? lrcEx.Message : DEFAULT_SAVE_LRC_ERROR_MESSAGE;
        }

        if (exception is PathTooLongException or ArgumentException or NotSupportedException)
        {
            return "保存先パスが不正または長すぎるため、LRC ファイルを保存できませんでした。";
        }

        if (exception is UnauthorizedAccessException)
        {
            return SAVE_LRC_ACCESS_DENIED_MESSAGE;
        }

        // DirectoryNotFoundException を含む IOException 派生型をここで処理する
        if (exception is IOException ioEx)
        {
            if (IsDiskFullException(ioEx))
            {
                return SAVE_LRC_DISK_FULL_MESSAGE;
            }

            if (ioEx.Message.Contains("上書き未承諾", StringComparison.Ordinal))
            {
                return "同名の .lrc ファイルが既に存在するため保存できませんでした。上書き保存を承諾してから再試行してください。";
            }

            return SAVE_LRC_IO_ERROR_MESSAGE;
        }

        return DEFAULT_SAVE_LRC_ERROR_MESSAGE;
    }

    /// <summary>
    /// 歌詞テキストまたは既存 LRC ファイルの読み込み処理で発生した例外をユーザー向けの安全な日本語メッセージへ変換します
    /// </summary>
    /// <param name="exception">発生した例外</param>
    /// <param name="isExistingLrc">既存 `.lrc` ファイルの読み込みかどうか</param>
    /// <returns>ユーザー向け日本語エラーメッセージ</returns>
    public static string ResolveTextReadErrorMessage(Exception exception, bool isExistingLrc = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException lrcEx)
        {
            return IsSafeUserMessage(lrcEx.Message)
                ? lrcEx.Message
                : (isExistingLrc
                    ? "既存の .lrc ファイルの読み込み中に予期しないエラーが発生しました。ログファイルをご確認ください。"
                    : "テキストファイルの読み込み中に予期しないエラーが発生しました。ログファイルをご確認ください。");
        }

        if (exception is FileNotFoundException)
        {
            return isExistingLrc
                ? "既存の .lrc ファイルが見つかりませんでした。"
                : "指定されたテキストファイルが見つかりません。";
        }

        if (exception is PathTooLongException or ArgumentException or NotSupportedException)
        {
            string candidate = exception is ArgumentException argEx
                ? StripArgumentParameterSuffix(argEx)
                : exception.Message;
            if (IsSafeUserMessage(candidate))
            {
                return candidate;
            }

            return isExistingLrc
                ? "既存の .lrc ファイルのパスが不正です。"
                : "指定されたテキストファイルのパスが不正です。";
        }

        if (exception is UnauthorizedAccessException)
        {
            return isExistingLrc
                ? "既存の .lrc ファイルへアクセスできませんでした。アクセス権限や USB メモリ・ドライブの接続状態をご確認ください。"
                : "テキストファイルへアクセスできませんでした。アクセス権限や USB メモリ・ドライブの接続状態をご確認ください。";
        }

        // DirectoryNotFoundException を含む IOException 派生型をここで処理する
        if (exception is IOException)
        {
            if (!IsWin32HResult(exception) && IsSafeUserMessage(exception.Message))
            {
                return isExistingLrc
                    ? $"既存の .lrc ファイルの読み込みに失敗しました: {exception.Message}"
                    : $"テキストファイルの読み込みに失敗しました: {exception.Message}";
            }

            return isExistingLrc
                ? "既存の .lrc ファイルを読み込めませんでした。ファイルの存在や USB メモリ・ドライブの接続状態をご確認ください。"
                : "テキストファイルを読み込めませんでした。ファイルの存在や USB メモリ・ドライブの接続状態をご確認ください。";
        }

        return isExistingLrc
            ? "既存の .lrc ファイルの読み込み中に予期しないエラーが発生しました。ログファイルをご確認ください。"
            : "テキストファイルの読み込み中に予期しないエラーが発生しました。ログファイルをご確認ください。";
    }

    /// <summary>
    /// ローカルモデルの選択・手動ファイル指定で発生した例外をユーザー向けの安全な日本語メッセージへ変換します
    /// </summary>
    /// <param name="exception">発生した例外</param>
    /// <returns>ユーザー向け日本語エラーメッセージ</returns>
    public static string ResolveModelSelectionErrorMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LrcGenerationException lrcEx)
        {
            return IsSafeUserMessage(lrcEx.Message)
                ? lrcEx.Message
                : "モデル設定の更新に失敗しました。モデルファイルの状態をご確認ください。";
        }

        if (exception is FileNotFoundException fnf)
        {
            return IsSafeUserMessage(fnf.Message)
                ? fnf.Message
                : "指定されたモデルファイルが見つかりませんでした。ファイルの存在をご確認ください。";
        }

        if (exception is PathTooLongException)
        {
            return "指定されたモデルファイルのパスが不正または長すぎます。";
        }

        if (exception is ArgumentException argEx)
        {
            string cleaned = StripArgumentParameterSuffix(argEx);
            return IsSafeUserMessage(cleaned)
                ? cleaned
                : "指定されたモデル設定が不正です。選択内容をご確認ください。";
        }

        if (exception is InvalidDataException)
        {
            return IsSafeUserMessage(exception.Message)
                ? exception.Message
                : "指定されたモデルファイルを使用できませんでした。ファイルの存在や形式をご確認ください。";
        }

        if (exception is UnauthorizedAccessException)
        {
            return "指定されたモデルファイルへアクセスできませんでした。アクセス権限やドライブの接続状態をご確認ください。";
        }

        // DirectoryNotFoundException を含む IOException 派生型をここで処理する
        if (exception is IOException)
        {
            return "指定されたモデルファイルを読み込めませんでした。ファイルの存在やドライブの接続状態をご確認ください。";
        }

        return "モデル設定の更新に失敗しました。モデルファイルの状態をご確認ください。";
    }

    /// <summary>
    /// 既に文字列化されたエラーメッセージが UI へ安全に表示可能かを確認し、生英語や空文字列であればフォールバックを返します
    /// </summary>
    /// <param name="message">検査対象のメッセージ</param>
    /// <param name="fallbackMessage">安全でない場合に返す既定メッセージ</param>
    /// <returns>安全なユーザー向け日本語メッセージ</returns>
    public static string EnsureSafeUserMessage(string? message, string fallbackMessage)
    {
        return IsSafeUserMessage(message) ? message!.Trim() : fallbackMessage;
    }

    private static string StripArgumentParameterSuffix(ArgumentException exception)
    {
        string message = exception.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exception.ParamName))
        {
            return message.Trim();
        }

        string modernSuffix = $" (Parameter '{exception.ParamName}')";
        if (message.EndsWith(modernSuffix, StringComparison.Ordinal))
        {
            return message[..^modernSuffix.Length].Trim();
        }

        int lineBreakIndex = message.IndexOfAny(['\r', '\n']);
        if (lineBreakIndex > 0)
        {
            return message[..lineBreakIndex].Trim();
        }

        return message.Trim();
    }

    private static bool IsWin32HResult(Exception exception)
    {
        return (exception.HResult & HRESULT_FACILITY_MASK) == HRESULT_FACILITY_WIN32;
    }

    private static bool ContainsJapaneseCharacter(string text)
    {
        foreach (char c in text)
        {
            if ((c >= '\u3040' && c <= '\u309F')
                || (c >= '\u30A0' && c <= '\u30FF')
                || (c >= '\u4E00' && c <= '\u9FFF'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyInChain(Exception root, Func<Exception, bool> predicate)
    {
        if (predicate(root))
        {
            return true;
        }

        if (root is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                if (MatchesAnyInChain(inner, predicate))
                {
                    return true;
                }
            }
        }

        return root.InnerException is not null && MatchesAnyInChain(root.InnerException, predicate);
    }
}
