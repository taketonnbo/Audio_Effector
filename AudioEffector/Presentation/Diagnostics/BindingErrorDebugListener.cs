using System;
using System.Diagnostics;
using System.IO;

namespace AudioEffector.Presentation.Diagnostics;

/// <summary>
/// デバッグ実行時にWPFのバインディングエラーを検知し、
/// Visual Studio出力ウィンドウおよび一時ログファイル（binding_errors.log）へ自動記録するリスナー。
/// </summary>
public sealed class BindingErrorDebugListener : TraceListener
{
    private readonly string _logFilePath;
    private readonly SourceLevels _originalLevel;
    private readonly object _fileLock = new();
    private bool _disposed;

    /// <summary>
    /// 出力先ログファイルの絶対パスを取得します
    /// </summary>
    public string LogFilePath => _logFilePath;

    /// <summary>
    /// 指定されたファイル名でリスナーを初期化します
    /// </summary>
    /// <param name="logFileName">出力ログファイル名</param>
    public BindingErrorDebugListener(string logFileName = "binding_errors.log")
    {
        _logFilePath = Path.IsPathRooted(logFileName)
            ? logFileName
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFileName);

        PresentationTraceSources.Refresh();
        _originalLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(this);

        // 起動時の初期ログ記録
        LogToFile($"=== DataBinding Error Trace Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    /// <summary>
    /// バインドエラーリスナーを登録して監視を開始します
    /// </summary>
    /// <param name="logFileName">ログファイル名</param>
    /// <returns>登録されたリスナーインスタンス</returns>
    public static BindingErrorDebugListener StartListening(string logFileName = "binding_errors.log")
    {
        return new BindingErrorDebugListener(logFileName);
    }

    /// <inheritdoc />
    public override void Write(string? message)
    {
        ProcessMessage(message);
    }

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        ProcessMessage(message);
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (eventType is TraceEventType.Error or TraceEventType.Warning or TraceEventType.Critical)
        {
            ProcessMessage($"[{eventType}] {message}");
        }
    }

    /// <inheritdoc />
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        if (eventType is TraceEventType.Error or TraceEventType.Warning or TraceEventType.Critical)
        {
            var msg = (format != null && args != null && args.Length > 0)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)
                : format;
            ProcessMessage($"[{eventType}] {msg}");
        }
    }

    /// <inheritdoc />
    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
    {
        if (eventType is TraceEventType.Error or TraceEventType.Warning or TraceEventType.Critical && data != null)
        {
            ProcessMessage($"[{eventType}] {data}");
        }
    }

    /// <inheritdoc />
    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, params object?[]? data)
    {
        if (eventType is TraceEventType.Error or TraceEventType.Warning or TraceEventType.Critical && data != null && data.Length > 0)
        {
            var combined = string.Join(" ", Array.FindAll(data, d => d != null));
            ProcessMessage($"[{eventType}] {combined}");
        }
    }

    private void ProcessMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var text = message.Trim();
        Debug.WriteLine($"[BindingError] {text}");
        LogToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}");
    }

    private void LogToFile(string line)
    {
        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // ロギング処理自体での例外はアプリケーションの動作に影響を与えないよう抑止
        }
    }

    /// <summary>
    /// リソースを解放しリスナーの登録を解除します
    /// </summary>
    /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                PresentationTraceSources.DataBindingSource.Flush();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
                PresentationTraceSources.DataBindingSource.Switch.Level = _originalLevel;
                PresentationTraceSources.Refresh();
                LogToFile($"=== DataBinding Error Trace Stopped: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
