using System.Security.Cryptography;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class FileChunkService
{
    public static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = System.IO.File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        using var sha = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeChunkChecksum(byte[] data, int length)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data, 0, length);
        return Convert.ToHexString(hash).ToLowerInvariant()[..8];
    }

    public static string GetDefaultSavePath(string fileName)
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TubaTransfer");
        System.IO.Directory.CreateDirectory(dir);

        var fullPath = System.IO.Path.Combine(dir, fileName);
        if (!System.IO.File.Exists(fullPath)) return fullPath;

        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var ext = System.IO.Path.GetExtension(fileName);
        var counter = 1;
        while (System.IO.File.Exists(fullPath))
        {
            fullPath = System.IO.Path.Combine(dir, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        }
        return fullPath;
    }

    public static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{(double)bytes / (1L << 30):F2} GB",
        >= 1L << 20 => $"{(double)bytes / (1L << 20):F1} MB",
        >= 1L << 10 => $"{(double)bytes / (1L << 10):F0} KB",
        _ => $"{bytes} B"
    };

    public static string FormatSpeed(double mbps) => mbps switch
    {
        >= 1000 => $"{mbps / 1000:F2} GB/s",
        >= 1 => $"{mbps:F1} MB/s",
        > 0 => $"{mbps * 1024:F0} KB/s",
        _ => ""
    };

    public static string FormatProgress(double percent) => $"{percent:F1}%";

    public static string FormatRemainingTime(FileTransferTask task)
    {
        if (task.SpeedMbps <= 0 || task.BytesTransferred >= task.FileSize) return "";

        var remainingBytes = task.FileSize - task.BytesTransferred;
        var remainingSeconds = remainingBytes / (task.SpeedMbps * 1024 * 1024 / 8);

        return remainingSeconds switch
        {
            >= 3600 => $"{(int)remainingSeconds / 3600}小时{(int)remainingSeconds % 3600 / 60}分",
            >= 60 => $"{(int)remainingSeconds / 60}分{(int)remainingSeconds % 60}秒",
            _ => $"{(int)remainingSeconds}秒"
        };
    }
}
