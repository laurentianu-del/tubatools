using System.Text;
using SIPSorcery.Net;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class WebRtcTransferService
{
    private const int DataChannelChunkSize = 16384;
    private const int WindowSize = 8;

    private static readonly Dictionary<string, RTCPeerConnection> _peerConnections = [];
    private static readonly Dictionary<string, RTCDataChannel> _dataChannels = [];
    private static readonly Dictionary<string, FileTransferTask> _pendingSends = [];
    private static readonly Dictionary<string, FileTransferTask> _pendingReceives = [];
    private static readonly Dictionary<string, FileStream> _receiveStreams = [];
    private static readonly Dictionary<string, int> _receiveAckedChunks = [];

    private static readonly RTCConfiguration _rtcConfig = new()
    {
        iceServers = new List<RTCIceServer>
        {
            new() { urls = "stun:stun.cloudflare.com:3478" },
            new() { urls = "stun:stun.l.google.com:19302" },
        }
    };

    public static event Action<string, string>? IceCandidateReady;
    public static event Action<string, string>? SdpOfferReady;
    public static event Action<string, string>? SdpAnswerReady;
    public static event Action<string>? PeerConnected;
    public static event Action<string>? PeerDisconnected;
    public static event Action<FileTransferTask>? TransferProgressChanged;
    public static event Action<FileTransferTask>? TransferCompleted;
    public static event Action<FileTransferTask>? TransferFailed;
    public static event Func<FileTransferTask, Task>? FileOfferReceived;

    public static bool UseTurn { get; set; }

    public static string? TurnUrl { get; set; }

    public static void ConfigureTurn(string url, string? username = null, string? credential = null)
    {
        UseTurn = true;
        TurnUrl = url;

        _rtcConfig.iceServers = new List<RTCIceServer>
        {
            new() { urls = "stun:stun.cloudflare.com:3478" },
            new() { urls = "stun:stun.l.google.com:19302" },
            new()
            {
                urls = url,
                username = username ?? "",
                credential = credential ?? "",
                credentialType = RTCIceCredentialType.password
            }
        };
    }

    public static async Task<string> CreateOfferAsync(string targetDeviceId)
    {
        var pc = CreatePeerConnection(targetDeviceId);
        var dc = await pc.createDataChannel("fileTransfer", new RTCDataChannelInit
        {
            ordered = true,
            maxRetransmits = 10
        });

        SetupDataChannel(dc, targetDeviceId);
        _dataChannels[targetDeviceId] = dc;

        var offer = pc.createOffer();
        await pc.setLocalDescription(offer);

        return offer.sdp;
    }

    public static async Task<string> HandleOfferAsync(string targetDeviceId, string sdpOffer)
    {
        if (_peerConnections.TryGetValue(targetDeviceId, out var existingPc))
        {
            existingPc.close();
            _peerConnections.Remove(targetDeviceId);
        }

        var pc = CreatePeerConnection(targetDeviceId);

        pc.ondatachannel += (dc) =>
        {
            _dataChannels[targetDeviceId] = dc;
            SetupDataChannel(dc, targetDeviceId);
        };

        var offer = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdpOffer
        };

        pc.setRemoteDescription(offer);
        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer);

        return answer.sdp;
    }

    public static void HandleAnswer(string targetDeviceId, string sdpAnswer)
    {
        if (!_peerConnections.TryGetValue(targetDeviceId, out var pc)) return;

        var answer = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = sdpAnswer
        };
        pc.setRemoteDescription(answer);
    }

    public static void AddIceCandidate(string targetDeviceId, string candidate, string? sdpMid, int sdpMlineIndex)
    {
        if (!_peerConnections.TryGetValue(targetDeviceId, out var pc)) return;

        var init = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid
        };
        pc.addIceCandidate(init);
    }

    public static async Task SendFileAsync(string targetDeviceId, string filePath, FileTransferTask task, CancellationToken ct)
    {
        task.Direction = TransferDirection.Sending;
        task.ConnectionType = UseTurn ? ConnectionType.WebRtcTurn : ConnectionType.WebRtcP2p;
        task.FilePath = filePath;
        task.FileName = System.IO.Path.GetFileName(filePath);
        task.ChunkSize = DataChannelChunkSize;

        var fileInfo = new System.IO.FileInfo(filePath);
        task.FileSize = fileInfo.Length;
        task.TotalChunks = (int)Math.Ceiling((double)fileInfo.Length / DataChannelChunkSize);

        _pendingSends[task.FileId] = task;

        if (!_dataChannels.TryGetValue(targetDeviceId, out var dc) || dc.readyState != RTCDataChannelState.open)
        {
            task.Status = TransferStatus.Connecting;
            var offer = await CreateOfferAsync(targetDeviceId);
            SdpOfferReady?.Invoke(targetDeviceId, offer);

            var timeout = TimeSpan.FromSeconds(30);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout && ct.IsCancellationRequested == false)
            {
                if (_dataChannels.TryGetValue(targetDeviceId, out var ch) && ch.readyState == RTCDataChannelState.open)
                    break;
                await Task.Delay(200, ct);
            }

            if (!_dataChannels.TryGetValue(targetDeviceId, out dc) || dc.readyState != RTCDataChannelState.open)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = "WebRTC 连接超时";
                TransferFailed?.Invoke(task);
                return;
            }
        }

        task.Status = TransferStatus.Transferring;
        task.StartTime = DateTime.Now;

        await SendFileOverDataChannelAsync(dc, filePath, task, ct);
    }

    private static async Task SendFileOverDataChannelAsync(RTCDataChannel dc, string filePath, FileTransferTask task, CancellationToken ct)
    {
        try
        {
            var headerJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "file-header",
                fileId = task.FileId,
                fileName = task.FileName,
                fileSize = task.FileSize,
                chunkSize = task.ChunkSize,
                totalChunks = task.TotalChunks,
                sha256 = task.Sha256,
                fromDeviceId = FileTransferOrchestrator.DeviceId,
                fromDeviceName = Environment.MachineName
            });
            dc.send(headerJson);

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, DataChannelChunkSize, true);
            var buffer = new byte[DataChannelChunkSize];
            var totalSent = 0L;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lastReport = 0L;
            var lastReportTime = TimeSpan.Zero;
            var pendingAcks = 0;

            for (int chunkIndex = 0; chunkIndex < task.TotalChunks; chunkIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var read = await fs.ReadAsync(buffer, ct);
                if (read == 0) break;

                var chunkData = read < buffer.Length ? buffer[..read] : buffer;

                var chunkMsg = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "file-chunk",
                    fileId = task.FileId,
                    index = chunkIndex,
                    data = Convert.ToBase64String(chunkData, 0, read)
                });

                while (dc.bufferedAmount > 1024 * 1024)
                {
                    await Task.Delay(10, ct);
                }

                dc.send(chunkMsg);
                totalSent += read;

                var now = sw.Elapsed;
                if (now - lastReportTime > TimeSpan.FromMilliseconds(500))
                {
                    var chunkBytes = totalSent - lastReport;
                    var chunkTime = now - lastReportTime;
                    task.SpeedMbps = chunkBytes / chunkTime.TotalSeconds / (1024 * 1024) * 8;
                    task.BytesTransferred = totalSent;
                    task.CompletedChunks = chunkIndex + 1;
                    lastReport = totalSent;
                    lastReportTime = now;
                    TransferProgressChanged?.Invoke(task);
                }
            }

            var eofMsg = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "file-eof",
                fileId = task.FileId,
                totalChunks = task.TotalChunks
            });
            dc.send(eofMsg);

            sw.Stop();
            task.BytesTransferred = totalSent;
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
        }
        catch (Exception ex)
        {
            task.Status = TransferStatus.Failed;
            task.ErrorMessage = ex.Message;
            TransferFailed?.Invoke(task);
        }
    }

    private static RTCPeerConnection CreatePeerConnection(string targetDeviceId)
    {
        var config = _rtcConfig;
        if (UseTurn && !string.IsNullOrEmpty(TurnUrl))
        {
            config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new() { urls = "stun:stun.cloudflare.com:3478" },
                    new() { urls = "stun:stun.l.google.com:19302" },
                    new()
                    {
                        urls = TurnUrl,
                        credentialType = RTCIceCredentialType.password
                    }
                }
            };
        }

        var pc = new RTCPeerConnection(config);

        pc.onicecandidate += (candidate) =>
        {
            if (candidate != null && !string.IsNullOrEmpty(candidate.candidate))
            {
                IceCandidateReady?.Invoke(targetDeviceId, candidate.candidate);
            }
        };

        pc.onconnectionstatechange += (state) =>
        {
            if (state == RTCPeerConnectionState.connected)
            {
                PeerConnected?.Invoke(targetDeviceId);
            }
            else if (state == RTCPeerConnectionState.disconnected ||
                     state == RTCPeerConnectionState.failed ||
                     state == RTCPeerConnectionState.closed)
            {
                PeerDisconnected?.Invoke(targetDeviceId);
            }
        };

        _peerConnections[targetDeviceId] = pc;
        return pc;
    }

    private static void SetupDataChannel(RTCDataChannel dc, string targetDeviceId)
    {
        dc.onopen += () =>
        {
            PeerConnected?.Invoke(targetDeviceId);
        };

        dc.onclose += () =>
        {
            PeerDisconnected?.Invoke(targetDeviceId);
        };

        dc.onmessage += (dc2, protocol, msg) =>
        {
            var msgStr = System.Text.Encoding.UTF8.GetString(msg);
            HandleDataChannelMessage(dc2, targetDeviceId, msgStr);
        };
    }

    private static void HandleDataChannelMessage(RTCDataChannel dc, string fromDeviceId, string msg)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(msg);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "file-header":
                    HandleFileHeader(fromDeviceId, doc);
                    break;
                case "file-chunk":
                    HandleFileChunk(fromDeviceId, doc);
                    break;
                case "file-eof":
                    HandleFileEof(fromDeviceId, doc);
                    break;
            }
        }
        catch { }
    }

    private static void HandleFileHeader(string fromDeviceId, System.Text.Json.JsonDocument doc)
    {
        var root = doc.RootElement;
        var fileId = root.GetProperty("fileId").GetString() ?? "";
        var fileName = root.GetProperty("fileName").GetString() ?? "";
        var fileSize = root.GetProperty("fileSize").GetInt64();
        var chunkSize = root.GetProperty("chunkSize").GetInt32();
        var totalChunks = root.GetProperty("totalChunks").GetInt32();
        var sha256 = root.GetProperty("sha256").GetString() ?? "";
        var fromDeviceName = root.GetProperty("fromDeviceName").GetString() ?? "";

        var task = new FileTransferTask
        {
            FileId = fileId,
            FileName = fileName,
            FileSize = fileSize,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            Sha256 = sha256,
            FromDeviceId = fromDeviceId,
            FromDeviceName = fromDeviceName,
            Direction = TransferDirection.Receiving,
            Status = TransferStatus.Pending,
            ConnectionType = UseTurn ? ConnectionType.WebRtcTurn : ConnectionType.WebRtcP2p,
            StartTime = DateTime.Now
        };

        var saveDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TubaTransfer");
        System.IO.Directory.CreateDirectory(saveDir);
        task.SavePath = System.IO.Path.Combine(saveDir, task.FileName);

        _pendingReceives[fileId] = task;
        _receiveAckedChunks[fileId] = 0;

        _ = FileOfferReceived?.Invoke(task);
    }

    private static void HandleFileChunk(string fromDeviceId, System.Text.Json.JsonDocument doc)
    {
        var root = doc.RootElement;
        var fileId = root.GetProperty("fileId").GetString() ?? "";
        var index = root.GetProperty("index").GetInt32();
        var base64 = root.GetProperty("data").GetString() ?? "";
        var data = Convert.FromBase64String(base64);

        if (!_pendingReceives.TryGetValue(fileId, out var task)) return;

        if (!_receiveStreams.TryGetValue(fileId, out var fs))
        {
            task.Status = TransferStatus.Transferring;
            fs = new FileStream(task.SavePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
            _receiveStreams[fileId] = fs;
        }

        fs.Position = (long)index * task.ChunkSize;
        fs.Write(data, 0, data.Length);

        _receiveAckedChunks[fileId] = index + 1;
        task.BytesTransferred = (long)(index + 1) * task.ChunkSize;
        if (task.BytesTransferred > task.FileSize) task.BytesTransferred = task.FileSize;
        task.CompletedChunks = index + 1;
        TransferProgressChanged?.Invoke(task);
    }

    private static void HandleFileEof(string fromDeviceId, System.Text.Json.JsonDocument doc)
    {
        var root = doc.RootElement;
        var fileId = root.GetProperty("fileId").GetString() ?? "";

        if (!_pendingReceives.TryGetValue(fileId, out var task)) return;

        if (_receiveStreams.TryGetValue(fileId, out var fs))
        {
            fs.Flush();
            fs.Dispose();
            _receiveStreams.Remove(fileId);
        }

        task.CompletedChunks = task.TotalChunks;
        task.BytesTransferred = task.FileSize;
        task.SpeedMbps = 0;
        task.Status = TransferStatus.Completed;
        task.EndTime = DateTime.Now;
        TransferCompleted?.Invoke(task);

        _pendingReceives.Remove(fileId);
        _receiveAckedChunks.Remove(fileId);
    }

    public static void ClosePeerConnection(string targetDeviceId)
    {
        if (_peerConnections.TryGetValue(targetDeviceId, out var pc))
        {
            pc.close();
            _peerConnections.Remove(targetDeviceId);
        }
        _dataChannels.Remove(targetDeviceId);
    }

    public static void CloseAll()
    {
        foreach (var pc in _peerConnections.Values)
            pc.close();
        _peerConnections.Clear();
        _peerConnections.Clear();
        _dataChannels.Clear();
        _pendingSends.Clear();

        foreach (var fs in _receiveStreams.Values)
            fs.Dispose();
        _receiveStreams.Clear();
        _receiveAckedChunks.Clear();
        _pendingReceives.Clear();
    }
}
