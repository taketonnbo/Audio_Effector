using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AudioEffector.Services
{
    public interface IDeviceSyncService
    {
        List<DriveInfo> GetRemovableDrives();
        Task TransferFilesAsync(List<string> sourceFilePaths, string destinationFolder, IProgress<double> progress);
    }
}
