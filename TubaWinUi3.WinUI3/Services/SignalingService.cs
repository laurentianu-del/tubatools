using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class SignalingService
{
    private static ClientWebSocket? _ws;
    private static CancellationTokenSource? _cts;
    private static Task? _receiveTask;
    private static string _signalingUrl = "wss://transfer.tubawinui3.cn";

    public static event Action<SignalingMessage>? MessageReceived;
    public static event Action? Connected;
    public static event Action? Disconnected;
    public static event Action<string>? Error;

    public static bool IsConnected => _ws?.State == WebSocketState.Open;

    public static string SignalingUrl
    {
        get => _signalingUrl;
        set => _signalingUrl = value;
    }

    public static async Task ConnectAsync(string groupCode, string? password = null, bool isCreator = false, CancellationToken ct = default)
    {
        if (IsConnected) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            if (!isCreator)
            {
                var infoUrl = $"{_signalingUrl.Replace("wss://", "https://").Replace("ws://", "http://")}/api/group/{groupCode}";
                using var httpClient = new HttpClient();
                try
                {
                    var infoResponse = await httpClient.GetAsync(infoUrl, _cts.Token);
                    if (infoResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Error?.Invoke("群组不存在");
                        return;
                    }
                }
                catch { }
            }

            _ws = new ClientWebSocket();
            var wsUrl = $"{_signalingUrl}/ws/group/{groupCode}?deviceId={Uri.EscapeDataString(FileTransferOrchestrator.DeviceId)}&deviceName={Uri.EscapeDataString(Environment.MachineName)}&lanIp={Uri.EscapeDataString(LanDiscoveryService.GetLocalIpAddress() ?? "")}";
            if (!string.IsNullOrEmpty(password))
                wsUrl += $"&password={Uri.EscapeDataString(password)}";

            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            Connected?.Invoke();

            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"连接失败: {ex.Message}");
            _ws?.Dispose();
            _ws = null;
        }
    }

    public static async Task DisconnectAsync()
    {
        _cts?.Cancel();

        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
            }
            catch { }
        }

        _ws?.Dispose();
        _ws = null;

        try { _receiveTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _receiveTask = null;
        _cts?.Dispose();
        _cts = null;

        Disconnected?.Invoke();
    }

    public static async Task SendMessageAsync(SignalingMessage msg, CancellationToken ct = default)
    {
        if (_ws?.State != WebSocketState.Open) return;

        try
        {
            msg.From ??= FileTransferOrchestrator.DeviceId;
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch { }
    }

    public static async Task SendSdpOfferAsync(string targetDeviceId, string sdp, CancellationToken ct = default)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = "sdp-offer",
            To = targetDeviceId,
            Sdp = sdp
        }, ct);
    }

    public static async Task SendSdpAnswerAsync(string targetDeviceId, string sdp, CancellationToken ct = default)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = "sdp-answer",
            To = targetDeviceId,
            Sdp = sdp
        }, ct);
    }

    public static async Task SendIceCandidateAsync(string targetDeviceId, string candidate, string? sdpMid, int sdpMlineIndex, CancellationToken ct = default)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = "ice-candidate",
            To = targetDeviceId,
            Candidate = candidate,
            SdpMid = sdpMid,
            SdpMlineIndex = sdpMlineIndex
        }, ct);
    }

    public static async Task SendFileOfferAsync(string targetDeviceId, FileTransferTask task, CancellationToken ct = default)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = "file-offer",
            To = targetDeviceId,
            FileId = task.FileId,
            FileName = task.FileName,
            FileSize = task.FileSize,
            ChunkSize = task.ChunkSize,
            TotalChunks = task.TotalChunks,
            Sha256 = task.Sha256
        }, ct);
    }

    public static async Task SendFileAcceptAsync(string targetDeviceId, string fileId, CancellationToken ct = default)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = "file-accept",
            To = targetDeviceId,
            FileId = fileId
        }, ct);
    }

    public static async Task SendFileRejectAsync(string targetDeviceId, string fileId, CancellationToken ct = default)
    {
        await SendMessageAsync(new SignalingMessage
        {
            Type = "file-reject",
            To = targetDeviceId,
            FileId = fileId
        }, ct);
    }

    private static async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Disconnected?.Invoke();
                    break;
                }

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var msg = JsonSerializer.Deserialize<SignalingMessage>(json);
                if (msg is not null)
                {
                    MessageReceived?.Invoke(msg);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
            catch { }
        }

        Disconnected?.Invoke();
    }

    public static async Task<string?> CreateGroupAsync(string groupName, string? password = null, CancellationToken ct = default)
    {
        var apiUrl = $"{_signalingUrl.Replace("wss://", "https://").Replace("ws://", "http://")}/api/group";
        using var httpClient = new HttpClient();
        var body = JsonSerializer.Serialize(new
        {
            groupName,
            password,
            creatorDeviceId = FileTransferOrchestrator.DeviceId
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(apiUrl, content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("groupCode", out var codeEl))
            return codeEl.GetString();
        return null;
    }
}
