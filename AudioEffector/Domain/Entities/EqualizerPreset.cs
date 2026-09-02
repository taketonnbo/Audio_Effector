using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Domain.Entities;

/// <summary>
/// イコライザーのプリセット（各周波数バンドのゲイン設定集約）を表すドメインエンティティ
/// </summary>
public class EqualizerPreset : IEquatable<EqualizerPreset>
{
    /// <summary>
    /// 10バンドEQの標準中心周波数（Hz）
    /// </summary>
    public static readonly float[] STANDARD_10_BAND_FREQUENCIES = [31.25f, 62.5f, 125.0f, 250.0f, 500.0f, 1000.0f, 2000.0f, 4000.0f, 8000.0f, 16000.0f];

    private readonly List<FrequencyBand> _bands;

    /// <summary>
    /// プリセット名
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 周波数バンド設定一覧（読み取り専用）
    /// </summary>
    public IReadOnlyList<FrequencyBand> Bands => _bands.AsReadOnly();

    /// <summary>
    /// ユーザー作成のカスタムプリセットかどうか
    /// </summary>
    public bool IsCustom { get; }

    /// <summary>
    /// バンド数
    /// </summary>
    public int BandCount => _bands.Count;

    /// <summary>
    /// イコライザープリセットを初期化します
    /// </summary>
    /// <param name="name">プリセット名</param>
    /// <param name="bands">周波数バンド設定コレクション</param>
    /// <param name="isCustom">カスタムプリセットフラグ</param>
    public EqualizerPreset(string name, IEnumerable<FrequencyBand> bands, bool isCustom = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("プリセット名を空にすることはできません", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(bands);

        Name = name.Trim();
        _bands = new List<FrequencyBand>(bands);
        IsCustom = isCustom;

        if (_bands.Count == 0)
        {
            throw new ArgumentException("プリセットには少なくとも1つの周波数バンドが必要です", nameof(bands));
        }
    }

    /// <summary>
    /// 指定されたインデックスの周波数バンドのゲインを更新します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス（0〜BandCount-1）</param>
    /// <param name="newGain">新しいゲイン値</param>
    public void UpdateBandGain(int bandIndex, Gain newGain)
    {
        if (bandIndex < 0 || bandIndex >= _bands.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex), $"バンドインデックスは 0 から {_bands.Count - 1} の範囲である必要があります");
        }

        _bands[bandIndex] = _bands[bandIndex].WithGain(newGain);
    }

    /// <summary>
    /// フラット（全バンド0dB）な10バンドEQプリセットを生成します
    /// </summary>
    /// <param name="name">プリセット名（デフォルト: "Flat"）</param>
    /// <returns>生成されたEqualizerPreset</returns>
    public static EqualizerPreset CreateFlat(string name = "Flat")
    {
        var bands = STANDARD_10_BAND_FREQUENCIES.Select(freq => new FrequencyBand(freq, Gain.Zero));
        return new EqualizerPreset(name, bands, isCustom: false);
    }

    /// <summary>
    /// 指定されたゲイン配列から10バンドEQプリセットを生成します
    /// </summary>
    /// <param name="name">プリセット名</param>
    /// <param name="gains">10要素のゲイン配列（dB）</param>
    /// <param name="isCustom">カスタムプリセットフラグ</param>
    /// <returns>生成されたEqualizerPreset</returns>
    public static EqualizerPreset Create10Band(string name, float[] gains, bool isCustom = false)
    {
        ArgumentNullException.ThrowIfNull(gains);
        if (gains.Length != STANDARD_10_BAND_FREQUENCIES.Length)
        {
            throw new ArgumentException($"ゲイン配列の要素数は {STANDARD_10_BAND_FREQUENCIES.Length} 個である必要があります", nameof(gains));
        }

        var bands = STANDARD_10_BAND_FREQUENCIES
            .Select((freq, i) => new FrequencyBand(freq, Gain.FromDecibels(gains[i])))
            .ToList();

        return new EqualizerPreset(name, bands, isCustom);
    }

    /// <summary>
    /// 同一性判定（プリセット名で比較）
    /// </summary>
    /// <param name="other">比較対象のEqualizerPreset</param>
    /// <returns>同一の場合はtrue、それ以外はfalse</returns>
    public bool Equals(EqualizerPreset? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// オブジェクト等価性判定
    /// </summary>
    /// <param name="obj">比較対象オブジェクト</param>
    /// <returns>等価な場合はtrue</returns>
    public override bool Equals(object? obj) => Equals(obj as EqualizerPreset);

    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    /// <summary>
    /// 文字列形式に変換します
    /// </summary>
    /// <returns>プリセット名</returns>
    public override string ToString() => $"{Name} ({BandCount} bands)";
}
