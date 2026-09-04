namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// デバイスの種類（ファイルシステム または MTP）
/// </summary>
public enum DeviceType
{
    /// <summary>
    /// USBマスストレージなどのファイルシステムデバイス
    /// </summary>
    FileSystem,

    /// <summary>
    /// ポータブルデバイスなどのMTPデバイス
    /// </summary>
    MTP
}
