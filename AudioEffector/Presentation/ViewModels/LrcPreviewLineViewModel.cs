using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// LRC 自動生成結果のプレビュー 1 行（タイムスタンプ・歌詞本文・個別インライン編集状態）を表す ViewModel です
/// </summary>
public sealed partial class LrcPreviewLineViewModel : ViewModelBase
{
    private readonly Action<LrcPreviewLineViewModel>? _onEdited;

    private TimeSpan _baseTimestamp;
    private TimeSpan _timestamp;
    private string _text;
    private bool _isEditing;
    private string _editingTimestampText = string.Empty;
    private string _editingLyricText = string.Empty;
    private string _editErrorMessage = string.Empty;

    /// <summary>
    /// 0 始まりの行インデックスを取得します
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// オフセット適用後の現在タイムスタンプを取得します
    /// </summary>
    public TimeSpan Timestamp
    {
        get => _timestamp;
        private set
        {
            if (SetProperty(ref _timestamp, value))
            {
                OnPropertyChanged(nameof(TimestampDisplay));
            }
        }
    }

    /// <summary>
    /// 表示用の等幅タイムスタンプ文字列（<c>[mm:ss.xx]</c>）を取得します
    /// </summary>
    public string TimestampDisplay => FormatTimestampBracketed(_timestamp);

    /// <summary>
    /// 歌詞本文を取得します
    /// </summary>
    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    /// <summary>
    /// この行が現在インライン編集モード中かどうかを取得します
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    /// <summary>
    /// インライン編集中のタイムスタンプ入力文字列（<c>mm:ss.xx</c>）を取得または設定します
    /// </summary>
    public string EditingTimestampText
    {
        get => _editingTimestampText;
        set
        {
            if (SetProperty(ref _editingTimestampText, value ?? string.Empty))
            {
                ClearEditError();
            }
        }
    }

    /// <summary>
    /// インライン編集中の歌詞本文入力文字列を取得または設定します
    /// </summary>
    public string EditingLyricText
    {
        get => _editingLyricText;
        set
        {
            if (SetProperty(ref _editingLyricText, value ?? string.Empty))
            {
                ClearEditError();
            }
        }
    }

