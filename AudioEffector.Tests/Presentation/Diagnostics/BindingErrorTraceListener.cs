using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AudioEffector.Tests.Presentation.Diagnostics;

/// <summary>
/// WPFのデータバインディングエラートレース（PresentationTraceSources.DataBindingSource）を監視し、
/// テスト実行中に発生したバインドエラー・警告をインターセプトして収集するテスト用リスナークラス。
/// </summary>
public sealed class BindingErrorTraceListener : TraceListener, IDisposable
{
    private readonly SourceLevels _originalLevel;
    private readonly List<string> _errors = new();
    private readonly List<string> _allLogs = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// 収集されたバインディングエラーメッセージの一覧を取得します
    /// </summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_lock)
            {
                return _errors.ToArray();
            }
        }
    }

    /// <summary>
    /// 1件以上のバインディングエラーまたは警告が検出されたかどうかを取得します
    /// </summary>
    public bool HasErrors
    {
        get
        {
            lock (_lock)
            {
                return _errors.Count > 0;
            }
        }
    }

    /// <summary>
    /// 検出されたバインディングエラーの件数を取得します
    /// </summary>
    public int ErrorCount
    {
        get
        {
            lock (_lock)
            {
                return _errors.Count;
            }
        }
    }

    /// <summary>
    /// キャプチャされたすべてのトレースメッセージを取得します
    /// </summary>
    public IReadOnlyList<string> AllLogs
    {
        get
        {
            lock (_lock)
            {
                return _allLogs.ToArray();
            }
        }
    }

    /// <summary>
    /// リスナーを初期化し、WPFのDataBindingSourceトレースソースへの登録を開始します
    /// </summary>
    public BindingErrorTraceListener()
    {
        PresentationTraceSources.Refresh();
        _originalLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        Trace.Listeners.Add(this);
    }

    /// <inheritdoc />
    public override void Write(string? message)
    {
        RecordRaw(message);
    }

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        RecordRaw(message);
    }

    private void RecordRaw(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock)
        {
            _allLogs.Add(message.Trim());
        }

        AddIfBindingError(message);
    }

    private void AddIfBindingError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // WPFのバインディングエラーメッセージパターンを判定
        if (message.Contains("Error:") ||
            message.Contains("Warning:") ||
            message.Contains("System.Windows.Data Error") ||
            message.Contains("BindingExpression path error") ||
            message.Contains("Cannot find governing FrameworkElement") ||
            message.Contains("Cannot find source for binding") ||
            message.Contains("not found") ||
            message.Contains("Cannot find"))
        {
            AddMessage(message);
        }
    }

    private void AddMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var trimmed = message.Trim();
        lock (_lock)
        {
            if (!_errors.Contains(trimmed))
            {
                _errors.Add(trimmed);
            }
        }
    }

    /// <summary>
    /// リスナーを登録解除し、リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                PresentationTraceSources.DataBindingSource.Flush();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
                Trace.Listeners.Remove(this);
                PresentationTraceSources.DataBindingSource.Switch.Level = _originalLevel;
                PresentationTraceSources.Refresh();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
