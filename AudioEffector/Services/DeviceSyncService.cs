using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AudioEffector.Services
{
    public class DeviceSyncService
    {
        public List<DriveInfo> GetRemovableDrives()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                .ToList();
        }

        public async Task TransferFilesAsync(List<string> sourceFilePaths, string destinationFolder, IProgress<double> progress)
        {
            await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(destinationFolder) || !Directory.Exists(destinationFolder))
                    throw new DirectoryNotFoundException("Destination folder not found.");

                int totalFiles = sourceFilePaths.Count;
                int processedFiles = 0;

                foreach (var sourcePath in sourceFilePaths)
                {
                    if (!File.Exists(sourcePath)) continue;

                    try
                    {
                        // Structure: Destination/Artist/Album/Track.ext
                        string relativePath = GetRelativePathFromMetadata(sourcePath);
                        string destPath = Path.Combine(destinationFolder, relativePath);

                        string destDir = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        File.Copy(sourcePath, destPath, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to copy {sourcePath}: {ex.Message}");
                    }

                    processedFiles++;
                    progress?.Report((double)processedFiles / totalFiles * 100);
                }
            });
        }

        private string GetRelativePathFromMetadata(string filePath)
        {
            try
            {
                using (var tfile = TagLib.File.Create(filePath))
                {
                    string artist = tfile.Tag.FirstPerformer ?? "Unknown Artist";
                    string album = tfile.Tag.Album ?? "Unknown Album";
                    string title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath);
                    string ext = Path.GetExtension(filePath);

                    // Sanitize filenames
                    artist = SanitizeFileName(artist);
                    album = SanitizeFileName(album);
                    title = SanitizeFileName(title);

                    return Path.Combine(artist, album, title + ext);
                }
            }
            catch
            {
                // Fallback: just put in "Unknown" folder
                return Path.Combine("Unknown", Path.GetFileName(filePath));
            }
        }

        private string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Where(ch => !invalidChars.Contains(ch)).ToArray());
        }
    }
}
