namespace AudioEffector.Domain.ValueObjects;

/// <summary>
/// 音声再生エンジンの状態を表す列挙型
/// </summary>
public enum PlaybackState
{
    /// <summary>
    /// 停止中
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// 再生中
    /// </summary>
    Playing = 1,

    /// <summary>
    /// 一時停止中
    /// </summary>
    Paused = 2
}
