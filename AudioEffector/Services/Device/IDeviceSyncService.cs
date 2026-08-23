using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AudioEffector.Services
{
    public interface IDeviceSyncService
    {
        [LogDescription("リムーバブルドライブの一覧を取得します")]
        List<DriveInfo> GetRemovableDrives();
        [LogDescription("楽曲ファイルをデバイスに転送します")]
        Task TransferFilesAsync(List<string> sourceFilePaths, string destinationFolder, IProgress<double> progress);
    }
}
