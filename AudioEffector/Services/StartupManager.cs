using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace AudioEffector.Services
{
    public static class StartupManager
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AudioEffector";

        /// <summary>
        /// WindowsのOS起動時の自動起動状態を設定します。
        /// </summary>
        /// <param name="enable">有効にする場合は true、無効にする場合は false</param>
        public static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // 起動パスをレジストリに登録
                        key.SetValue(AppName, exePath);
                    }
                }
                else
                {
                    // レジストリから削除（値が存在しなくても例外は発生しない）
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動起動の設定に失敗しました: {ex.Message}");
            }
        }
    }
}
