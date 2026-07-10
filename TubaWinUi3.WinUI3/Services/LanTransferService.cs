using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class LanTransferService
{
    public const int DefaultPort = 19877;
    private const int BufferSize = 65536;
    private const int LanChunkSize = 65536;

    private static TcpListener? _listener;
    private static CancellationTokenSource? _listenerCts;
    private static Task? _listenerTask;
    private static readonly Dictionary<string, FileTransferTask> _activeReceives = [];

    public static event Func<FileTransferTask, Task>? FileOfferReceived;
    public static event Action<FileTransferTask>? TransferProgressChanged;
    public static event Action<FileTransferTask>? TransferCompleted;
    public static event Action<FileTransferTask>? TransferFailed;

    public static bool IsListening => _listenerCts is not null && !_listenerCts.IsCancellationRequested;

    public static void StartListening()
    {
        if (IsListening) return;

        _listenerCts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, DefaultPort);
        _listener.Start();
        _listenerTask = AcceptLoopAsync(_listenerCts.Token);
    }

    public static void StopListening()
    {
        _listenerCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _listenerCts?.Dispose();
        _listenerCts = null;

        try { _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _listenerTask = null;
    }

    private static async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync().WaitAsync(ct);
                _ = HandleIncomingConnectionAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private static async Task HandleIncomingConnectionAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };

            var headerJson = await reader.ReadLineAsync(ct);
            if (headerJson is null) return;

            var header = LanTransferHeader.Deserialize(headerJson);
            if (header is null) return;

            var task = new FileTransferTask
            {
                FileId = header.FileId,
                FileName = header.FileName,
                FileSize = header.FileSize,
                ChunkSize = header.ChunkSize,
                TotalChunks = header.TotalChunks,
                Sha256 = header.Sha256,
                FromDeviceId = header.FromDeviceId,
                FromDeviceName = header.FromDeviceName,
                Direction = TransferDirection.Receiving,
                Status = TransferStatus.Pending,
                ConnectionType = ConnectionType.Lan,
                StartTime = DateTime.Now
            };

            _activeReceives[task.FileId] = task;

            if (FileOfferReceived is not null)
            {
                await FileOfferReceived(task);
            }

            var saveDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "TubaTransfer");
            System.IO.Directory.CreateDirectory(saveDir);
            task.SavePath = System.IO.Path.Combine(saveDir, task.FileName);

            await writer.WriteLineAsync("ACCEPT");

            task.Status = TransferStatus.Transferring;

            await ReceiveFileDataAsync(stream, task, ct);

            client.Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LAN receive error: {ex.Message}");
        }
    }

    private static async Task ReceiveFileDataAsync(NetworkStream stream, FileTransferTask task, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(task.SavePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true);
            var buffer = new byte[BufferSize];
            var totalRead = 0L;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lastReport = 0L;
            var lastReportTime = TimeSpan.Zero;

            while (totalRead < task.FileSize)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = task.FileSize - totalRead;
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (read == 0) break;

                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                var now = sw.Elapsed;
                if (now - lastReportTime > TimeSpan.FromMilliseconds(200))
                {
                    var chunkBytes = totalRead - lastReport;
                    var chunkTime = now - lastReportTime;
                    task.SpeedMbps = chunkBytes / chunkTime.TotalSeconds / (1024 * 1024) * 8;
                    task.BytesTransferred = totalRead;
                    task.CompletedChunks = (int)(totalRead / task.ChunkSize);
                    lastReport = totalRead;
                    lastReportTime = now;
                    TransferProgressChanged?.Invoke(task);
                }
            }

            sw.Stop();
            task.BytesTransferred = totalRead;
            task.CompletedChunks = task.TotalChunks;
            task.SpeedMbps = 0;
            task.Status = TransferStatus.Completed;
            task.EndTime = DateTime.Now;
            TransferCompleted?.Invoke(task);
        }
        catch (OperationCanceledException)
        {
            task.Status = TransferStatus.Cancelled;
            TransferFailed?.Invoke(task);
            throw;
        }
        catch (Exception ex)
        {
            task.Status = TransferStatus.Failed;
            task.ErrorMessage = ex.Message;
            TransferFailed?.Invoke(task);
        }
    }

    public static async Task SendFileAsync(string filePath, string targetIp, int targetPort, FileTransferTask task, CancellationToken ct)
    {
        try
        {
            task.Status = TransferStatus.Connecting;
            task.Direction = TransferDirection.Sending;
            task.ConnectionType = ConnectionType.Lan;
            task.FilePath = filePath;
            task.FileName = System.IO.Path.GetFileName(filePath);

            var fileInfo = new System.IO.FileInfo(filePath);
            task.FileSize = fileInfo.Length;
            task.ChunkSize = LanChunkSize;
            task.TotalChunks = (int)Math.Ceiling((double)fileInfo.Length / LanChunkSize);

            using var client = new TcpClient();
            await client.ConnectAsync(targetIp, targetPort, ct);

            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, leaveOpen: true);

            var sha256 = ComputeFileSha256(filePath);
            task.Sha256 = sha256;

            var header = new LanTransferHeader
            {
                FileId = task.FileId,
                FileName = task.FileName,
                FileSize = task.FileSize,
                ChunkSize = task.ChunkSize,
                TotalChunks = task.TotalChunks,
                Sha256 = sha256,
                FromDeviceId = FileTransferOrchestrator.DeviceId,
                FromDeviceName = Environment.MachineName
            };

            await writer.WriteLineAsync(header.Serialize());

            var response = await reader.ReadLineAsync(ct);
            if (response != "ACCEPT")
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = "接收方拒绝";
                TransferFailed?.Invoke(task);
                return;
            }

            task.Status = TransferStatus.Transferring;
            task.StartTime = DateTime.Now;

            await SendFileDataAsync(stream, filePath, task, ct);

            client.Close();
        }
        catch (OperationCanceledException)
        {
            task.Status = TransferStatus.Cancelled;
            TransferFailed?.Invoke(task);
        }
        catch (Exception ex)
        {
            task.Status = TransferStatus.Failed;
            task.ErrorMessage = ex.Message;
            TransferFailed?.Invoke(task);
        }
    }

    private static async Task SendFileDataAsync(NetworkStream stream, string filePath, FileTransferTask task, CancellationToken ct)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
        var buffer = new byte[BufferSize];
        var totalSent = 0L;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = 0L;
        var lastReportTime = TimeSpan.Zero;

        while (totalSent < task.FileSize)
        {
            ct.ThrowIfCancellationRequested();
            var read = await fs.ReadAsync(buffer, ct);
            if (read == 0) break;

            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalSent += read;

            var now = sw.Elapsed;
            if (now - lastReportTime > TimeSpan.FromMilliseconds(200))
            {
                var chunkBytes = totalSent - lastReport;
                var chunkTime = now - lastReportTime;
                task.SpeedMbps = chunkBytes / chunkTime.TotalSeconds / (1024 * 1024) * 8;
                task.BytesTransferred = totalSent;
                task.CompletedChunks = (int)(totalSent / task.ChunkSize);
                lastReport = totalSent;
                lastReportTime = now;
                TransferProgressChanged?.Invoke(task);
            }
        }

        sw.Stop();
        task.BytesTransferred = totalSent;
        task.CompletedChunks = task.TotalChunks;
        task.SpeedMbps = 0;
        task.Status = TransferStatus.Completed;
        task.EndTime = DateTime.Now;
        TransferCompleted?.Invoke(task);
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = System.IO.File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
