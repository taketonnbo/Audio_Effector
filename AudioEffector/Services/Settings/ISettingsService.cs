namespace AudioEffector.Services
{
    public interface ISettingsService
    {
        [LogDescription("アプリケーション設定を読み込みます")]
        AppSettings LoadSettings();

        [LogDescription("アプリケーション設定を保存します")]
        void SaveSettings(AppSettings settings);
    }
}
