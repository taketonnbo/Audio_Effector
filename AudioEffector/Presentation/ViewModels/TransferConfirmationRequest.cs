using System;
using System.Collections.Generic;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 機器転送開始前の確認・警告ダイアログへ渡す事前チェック結果を表します
/// </summary>
public sealed class TransferConfirmationRequest
{
    /// <summary>
    /// 転送対象として選択されている未転送トラック総数を取得します
    /// </summary>
    public int TotalTrackCount { get; }

    /// <summary>
    /// 転送対象トラックの合計必要サイズ（バイト）を取得します
    /// </summary>
    public long RequiredBytes { get; }

    /// <summary>
    /// 接続機器の空き容量情報が取得できているかどうかを取得します
    /// </summary>
    public bool HasStorageInfo { get; }

    /// <summary>
    /// 接続機器の空き容量（バイト）を取得します
    /// </summary>
    public long FreeSpaceBytes { get; }

    /// <summary>
    /// 接続機器の空き容量が不足しているかどうかを取得します
    /// </summary>
    public bool IsInsufficientSpace { get; }

    /// <summary>
    /// 不足している容量（バイト、容量十分な場合は 0）を取得します
    /// </summary>
    public long ShortageBytes { get; }

    /// <summary>
    /// MP4 から M4A への変換対象曲数を取得します
    /// </summary>
    public int Mp4ToM4aCount { get; }

    /// <summary>
    /// FLAC から MP3 への変換対象曲数を取得します
    /// </summary>
    public int FlacToMp3Count { get; }

    /// <summary>
    /// 形式変換を行わずそのままコピーする曲数を取得します
    /// </summary>
    public int CopyOnlyCount { get; }

    /// <summary>
    /// 形式変換対象が 1 曲以上含まれるかどうかを取得します
    /// </summary>
    public bool HasTranscodeTargets => Mp4ToM4aCount > 0 || FlacToMp3Count > 0;

    /// <summary>
    /// 転送開始前に確認・警告ダイアログの表示が必要かどうかを取得します
    /// </summary>
    public bool NeedsConfirmation => IsInsufficientSpace || HasTranscodeTargets;

    /// <summary>
    /// 容量不足かつ形式変換対象が含まれる場合に概算サイズ注記を表示するかどうかを取得します
    /// </summary>
    public bool ShowEstimatedSizeNote => IsInsufficientSpace && HasTranscodeTargets;

    /// <summary>
    /// ダイアログのウィンドウタイトルを取得します
    /// </summary>
    public string DialogTitle => (IsInsufficientSpace, HasTranscodeTargets) switch
    {
        (true, true) => "転送前の確認（容量不足・形式変換）",
        (true, false) => "空き容量不足の警告",
        (false, true) => "形式変換を伴う転送の確認",
        _ => "転送前の確認"
    };

    /// <summary>
    /// ダイアログの見出しテキストを取得します
    /// </summary>
    public string HeaderText => (IsInsufficientSpace, HasTranscodeTargets) switch
    {
        (true, true) => "⚠ 空き容量不足と形式変換の確認",
        (true, false) => "⚠ 機器の空き容量が不足しています",
        (false, true) => "形式変換を伴う転送を開始しますか？",
        _ => "転送を開始しますか？"
    };

    /// <summary>
    /// 転送開始ボタンの表示ラベルを取得します
    /// </summary>
    public string PrimaryActionText => IsInsufficientSpace ? "そのまま転送を開始" : "転送を開始";

    /// <summary>
    /// 選択曲数の表示文字列を取得します
    /// </summary>
    public string SelectedTrackCountText => $"{TotalTrackCount} 曲";

    /// <summary>
    /// 必要容量のフォーマット済み表示文字列を取得します
    /// </summary>
    public string RequiredSizeText => MainViewModel.DeviceViewModel.FormatByteSize(RequiredBytes);

    /// <summary>
    /// 機器の空き容量のフォーマット済み表示文字列を取得します
    /// </summary>
    public string FreeSpaceText => MainViewModel.DeviceViewModel.FormatByteSize(FreeSpaceBytes);

    /// <summary>
    /// 不足容量のフォーマット済み表示文字列を取得します
    /// </summary>
    public string ShortageSizeText => MainViewModel.DeviceViewModel.FormatByteSize(ShortageBytes);

    /// <summary>
    /// 形式変換の内訳およびコピーのみ曲数の表示行一覧を取得します
    /// </summary>
    public IReadOnlyList<string> ConversionBreakdownLines { get; }

    /// <summary>
    /// <see cref="TransferConfirmationRequest"/> のインスタンスを初期化します
    /// </summary>
    /// <param name="totalTrackCount">転送対象の総曲数</param>
    /// <param name="requiredBytes">転送対象の合計サイズ（バイト）</param>
    /// <param name="hasStorageInfo">接続機器の空き容量情報が取得できているか</param>
    /// <param name="freeSpaceBytes">接続機器の空き容量（バイト）</param>
    /// <param name="mp4ToM4aCount">MP4→M4A 変換対象曲数</param>
    /// <param name="flacToMp3Count">FLAC→MP3 変換対象曲数</param>
    /// <param name="copyOnlyCount">形式変換なしのコピー曲数</param>
    public TransferConfirmationRequest(
        int totalTrackCount,
        long requiredBytes,
        bool hasStorageInfo,
        long freeSpaceBytes,
        int mp4ToM4aCount,
        int flacToMp3Count,
        int copyOnlyCount)
    {
        TotalTrackCount = Math.Max(0, totalTrackCount);
        RequiredBytes = Math.Max(0L, requiredBytes);
        HasStorageInfo = hasStorageInfo;
        FreeSpaceBytes = Math.Max(0L, freeSpaceBytes);
        IsInsufficientSpace = HasStorageInfo && RequiredBytes > FreeSpaceBytes;
        ShortageBytes = IsInsufficientSpace ? RequiredBytes - FreeSpaceBytes : 0L;
        Mp4ToM4aCount = Math.Max(0, mp4ToM4aCount);
        FlacToMp3Count = Math.Max(0, flacToMp3Count);
        CopyOnlyCount = Math.Max(0, copyOnlyCount);

        var lines = new List<string>(3);
        if (FlacToMp3Count > 0)
        {
            lines.Add($"FLAC → MP3: {FlacToMp3Count} 曲");
        }

        if (Mp4ToM4aCount > 0)
        {
            lines.Add($"MP4 → M4A: {Mp4ToM4aCount} 曲");
        }

        if (CopyOnlyCount > 0)
        {
            lines.Add($"コピーのみ（変換なし）: {CopyOnlyCount} 曲");
        }

        ConversionBreakdownLines = lines;
    }
}
