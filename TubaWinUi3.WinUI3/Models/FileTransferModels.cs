using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

public enum ConnectionType
{
    Lan,
    WebRtcP2p,
    WebRtcTurn,
    WebSocketRelay
}

public enum TransferDirection
{
    Sending,
    Receiving
}

public enum TransferStatus
{
    Pending,
    Connecting,
    Transferring,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public sealed class TransferGroup : INotifyPropertyChanged
{
    private string _groupId = "";
    private string _groupName = "";
    private string _password = "";
    private string _creatorDeviceId = "";
    private DateTime _createdAt;

    public string GroupId { get => _groupId; set { _groupId = value; OnPropertyChanged(nameof(GroupId)); } }
    public string GroupName { get => _groupName; set { _groupName = value; OnPropertyChanged(nameof(GroupName)); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(nameof(Password)); } }
    public string CreatorDeviceId { get => _creatorDeviceId; set { _creatorDeviceId = value; OnPropertyChanged(nameof(CreatorDeviceId)); } }
    public DateTime CreatedAt { get => _createdAt; set { _createdAt = value; OnPropertyChanged(nameof(CreatedAt)); } }

    public ObservableCollection<GroupDevice> Devices { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GroupDevice : INotifyPropertyChanged
{
    private string _deviceId = "";
    private string _deviceName = "";
    private string? _lanIp;
    private bool _isOnline;
    private DateTime _joinedAt;
    private ConnectionType _preferredConnection;

    public string DeviceId { get => _deviceId; set { _deviceId = value; OnPropertyChanged(nameof(DeviceId)); } }
    public string DeviceName { get => _deviceName; set { _deviceName = value; OnPropertyChanged(nameof(DeviceName)); } }
    public string? LanIp { get => _lanIp; set { _lanIp = value; OnPropertyChanged(nameof(LanIp)); } }
    public bool IsOnline { get => _isOnline; set { _isOnline = value; OnPropertyChanged(nameof(IsOnline)); } }
    public DateTime JoinedAt { get => _joinedAt; set { _joinedAt = value; OnPropertyChanged(nameof(JoinedAt)); } }
    public ConnectionType PreferredConnection { get => _preferredConnection; set { _preferredConnection = value; OnPropertyChanged(nameof(PreferredConnection)); } }

    public string ConnectionTypeLabel => PreferredConnection switch
    {
        ConnectionType.Lan => "局域网",
        ConnectionType.WebRtcP2p => "P2P",
        ConnectionType.WebRtcTurn => "TURN中继",
        ConnectionType.WebSocketRelay => "WS中继",
        _ => "未知"
    };

    public string ConnectionTypeGlyph => PreferredConnection switch
    {
        ConnectionType.Lan => "\uE8AB",
        ConnectionType.WebRtcP2p => "\uE968",
        ConnectionType.WebRtcTurn => "\uE774",
        ConnectionType.WebSocketRelay => "\uE912",
        _ => "\uE968"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FileTransferTask : INotifyPropertyChanged
{
    private string _taskId = Guid.NewGuid().ToString("N")[..16];
    private string _fileId = "";
    private string _fileName = "";
    private long _fileSize;
    private string _filePath = "";
    private string _sha256 = "";
    private int _chunkSize;
    private int _totalChunks;
    private int _completedChunks;
    private TransferDirection _direction;
    private TransferStatus _status;
    private ConnectionType _connectionType;
    private string _fromDeviceId = "";
    private string _fromDeviceName = "";
    private string _toDeviceId = "";
    private string _toDeviceName = "";
    private double _speedMbps;
    private long _bytesTransferred;
    private DateTime _startTime;
    private DateTime? _endTime;
    private string _errorMessage = "";
    private string _savePath = "";

    public string TaskId { get => _taskId; set { _taskId = value; OnPropertyChanged(nameof(TaskId)); } }
    public string FileId { get => _fileId; set { _fileId = value; OnPropertyChanged(nameof(FileId)); } }
    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(nameof(FileName)); } }
    public long FileSize { get => _fileSize; set { _fileSize = value; OnPropertyChanged(nameof(FileSize)); } }
    public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(nameof(FilePath)); } }
    public string Sha256 { get => _sha256; set { _sha256 = value; OnPropertyChanged(nameof(Sha256)); } }
    public int ChunkSize { get => _chunkSize; set { _chunkSize = value; OnPropertyChanged(nameof(ChunkSize)); } }
    public int TotalChunks { get => _totalChunks; set { _totalChunks = value; OnPropertyChanged(nameof(TotalChunks)); } }
    public int CompletedChunks { get => _completedChunks; set { _completedChunks = value; OnPropertyChanged(nameof(CompletedChunks)); OnPropertyChanged(nameof(Progress)); } }
    public TransferDirection Direction { get => _direction; set { _direction = value; OnPropertyChanged(nameof(Direction)); } }
    public TransferStatus Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); } }
    public ConnectionType ConnectionType { get => _connectionType; set { _connectionType = value; OnPropertyChanged(nameof(ConnectionType)); OnPropertyChanged(nameof(ConnectionTypeLabel)); } }
    public string FromDeviceId { get => _fromDeviceId; set { _fromDeviceId = value; OnPropertyChanged(nameof(FromDeviceId)); } }
    public string FromDeviceName { get => _fromDeviceName; set { _fromDeviceName = value; OnPropertyChanged(nameof(FromDeviceName)); } }
    public string ToDeviceId { get => _toDeviceId; set { _toDeviceId = value; OnPropertyChanged(nameof(ToDeviceId)); } }
    public string ToDeviceName { get => _toDeviceName; set { _toDeviceName = value; OnPropertyChanged(nameof(ToDeviceName)); } }
    public double SpeedMbps { get => _speedMbps; set { _speedMbps = value; OnPropertyChanged(nameof(SpeedMbps)); OnPropertyChanged(nameof(SpeedText)); } }
    public long BytesTransferred { get => _bytesTransferred; set { _bytesTransferred = value; OnPropertyChanged(nameof(BytesTransferred)); OnPropertyChanged(nameof(Progress)); } }
    public DateTime StartTime { get => _startTime; set { _startTime = value; OnPropertyChanged(nameof(StartTime)); } }
    public DateTime? EndTime { get => _endTime; set { _endTime = value; OnPropertyChanged(nameof(EndTime)); } }
    public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); } }
    public string SavePath { get => _savePath; set { _savePath = value; OnPropertyChanged(nameof(SavePath)); } }

    public double Progress => TotalChunks > 0 ? (double)CompletedChunks / TotalChunks * 100 : 0;
    public string StatusText => Status switch
    {
        TransferStatus.Pending => "等待中",
        TransferStatus.Connecting => "连接中",
        TransferStatus.Transferring => "传输中",
        TransferStatus.Paused => "已暂停",
        TransferStatus.Completed => "已完成",
        TransferStatus.Failed => "失败",
        TransferStatus.Cancelled => "已取消",
        _ => "未知"
    };
    public string ConnectionTypeLabel => ConnectionType switch
    {
        ConnectionType.Lan => "局域网",
        ConnectionType.WebRtcP2p => "P2P",
        ConnectionType.WebRtcTurn => "TURN",
        ConnectionType.WebSocketRelay => "中继",
        _ => ""
    };
    public string SpeedText => SpeedMbps switch
    {
        >= 1000 => $"{SpeedMbps / 1000:F2} GB/s",
        >= 1 => $"{SpeedMbps:F1} MB/s",
        > 0 => $"{SpeedMbps * 1024:F0} KB/s",
        _ => ""
    };
    public string FileSizeText => FileSize switch
    {
        >= 1L << 30 => $"{(double)FileSize / (1L << 30):F2} GB",
        >= 1L << 20 => $"{(double)FileSize / (1L << 20):F1} MB",
        >= 1L << 10 => $"{(double)FileSize / (1L << 10):F0} KB",
        _ => $"{FileSize} B"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FileChunk
{
    public int Index { get; init; }
    public byte[] Data { get; init; } = [];
    public string Checksum { get; init; } = "";
}

public sealed class SignalingMessage
{
    public string Type { get; set; } = "";
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Sdp { get; set; }
    public string? Candidate { get; set; }
    public string? SdpMid { get; set; }
    public int? SdpMlineIndex { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public int? ChunkSize { get; set; }
    public int? TotalChunks { get; set; }
    public string? Sha256 { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? LanIp { get; set; }
    public long? JoinedAt { get; set; }
    public string? GroupCode { get; set; }
    public string? GroupName { get; set; }
    public string? Password { get; set; }
    public string? ErrorMessage { get; set; }
    public long? Timestamp { get; set; }
}

public sealed class LanDiscoveryPacket
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string LanIp { get; set; } = "";
    public string? GroupId { get; set; }
    public int TcpPort { get; set; } = LanTransferService.DefaultPort;
    public long Timestamp { get; set; }

    public static LanDiscoveryPacket Create(string? groupId = null)
    {
        return new LanDiscoveryPacket
        {
            DeviceId = FileTransferOrchestrator.DeviceId,
            DeviceName = Environment.MachineName,
            LanIp = LanDiscoveryService.GetLocalIpAddress() ?? "",
            GroupId = groupId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public string Serialize() => System.Text.Json.JsonSerializer.Serialize(this);

    public static LanDiscoveryPacket? Deserialize(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<LanDiscoveryPacket>(json); }
        catch { return null; }
    }
}

public sealed class LanTransferHeader
{
    public string FileId { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public string Sha256 { get; set; } = "";
    public string FromDeviceId { get; set; } = "";
    public string FromDeviceName { get; set; } = "";

    public string Serialize() => System.Text.Json.JsonSerializer.Serialize(this);

    public static LanTransferHeader? Deserialize(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<LanTransferHeader>(json); }
        catch { return null; }
    }
}
