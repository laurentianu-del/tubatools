using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class FileTransferOrchestrator
{
    private static string? _deviceId;
    public static string DeviceId
    {
        get
        {
            if (_deviceId is not null) return _deviceId;
            var machineName = Environment.MachineName;
            var macAddr = System.Net.NetworkInformation.NetworkInterface
                .GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)?
                .GetPhysicalAddress().ToString() ?? "nomac";
            _deviceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{machineName}:{macAddr}")))[..12];
            return _deviceId;
        }
    }

    private static TransferGroup? _currentGroup;
    private static readonly Dictionary<string, FileTransferTask> _activeTasks = [];
    private static readonly Dictionary<string, CancellationTokenSource> _taskCts = [];
    private static readonly Dictionary<string, string> _peerConnectionTypes = [];

    public static TransferGroup? CurrentGroup => _currentGroup;
    public static IReadOnlyDictionary<string, FileTransferTask> ActiveTasks => _activeTasks;

    public static event Action<TransferGroup>? GroupJoined;
    public static event Action? GroupLeft;
    public static event Action<GroupDevice>? DeviceJoined;
    public static event Action<string>? DeviceLeft;
    public static event Action<FileTransferTask>? TransferStarted;
    public static event Action<FileTransferTask>? TransferProgressChanged;
    public static event Action<FileTransferTask>? TransferCompleted;
    public static event Action<FileTransferTask>? TransferFailed;
    public static event Func<FileTransferTask, Task<bool>>? FileOfferReceived;
    public static event Action<string>? Error;

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        SignalingService.MessageReceived += HandleSignalingMessage;
        SignalingService.Connected += () => { };
        SignalingService.Disconnected += () => { };
        SignalingService.Error += (msg) => Error?.Invoke(msg);

        WebRtcTransferService.IceCandidateReady += async (targetId, candidate) =>
        {
            await SignalingService.SendIceCandidateAsync(targetId, candidate, null, 0);
        };

        WebRtcTransferService.SdpOfferReady += async (targetId, sdp) =>
        {
            await SignalingService.SendSdpOfferAsync(targetId, sdp);
        };

        WebRtcTransferService.SdpAnswerReady += async (targetId, sdp) =>
        {
            await SignalingService.SendSdpAnswerAsync(targetId, sdp);
        };

        WebRtcTransferService.PeerConnected += (deviceId) => { };
        WebRtcTransferService.PeerDisconnected += (deviceId) => { };

        WebRtcTransferService.TransferProgressChanged += (task) => TransferProgressChanged?.Invoke(task);
        WebRtcTransferService.TransferCompleted += (task) =>
        {
            _activeTasks.Remove(task.FileId);
            _taskCts.Remove(task.FileId);
            TransferCompleted?.Invoke(task);
        };
        WebRtcTransferService.TransferFailed += (task) =>
        {
            _activeTasks.Remove(task.FileId);
            _taskCts.Remove(task.FileId);
            TransferFailed?.Invoke(task);
        };

        WebRtcTransferService.FileOfferReceived += async (task) =>
        {
            var accept = await FileOfferReceived(task);
            if (accept)
            {
                task.Status = TransferStatus.Transferring;
                TransferStarted?.Invoke(task);
            }
            else
            {
                await SignalingService.SendFileRejectAsync(task.FromDeviceId, task.FileId);
            }
        };

        LanDiscoveryService.DeviceDiscovered += (packet) =>
        {
            if (_currentGroup is null) return;

            var device = _currentGroup.Devices.FirstOrDefault(d => d.DeviceId == packet.DeviceId);
            if (device is not null)
            {
                device.LanIp = packet.LanIp;
                device.IsOnline = true;
                device.PreferredConnection = ConnectionType.Lan;
            }
            else if (packet.GroupId == _currentGroup.GroupId)
            {
                device = new GroupDevice
                {
                    DeviceId = packet.DeviceId,
                    DeviceName = packet.DeviceName,
                    LanIp = packet.LanIp,
                    IsOnline = true,
                    PreferredConnection = ConnectionType.Lan,
                    JoinedAt = DateTime.Now
                };
                _currentGroup.Devices.Add(device);
                DeviceJoined?.Invoke(device);
            }
        };

        LanDiscoveryService.DeviceExpired += (deviceId) =>
        {
            if (_currentGroup is null) return;
            var device = _currentGroup.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device is not null)
                device.IsOnline = false;
            DeviceLeft?.Invoke(deviceId);
        };

        LanTransferService.FileOfferReceived += async (task) =>
        {
            var accept = await FileOfferReceived(task);
            if (accept)
            {
                task.Status = TransferStatus.Transferring;
                _activeTasks[task.FileId] = task;
                TransferStarted?.Invoke(task);
            }
        };

        LanTransferService.TransferProgressChanged += (task) => TransferProgressChanged?.Invoke(task);
        LanTransferService.TransferCompleted += (task) =>
        {
            _activeTasks.Remove(task.FileId);
            TransferCompleted?.Invoke(task);
        };
        LanTransferService.TransferFailed += (task) =>
        {
            _activeTasks.Remove(task.FileId);
            TransferFailed?.Invoke(task);
        };
    }

    public static async Task CreateGroupAsync(string groupName, string? password = null)
    {
        Initialize();

        try
        {
            var groupCode = await SignalingService.CreateGroupAsync(groupName, password);
            if (string.IsNullOrEmpty(groupCode))
            {
                Error?.Invoke("创建群组失败: 服务器未返回群组码");
                return;
            }

            _currentGroup = new TransferGroup
            {
                GroupId = groupCode,
                GroupName = groupName,
                Password = password ?? "",
                CreatorDeviceId = DeviceId,
                CreatedAt = DateTime.Now
            };

            var selfDevice = new GroupDevice
            {
                DeviceId = DeviceId,
                DeviceName = Environment.MachineName,
                LanIp = LanDiscoveryService.GetLocalIpAddress(),
                IsOnline = true,
                PreferredConnection = ConnectionType.Lan,
                JoinedAt = DateTime.Now
            };
            _currentGroup.Devices.Add(selfDevice);

            await SignalingService.ConnectAsync(groupCode, password, isCreator: true);
            LanDiscoveryService.Start(groupCode);
            LanTransferService.StartListening();

            GroupJoined?.Invoke(_currentGroup);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"创建群组失败: {ex.Message}");
        }
    }

    public static async Task JoinGroupAsync(string groupCode, string? password = null)
    {
        Initialize();

        try
        {
            groupCode = groupCode.ToUpperInvariant().Trim();

            _currentGroup = new TransferGroup
            {
                GroupId = groupCode,
                GroupName = $"群组 {groupCode}",
                Password = password ?? "",
                CreatedAt = DateTime.Now
            };

            var selfDevice = new GroupDevice
            {
                DeviceId = DeviceId,
                DeviceName = Environment.MachineName,
                LanIp = LanDiscoveryService.GetLocalIpAddress(),
                IsOnline = true,
                PreferredConnection = ConnectionType.Lan,
                JoinedAt = DateTime.Now
            };
            _currentGroup.Devices.Add(selfDevice);

            await SignalingService.ConnectAsync(groupCode, password);
            LanDiscoveryService.Start(groupCode);
            LanTransferService.StartListening();

            GroupJoined?.Invoke(_currentGroup);
        }
        catch (Exception ex)
        {
            _currentGroup = null;
            Error?.Invoke($"加入群组失败: {ex.Message}");
        }
    }

    public static async Task LeaveGroupAsync()
    {
        foreach (var cts in _taskCts.Values)
            cts.Cancel();
        _taskCts.Clear();
        _activeTasks.Clear();

        LanDiscoveryService.Stop();
        LanTransferService.StopListening();
        WebRtcTransferService.CloseAll();
        await SignalingService.DisconnectAsync();

        _currentGroup = null;
        _peerConnectionTypes.Clear();
        GroupLeft?.Invoke();
    }

    public static async Task SendFileAsync(string filePath, string targetDeviceId)
    {
        Initialize();

        var task = new FileTransferTask
        {
            FileId = Guid.NewGuid().ToString("N")[..12],
            FilePath = filePath,
            FileName = System.IO.Path.GetFileName(filePath),
            Direction = TransferDirection.Sending,
            ToDeviceId = targetDeviceId,
            Status = TransferStatus.Pending,
            StartTime = DateTime.Now
        };

        var fileInfo = new System.IO.FileInfo(filePath);
        task.FileSize = fileInfo.Length;
        task.Sha256 = await FileChunkService.ComputeSha256Async(filePath);

        _activeTasks[task.FileId] = task;
        var cts = new CancellationTokenSource();
        _taskCts[task.FileId] = cts;

        TransferStarted?.Invoke(task);

        var targetDevice = _currentGroup?.Devices.FirstOrDefault(d => d.DeviceId == targetDeviceId);

        if (targetDevice?.LanIp is not null && LanDiscoveryService.IsSameSubnet(targetDevice.LanIp))
        {
            task.ConnectionType = ConnectionType.Lan;
            targetDevice.PreferredConnection = ConnectionType.Lan;
            _ = LanTransferService.SendFileAsync(filePath, targetDevice.LanIp, LanTransferService.DefaultPort, task, cts.Token);
        }
        else
        {
            task.ConnectionType = ConnectionType.WebRtcP2p;
            if (targetDevice is not null)
                targetDevice.PreferredConnection = ConnectionType.WebRtcP2p;

            try
            {
                _ = WebRtcTransferService.SendFileAsync(targetDeviceId, filePath, task, cts.Token);
            }
            catch (Exception ex)
            {
                task.Status = TransferStatus.Failed;
                task.ErrorMessage = $"P2P 连接失败: {ex.Message}";
                TransferFailed?.Invoke(task);
            }
        }
    }

    public static async Task SendFileToAllAsync(string filePath)
    {
        if (_currentGroup is null) return;

        var targets = _currentGroup.Devices
            .Where(d => d.DeviceId != DeviceId && d.IsOnline)
            .Select(d => d.DeviceId)
            .ToList();

        foreach (var targetId in targets)
        {
            await SendFileAsync(filePath, targetId);
        }
    }

    public static void CancelTransfer(string fileId)
    {
        if (_taskCts.TryGetValue(fileId, out var cts))
        {
            cts.Cancel();
            _taskCts.Remove(fileId);
        }
        _activeTasks.Remove(fileId);
    }

    public static void SetSignalingUrl(string url)
    {
        SignalingService.SignalingUrl = url;
    }

    public static void ConfigureTurn(string url, string? username = null, string? credential = null)
    {
        WebRtcTransferService.ConfigureTurn(url, username, credential);
    }

    private static async void HandleSignalingMessage(SignalingMessage msg)
    {
        switch (msg.Type)
        {
            case "joined":
                HandleJoinedMessage(msg);
                break;

            case "device-joined":
                HandleDeviceJoinedMessage(msg);
                break;

            case "device-left":
                HandleDeviceLeftMessage(msg);
                break;

            case "sdp-offer":
                await HandleSdpOfferMessage(msg);
                break;

            case "sdp-answer":
                HandleSdpAnswerMessage(msg);
                break;

            case "ice-candidate":
                HandleIceCandidateMessage(msg);
                break;

            case "file-offer":
                await HandleFileOfferMessage(msg);
                break;

            case "file-accept":
                break;

            case "file-reject":
                HandleFileRejectMessage(msg);
                break;
        }
    }

    private static void HandleJoinedMessage(SignalingMessage msg)
    {
        if (_currentGroup is null || msg.GroupCode is null) return;

        _currentGroup.GroupId = msg.GroupCode;
        if (msg.GroupName is not null)
            _currentGroup.GroupName = msg.GroupName;
    }

    private static void HandleDeviceJoinedMessage(SignalingMessage msg)
    {
        if (_currentGroup is null) return;

        string? deviceId = msg.DeviceId;
        string? deviceName = msg.DeviceName;
        string? lanIp = msg.LanIp;
        long? joinedAt = msg.JoinedAt;

        if (deviceId is null && msg is { } m)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(m);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("device", out var deviceEl))
            {
                deviceId = deviceEl.TryGetProperty("deviceId", out var idEl) ? idEl.GetString() : null;
                deviceName = deviceEl.TryGetProperty("deviceName", out var nameEl) ? nameEl.GetString() : null;
                lanIp = deviceEl.TryGetProperty("lanIp", out var ipEl) ? ipEl.GetString() : null;
                joinedAt = deviceEl.TryGetProperty("joinedAt", out var atEl) ? atEl.GetInt64() : null;
            }
        }

        if (deviceId is null) return;

        var existing = _currentGroup.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (existing is not null)
        {
            existing.IsOnline = true;
            if (lanIp is not null) existing.LanIp = lanIp;
            if (deviceName is not null) existing.DeviceName = deviceName;
            existing.PreferredConnection = lanIp is not null && LanDiscoveryService.IsSameSubnet(lanIp)
                ? ConnectionType.Lan
                : ConnectionType.WebRtcP2p;
            return;
        }

        var device = new GroupDevice
        {
            DeviceId = deviceId,
            DeviceName = deviceName ?? "未知设备",
            LanIp = lanIp,
            IsOnline = true,
            PreferredConnection = lanIp is not null && LanDiscoveryService.IsSameSubnet(lanIp)
                ? ConnectionType.Lan
                : ConnectionType.WebRtcP2p,
            JoinedAt = joinedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(joinedAt.Value).DateTime : DateTime.Now
        };

        _currentGroup.Devices.Add(device);
        DeviceJoined?.Invoke(device);
    }

    private static void HandleDeviceLeftMessage(SignalingMessage msg)
    {
        if (_currentGroup is null || msg.DeviceId is null) return;

        var device = _currentGroup.Devices.FirstOrDefault(d => d.DeviceId == msg.DeviceId);
        if (device is not null)
            device.IsOnline = false;

        DeviceLeft?.Invoke(msg.DeviceId);
    }

    private static async Task HandleSdpOfferMessage(SignalingMessage msg)
    {
        if (msg.From is null || msg.Sdp is null) return;

        try
        {
            var answer = await WebRtcTransferService.HandleOfferAsync(msg.From, msg.Sdp);
            await SignalingService.SendSdpAnswerAsync(msg.From, answer);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"处理 SDP Offer 失败: {ex.Message}");
        }
    }

    private static void HandleSdpAnswerMessage(SignalingMessage msg)
    {
        if (msg.From is null || msg.Sdp is null) return;
        WebRtcTransferService.HandleAnswer(msg.From, msg.Sdp);
    }

    private static void HandleIceCandidateMessage(SignalingMessage msg)
    {
        if (msg.From is null || msg.Candidate is null) return;
        WebRtcTransferService.AddIceCandidate(msg.From, msg.Candidate, msg.SdpMid, msg.SdpMlineIndex ?? 0);
    }

    private static async Task HandleFileOfferMessage(SignalingMessage msg)
    {
        if (msg.From is null || msg.FileId is null) return;

        var task = new FileTransferTask
        {
            FileId = msg.FileId,
            FileName = msg.FileName ?? "未知文件",
            FileSize = msg.FileSize ?? 0,
            ChunkSize = msg.ChunkSize ?? 16384,
            TotalChunks = msg.TotalChunks ?? 0,
            Sha256 = msg.Sha256 ?? "",
            FromDeviceId = msg.From,
            FromDeviceName = "",
            Direction = TransferDirection.Receiving,
            Status = TransferStatus.Pending,
            ConnectionType = ConnectionType.WebRtcP2p,
            StartTime = DateTime.Now
        };

        var fromDevice = _currentGroup?.Devices.FirstOrDefault(d => d.DeviceId == msg.From);
        if (fromDevice is not null)
        {
            task.FromDeviceName = fromDevice.DeviceName;
            task.ConnectionType = fromDevice.PreferredConnection;
        }

        var accept = await FileOfferReceived(task);
        if (accept)
        {
            await SignalingService.SendFileAcceptAsync(msg.From, msg.FileId);
            task.Status = TransferStatus.Transferring;
            _activeTasks[task.FileId] = task;
            TransferStarted?.Invoke(task);
        }
        else
        {
            await SignalingService.SendFileRejectAsync(msg.From, msg.FileId);
        }
    }

    private static void HandleFileRejectMessage(SignalingMessage msg)
    {
        if (msg.FileId is null) return;

        if (_activeTasks.TryGetValue(msg.FileId, out var task))
        {
            task.Status = TransferStatus.Failed;
            task.ErrorMessage = "接收方拒绝";
            _activeTasks.Remove(msg.FileId);
            TransferFailed?.Invoke(task);
        }
    }
}
