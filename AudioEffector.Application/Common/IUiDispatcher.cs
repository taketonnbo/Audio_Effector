using System;

namespace AudioEffector.Application.Common;

/// <summary>
/// UIスレッドへの処理ディスパッチを抽象化するインターフェース
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// 指定された処理をUIスレッド上で同期的に実行します
    /// </summary>
    /// <param name="action">実行する処理</param>
    void Invoke(Action action);

    /// <summary>
    /// 指定された処理をUIスレッド（またはバックグラウンドスレッド）へ非同期・非ブロッキングで投入します
    /// </summary>
    /// <param name="action">実行する処理</param>
    void BeginInvoke(Action action);
}
