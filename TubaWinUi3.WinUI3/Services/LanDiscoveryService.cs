using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class LanDiscoveryService
{
    private const int BroadcastPort = 19876;
    private const int BroadcastIntervalMs = 3000;
    private const int PacketTimeoutMs = 10000;

    private static UdpClient? _udpClient;
    private static CancellationTokenSource? _cts;
    private static Task? _listenTask;
    private static Task? _broadcastTask;
    private static string? _currentGroupId;

    public static event Action<LanDiscoveryPacket>? DeviceDiscovered;
    public static event Action<string>? DeviceExpired;

    private static readonly Dictionary<string, LanDiscoveryPacket> _discoveredDevices = [];
    public static IReadOnlyDictionary<string, LanDiscoveryPacket> DiscoveredDevices => _discoveredDevices;

    public static bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;

    public static string? GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            if (socket.LocalEndPoint is IPEndPoint ep)
                return ep.Address.ToString();
        }
        catch { }

        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return ip?.ToString();
        }
        catch { return null; }
    }

    public static string? GetSubnetPrefix()
    {
        var ip = GetLocalIpAddress();
        if (ip is null) return null;
        var parts = ip.Split('.');
        return $"{parts[0]}.{parts[1]}.{parts[2]}";
    }

    public static bool IsSameSubnet(string? otherIp)
    {
        if (otherIp is null) return false;
        var myPrefix = GetSubnetPrefix();
        return myPrefix is not null && otherIp.StartsWith(myPrefix);
    }

    public static void Start(string? groupId = null)
    {
        if (IsRunning) return;

        _currentGroupId = groupId;
        _cts = new CancellationTokenSource();

        _udpClient = new UdpClient();
        _udpClient.EnableBroadcast = true;
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));
        }
        catch (SocketException)
        {
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        }

        _listenTask = ListenAsync(_cts.Token);
        _broadcastTask = BroadcastLoopAsync(_cts.Token);
        _ = ExpireDevicesLoopAsync(_cts.Token);
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        _cts?.Dispose();
        _cts = null;

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
            _broadcastTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        _listenTask = null;
        _broadcastTask = null;
        _discoveredDevices.Clear();
    }

    public static void SetGroupId(string? groupId)
    {
        _currentGroupId = groupId;
    }

    private static async Task BroadcastLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var packet = LanDiscoveryPacket.Create(_currentGroupId);
                var json = packet.Serialize();
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);

                var broadcastAddr = GetBroadcastAddress();
                if (broadcastAddr is not null && _udpClient is not null)
                {
                    await _udpClient.SendAsync(bytes, bytes.Length, broadcastAddr.ToString(), BroadcastPort);
                }

                await Task.Delay(BroadcastIntervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private static async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient is not null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync().WaitAsync(ct);
                var json = System.Text.Encoding.UTF8.GetString(result.Buffer);

                if (result.RemoteEndPoint.Address.Equals(GetLocalIpAddress()) ||
                    result.RemoteEndPoint.Address.Equals(IPAddress.Loopback))
                    continue;

                var packet = LanDiscoveryPacket.Deserialize(json);
                if (packet is null || packet.DeviceId == FileTransferOrchestrator.DeviceId)
                    continue;

                _discoveredDevices[packet.DeviceId] = packet;
                DeviceDiscovered?.Invoke(packet);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private static async Task ExpireDevicesLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct);
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var expired = _discoveredDevices
                    .Where(kvp => now - kvp.Value.Timestamp > PacketTimeoutMs)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var id in expired)
                {
                    _discoveredDevices.Remove(id);
                    DeviceExpired?.Invoke(id);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private static IPAddress? GetBroadcastAddress()
    {
        try
        {
            var localIp = GetLocalIpAddress();
            if (localIp is null) return null;
            var parts = localIp.Split('.');
            return IPAddress.Parse($"{parts[0]}.{parts[1]}.{parts[2]}.255");
        }
        catch { return IPAddress.Broadcast; }
    }
}
