using AudioEffector.Application.Common;
using AudioEffector.Presentation.Services;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AudioEffector.Presentation;

/// <summary>
/// プレゼンテーション層のサービスおよびViewModel登録拡張メソッドを提供するクラス
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// プレゼンテーション層の各ViewModelおよびViewをDIコンテナに登録します
    /// </summary>
    /// <param name="services">登録対象のサービスコレクション</param>
    /// <returns>サービス登録後のサービスコレクション</returns>
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        // UI スレッドディスパッチャー（Infrastructure層のヘッドレス既定実装をWPF実装で上書き）
        services.Replace(ServiceDescriptor.Singleton<IUiDispatcher, WpfUiDispatcher>());

        // 状態保持および画面間共有のため各ViewModelはSingleton登録
        services.AddSingleton<PlayerControlViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<AlbumDetailViewModel>();
        services.AddSingleton<AllSongsViewModel>();
        services.AddSingleton<ArtistsViewModel>();
        services.AddSingleton<RecentViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<EqualizerViewModel>();
        services.AddSingleton<DeviceSyncViewModel>();
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<LyricsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // 全体ViewModel
        services.AddSingleton<MainViewModel>();

        // メインウィンドウ
        services.AddSingleton<MainWindow>();

        return services;
    }
}
