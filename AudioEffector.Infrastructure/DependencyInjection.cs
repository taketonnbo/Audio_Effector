using AudioEffector.Application.Common;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.DataTransfer;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Infrastructure.Logging;
using AudioEffector.Infrastructure.LrcGeneration;
using AudioEffector.Infrastructure.Lyrics;
using AudioEffector.Infrastructure.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AudioEffector.Infrastructure;

/// <summary>
/// インフラストラクチャ層のサービス登録拡張メソッドを提供するクラス
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// インフラストラクチャ層の各サービスおよびリポジトリをDIコンテナに登録します
    /// </summary>
    /// <param name="services">登録対象のサービスコレクション</param>
    /// <param name="dataDirectory">永続化JSONファイルを格納するデータディレクトリ（未指定時は既定の %APPDATA%\AudioEffector）</param>
    /// <returns>サービス登録後のサービスコレクション</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string? dataDirectory = null)
    {
        // データディレクトリ解決およびプロセス間ファイルロック基盤
        services.AddSingleton<IDataDirectoryProvider>(new DataDirectoryProvider(dataDirectory));
        services.TryAddSingleton<IJsonFileLockHelper>(JsonFileLockHelper.Default);

        // 音声再生エンジン（リソース管理・排他制御のためSingleton）
        services.TryAddSingleton<IUiDispatcher, ImmediateUiDispatcher>();
        services.AddSingleton<IAudioEngine, NAudioPlaybackEngine>();
        services.AddSingleton<AudioEffector.Application.ApplicationServices.IAudioService, AudioService>();

        // メタデータ抽出および画像ローダー
        services.AddSingleton<TagLibMetadataExtractor>();
        services.AddSingleton<IMetadataExtractor>(sp => sp.GetRequiredService<TagLibMetadataExtractor>());
        services.AddSingleton<AlbumArtLoader>();

        // リポジトリ（ファイル競合防止・メモリキャッシュ保持のためSingleton。データ系はIDataDirectoryProviderで動的パス解決）
        services.AddSingleton<ITrackRepository>(sp => new JsonTrackRepository(
            sp.GetRequiredService<IDataDirectoryProvider>(),
            sp.GetRequiredService<IJsonFileLockHelper>()));
        services.AddSingleton<IPlaylistRepository>(sp => new JsonPlaylistRepository(
            sp.GetRequiredService<IDataDirectoryProvider>(),
            sp.GetRequiredService<IJsonFileLockHelper>()));
        services.AddSingleton<IFavoriteRepository>(sp => new JsonFavoriteRepository(
            sp.GetRequiredService<IDataDirectoryProvider>(),
            sp.GetRequiredService<IJsonFileLockHelper>()));
        // settings.json は次回起動時の設定読み込み元として固定ディレクトリへ維持する
        services.AddSingleton<ISettingsRepository>(sp => new JsonSettingsRepository(
            sp.GetRequiredService<IDataDirectoryProvider>().GetFilePath("settings.json"),
            sp.GetRequiredService<IJsonFileLockHelper>()));
        services.AddSingleton<JsonListeningStatsRepository>(sp => new JsonListeningStatsRepository(
            sp.GetRequiredService<IDataDirectoryProvider>(),
            sp.GetRequiredService<IJsonFileLockHelper>()));
        services.AddSingleton<IListeningStatsRepository>(sp => sp.GetRequiredService<JsonListeningStatsRepository>());
        services.AddSingleton<IRecentTrackRepository>(sp => sp.GetRequiredService<JsonListeningStatsRepository>());

        // データ転送アダプターおよび音声変換サービス
        services.AddSingleton<IDataTransferRepository, MtpDataTransferAdapter>();
        services.AddSingleton<IAudioTranscodeService, MediaFoundationAudioTranscodeService>();

        // 歌詞解析およびローカル歌詞プロバイダー
        services.AddSingleton<ILrcParser, LrcParser>();
        services.AddSingleton<ILyricsProvider, LocalLyricsProvider>();

        // LRC 自動生成（ローカルモデル管理・文字コード判定/変換・アライメントエンジン）
        services.AddSingleton<ITextEncodingService, TextEncodingService>();
        services.AddSingleton<ILocalSpeechModelManager, WhisperModelManager>();
        services.AddSingleton<IAudioAlignmentEngine, WhisperLocalAlignmentEngine>();

        // ロギングサービスおよび動的ログ構成マネージャ
        services.AddSingleton<NLogService>();
        services.AddSingleton<ILogConfigurationManager, LogConfigurationManager>();

        return services;
    }
}
