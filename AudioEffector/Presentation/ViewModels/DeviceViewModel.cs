using System.IO;
using MediaDevices;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// 接続されたデバイスを表すViewModel
/// </summary>
public class DeviceViewModel
{
    /// <summary>
    /// デバイス名を取得または設定します
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// デバイス種別を取得または設定します
    /// </summary>
    public DeviceType Type { get; set; }

    /// <summary>
    /// ファイルシステムデバイスの場合のドライブ情報を取得または設定します
    /// </summary>
    public DriveInfo? Drive { get; set; }

    /// <summary>
    /// MTPデバイスの場合のデバイスインスタンスを取得または設定します
    /// </summary>
    public MediaDevice? MtpDevice { get; set; }

    /// <summary>
    /// デバイスのルートパスを取得または設定します
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
