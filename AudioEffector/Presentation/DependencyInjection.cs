using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Presentation;

/// <summary>
/// プレゼンテーション層のサービスおよびViewModel登録拡張メソッドを提供するクラス
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// プレゼンテーション層の各ViewModelおよびViewをDIコンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        // 状態保持および画面間共有のため各ViewModelはSingleton登録
        services.AddSingleton<PlayerControlViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<EqualizerViewModel>();
        services.AddSingleton<DeviceSyncViewModel>();
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // 全体ViewModel
        services.AddSingleton<MainViewModel>();

        // メインウィンドウ
        services.AddSingleton<MainWindow>();

        return services;
    }
}