    /// <summary>
    /// 編集入力のバリデーションエラーメッセージを取得します
    /// </summary>
    public string EditErrorMessage
    {
        get => _editErrorMessage;
        private set
        {
            if (SetProperty(ref _editErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasEditError));
            }
        }
    }

    /// <summary>
    /// 編集エラーが存在するかどうかを取得します
    /// </summary>
    public bool HasEditError => !string.IsNullOrEmpty(_editErrorMessage);

    /// <summary>
    /// 個別行インライン編集モードを開始するコマンドを取得します
    /// </summary>
    public ICommand BeginEditCommand { get; }

    /// <summary>
    /// 個別行インライン編集を検証・確定するコマンドを取得します
    /// </summary>
    public ICommand CommitEditCommand { get; }

    /// <summary>
    /// 個別行インライン編集を破棄して通常表示へ戻すコマンドを取得します
    /// </summary>
    public ICommand CancelEditCommand { get; }

    /// <summary>
    /// <see cref="LrcPreviewLineViewModel"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="index">0 始まりの行インデックス</param>
    /// <param name="timestamp">初期タイムスタンプ</param>
    /// <param name="text">初期歌詞本文</param>
    /// <param name="onEdited">編集確定時に親 ViewModel へ通知するコールバック（任意）</param>
    public LrcPreviewLineViewModel(
        int index,
        TimeSpan timestamp,
        string text,
        Action<LrcPreviewLineViewModel>? onEdited = null)
    {
        Index = index;
        _baseTimestamp = timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
        _timestamp = _baseTimestamp;
        _text = text ?? string.Empty;
        _onEdited = onEdited;

        BeginEditCommand = new RelayCommand(_ => BeginEdit());
        CommitEditCommand = new RelayCommand(_ => CommitEdit());
        CancelEditCommand = new RelayCommand(_ => CancelEdit());
    }

    /// <summary>
    /// 全体オフセット秒数を基準タイムスタンプへ適用します（0 秒未満は 00:00.00 にクランプ）
    /// </summary>
    /// <param name="totalOffsetSeconds">累計オフセット秒数</param>
    public void ApplyGlobalOffset(double totalOffsetSeconds)
    {
        TimeSpan shifted = _baseTimestamp + TimeSpan.FromSeconds(totalOffsetSeconds);
        Timestamp = shifted < TimeSpan.Zero ? TimeSpan.Zero : RoundToCentiseconds(shifted);
    }

    /// <summary>
    /// インライン編集モードを開始します
    /// </summary>
    public void BeginEdit()
    {
        EditingTimestampText = FormatTimestampRaw(_timestamp);
        EditingLyricText = _text;
        ClearEditError();
        IsEditing = true;
    }

    /// <summary>
    /// 入力中のタイムスタンプ書式（<c>mm:ss.xx</c> / 00:00.00 以上）を検証し、妥当な場合に変更を確定します
    /// </summary>
    /// <returns>検証に合格し確定された場合は <see langword="true"/></returns>
    public bool CommitEdit()
    {
        if (!TryParseTimestamp(EditingTimestampText, out TimeSpan parsed))
        {
            EditErrorMessage = "時刻は mm:ss.xx 形式（00:00.00 以上）で入力してください。";
            return false;
        }

        string trimmedText = (EditingLyricText ?? string.Empty).Trim();
        _baseTimestamp = parsed;
        Timestamp = parsed;
        Text = trimmedText;
        ClearEditError();
        IsEditing = false;

        _onEdited?.Invoke(this);
        return true;
    }

    /// <summary>
    /// 編集内容を破棄して通常表示へ戻します
    /// </summary>
    public void CancelEdit()
    {
        ClearEditError();
        IsEditing = false;
    }

    /// <summary>
    /// 現在のタイムスタンプと本文からドメインの <see cref="LyricLine"/> を構築します
    /// </summary>
    /// <param name="lineIndex">出力行インデックス</param>
    /// <returns><see cref="LyricLine"/></returns>
    public LyricLine ToLyricLine(int lineIndex)
    {
        return new LyricLine(lineIndex, _timestamp, _text);
    }

    /// <summary>
    /// ユーザー入力のタイムスタンプ文字列（<c>mm:ss.xx</c>, <c>mm:ss.xxx</c>, <c>mm:ss</c>, 角括弧付きも許容）を解析します
    /// </summary>
    /// <param name="input">解析対象の文字列</param>
    /// <param name="result">解析結果の <see cref="TimeSpan"/></param>
    /// <returns>有効な形式の場合は <see langword="true"/></returns>
    public static bool TryParseTimestamp(string? input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string trimmed = input.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        Match match = TimestampInputRegex().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["min"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || minutes < 0
            || minutes > 9999)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["sec"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || seconds < 0
            || seconds >= 60)
        {
            return false;
        }

        int milliseconds = 0;
        Group fracGroup = match.Groups["frac"];
        if (fracGroup.Success && fracGroup.Length > 0)
        {
            if (!int.TryParse(fracGroup.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int fracValue))
            {
                return false;
            }

            milliseconds = fracGroup.Length switch
            {
                1 => fracValue * 100,
                2 => fracValue * 10,
                3 => fracValue,
                _ => 0,
            };
        }

        result = TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds)
            + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private void ClearEditError()
    {
        if (!string.IsNullOrEmpty(_editErrorMessage))
        {
            EditErrorMessage = string.Empty;
        }
    }

    private static string FormatTimestampBracketed(TimeSpan value)
    {
        return $"[{FormatTimestampRaw(value)}]";
    }

    private static string FormatTimestampRaw(TimeSpan value)
    {
        TimeSpan clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        long totalCentiseconds = (long)Math.Round(clamped.TotalMilliseconds / 10.0, MidpointRounding.AwayFromZero);
        long totalSeconds = totalCentiseconds / 100;
        long minutes = totalSeconds / 60;
        int seconds = (int)(totalSeconds % 60);
        int centiseconds = (int)(totalCentiseconds % 100);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}.{centiseconds:00}");
    }

    private static TimeSpan RoundToCentiseconds(TimeSpan value)
    {
        long cs = (long)Math.Round(value.TotalMilliseconds / 10.0, MidpointRounding.AwayFromZero);
        return TimeSpan.FromMilliseconds(cs * 10L);
    }

    [GeneratedRegex(@"^(?<min>\d{1,4}):(?<sec>\d{2})(?:\.(?<frac>\d{1,3}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampInputRegex();
}
