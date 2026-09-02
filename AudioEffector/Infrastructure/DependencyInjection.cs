using AudioEffector.Application.Common;
using AudioEffector.Domain.Repositories;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.DataTransfer;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Infrastructure.Logging;
using AudioEffector.Infrastructure.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace AudioEffector.Infrastructure;

/// <summary>
/// インフラストラクチャ層のサービス登録拡張メソッドを提供するクラス
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// インフラストラクチャ層の各サービスおよびリポジトリをDIコンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // 音声再生エンジン（リソース管理・排他制御のためSingleton）
        services.AddSingleton<IAudioEngine, NAudioPlaybackEngine>();

        // メタデータ抽出および画像ローダー
        services.AddSingleton<TagLibMetadataExtractor>();
        services.AddSingleton<AlbumArtLoader>();

        // リポジトリ（ファイル競合防止・メモリキャッシュ保持のためSingleton）
        services.AddSingleton<ITrackRepository, JsonTrackRepository>();
        services.AddSingleton<IPlaylistRepository, JsonPlaylistRepository>();
        services.AddSingleton<IFavoriteRepository, JsonFavoriteRepository>();
        services.AddSingleton<ISettingsRepository, JsonSettingsRepository>();

        // データ転送アダプター
        services.AddSingleton<IDataTransferRepository, MtpDataTransferAdapter>();

        // ロギングサービス
        services.AddSingleton<NLogService>();

        return services;
    }
}
