using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Application;

/// <summary>
/// アプリケーション層のサービス登録拡張メソッドを提供するクラス
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// アプリケーション層の各サービスおよびEventBusをDIコンテナに登録します
    /// </summary>
    /// <param name="services">登録対象のサービスコレクション</param>
    /// <returns>サービス登録後のサービスコレクション</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // イベントバス（全体共有）
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // 各アプリケーションサービス
        services.AddSingleton<AudioApplicationService>();
        services.AddSingleton<LibraryApplicationService>();
        services.AddSingleton<PlaylistApplicationService>();
        services.AddSingleton<EqualizerApplicationService>();
        services.AddSingleton<DataTransferApplicationService>();
        services.AddSingleton<SettingsApplicationService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsApplicationService>());

        return services;
    }
}
