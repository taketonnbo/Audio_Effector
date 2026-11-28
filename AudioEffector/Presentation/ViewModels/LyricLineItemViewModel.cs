using System;
using System.Globalization;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 歌詞リストの 1 行分の表示状態・タイムスタンプおよびアクティブ強調状態を保持する ViewModel です
/// </summary>
public sealed class LyricLineItemViewModel : ViewModelBase
{
    private bool _isActive;
    private bool _isNearActive;

    /// <summary>
    /// 元となるドメインの歌詞行値オブジェクトを取得します
    /// </summary>
    public LyricLine Line { get; }

    /// <summary>
    /// 歌詞ドキュメント内における 0 始まりの行インデックスを取得します
    /// </summary>
    public int Index => Line.Index;

    /// <summary>
    /// 歌詞行に紐づく元のタイムスタンプを取得します（非同期歌詞の場合は <see langword="null"/>）
    /// </summary>
    public TimeSpan? Timestamp => Line.Timestamp;

    /// <summary>
    /// <c>[offset:]</c> 補正後の有効タイムスタンプを取得します（非同期歌詞の場合は <see langword="null"/>）
    /// </summary>
    public TimeSpan? EffectiveTimestamp { get; }

    /// <summary>
    /// 表示する歌詞本文を取得します（間奏・歌唱休止行の場合は空文字列）
    /// </summary>
    public string Text => Line.Text;

    /// <summary>
    /// テキストを含まない間奏・歌唱休止行かどうかを取得します
    /// </summary>
    public bool IsInstrumental => Line.IsInstrumental;

    /// <summary>
    /// 所属する歌詞ドキュメントがタイムタグによる再生位置同期に対応しているかどうかを取得します
    /// </summary>
    public bool IsSynced { get; }

    /// <summary>
    /// 行クリックによるシーク操作の対象となるかどうかを取得します
    /// </summary>
    public bool IsSeekable => IsSynced && Line.IsSeekable && EffectiveTimestamp.HasValue;

    /// <summary>
    /// ツールチップ表示用の整形済みタイムスタンプ文字列（例: <c>[01:23]</c>）を取得します
    /// </summary>
    public string TimestampDisplay { get; }

    /// <summary>
    /// 現在の再生位置に対応するアクティブ行かどうかを取得または設定します
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    /// <summary>
    /// アクティブ行に近接する前後行（±1〜2 行）かどうかを取得または設定します
    /// </summary>
    public bool IsNearActive
    {
        get => _isNearActive;
        internal set => SetProperty(ref _isNearActive, value);
    }

    /// <summary>
    /// <see cref="LyricLineItemViewModel"/> の新しいインスタンスを初期化します
    /// </summary>
    /// <param name="line">ドメインの歌詞行値オブジェクト</param>
    /// <param name="effectiveTimestamp"><c>[offset:]</c> 補正後の有効タイムスタンプ</param>
    /// <param name="isSynced">同期歌詞ドキュメントに属するかどうか</param>
    public LyricLineItemViewModel(LyricLine line, TimeSpan? effectiveTimestamp, bool isSynced)
    {
        Line = line ?? throw new ArgumentNullException(nameof(line));
        EffectiveTimestamp = effectiveTimestamp;
        IsSynced = isSynced;
        TimestampDisplay = FormatTimestamp(effectiveTimestamp);
    }

    private static string FormatTimestamp(TimeSpan? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return string.Empty;
        }

        TimeSpan value = timestamp.Value < TimeSpan.Zero ? TimeSpan.Zero : timestamp.Value;
        int totalMinutes = (int)value.TotalMinutes;
        return string.Format(CultureInfo.InvariantCulture, "[{0:00}:{1:00}]", totalMinutes, value.Seconds);
    }
}
