using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Logging;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// アプリケーション設定の読み込み・保存を行うサービスインターフェース
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// アプリケーション設定を同期的に読み込みます
    /// </summary>
    /// <returns>読み込んだ設定情報</returns>
    [LogDescription("アプリケーション設定を読み込みます")]
    AppSettings LoadSettings();

    /// <summary>
    /// アプリケーション設定を同期的に保存します
    /// </summary>
    /// <param name="settings">保存する設定情報</param>
    [LogDescription("アプリケーション設定を保存します")]
    void SaveSettings(AppSettings settings);

    /// <summary>
    /// アプリケーション設定を非同期で読み込みます
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>読み込んだ設定情報を含むタスク</returns>
    [LogDescription("アプリケーション設定を非同期で読み込みます")]
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// アプリケーション設定を非同期で保存します
    /// </summary>
    /// <param name="settings">保存する設定情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>保存処理を表す非同期タスク</returns>
    [LogDescription("アプリケーション設定を非同期で保存します")]
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
