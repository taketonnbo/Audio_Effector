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
    [LogDescription("アプリケーション設定を読み込みます")]
    AppSettings LoadSettings();

    [LogDescription("アプリケーション設定を保存します")]
    void SaveSettings(AppSettings settings);

    [LogDescription("アプリケーション設定を非同期で読み込みます")]
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    [LogDescription("アプリケーション設定を非同期で保存します")]
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
