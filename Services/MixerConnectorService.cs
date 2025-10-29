using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Eggbox.Models;
using Microsoft.Extensions.Logging;
using Optional;
using Optional.Async.Extensions;
using OscCore;
#if ANDROID
using Android.Net.Wifi;
using Android.Content;
#endif

namespace Eggbox.Services;

public sealed class MixerConnectorService : IDisposable, IMixerTransport
{
    private readonly ILogger<MixerConnectorService> _logger;
    private readonly MixerStateCacheService _stateCache;
    private readonly MixerCommandService _commands;

    private UdpOscClient? _client;
    private const int Port = 10024;

    public Option<string> ConnectedMixerIp { get; private set; }
    public event Action<ConnectState>? OnConnectionStateChanged;
    public event Action<string, bool>? OnOscLog;
    public event Action<int, string, MixerColor>? OnBusUpdated;
    public event Action? OnBusStateReceived;
    public event Action<object?, OscPacket>? MessageReceived;
#if ANDROID
    private WifiManager.MulticastLock? _multicastLock;
#endif

    public MixerConnectorService(
        ILogger<MixerConnectorService> logger,
        MixerStateCacheService stateCache)
    {
        _logger = logger;
        _stateCache = stateCache;
    }

    // ---------------- Connectivity ----------------
    public async Task SendAsync(OscMessage msg)
        => await _client.SendAsync(msg);

    public async Task TryAutoReconnectAsync()
    {
        var lastMixer = Storage.Get<MixerInfo?>(StorageKeys.LastMixer);
        bool hadPrevious = false;

        await lastMixer.MatchSomeAsync(async lm =>
        {
            hadPrevious = true;

            if (CheckWifiMismatch(lm.IpAddress) == ConnectState.WifiMismatch)
            {
                OnConnectionStateChanged?.Invoke(ConnectState.WifiMismatch);
                return;
            }

            OnConnectionStateChanged?.Invoke(ConnectState.Connecting);
            var success = await ConnectAsync(lm);
            OnConnectionStateChanged?.Invoke(success ? ConnectState.Connected : ConnectState.ScanRequired);
        });

        if (!hadPrevious)
            OnConnectionStateChanged?.Invoke(ConnectState.ScanRequired);
    }

    public async Task<bool> ConnectAsync(MixerInfo mixer)
    {
        try
        {
            var ip = mixer.IpAddress;
            if (CheckWifiMismatch(ip) == ConnectState.WifiMismatch)
            {
                OnConnectionStateChanged?.Invoke(ConnectState.WifiMismatch);
                return false;
            }

            Storage.Set(StorageKeys.LastMixer, mixer);
            Disconnect();

            _client = new UdpOscClient(ip, Port, _logger); // ⬅️ ctor stuurt /xremote en zet keepalive timer:contentReference[oaicite:2]{index=2}
            _client.PacketReceived += HandleIncomingPacket;
            _client.PacketSent += (_, msg) =>
                OnOscLog?.Invoke($"➡️ {msg.Address} {string.Join(' ', msg.Select(a => a.ToString()))}", true);

            ConnectedMixerIp = ip.SomeNotNull();
            _logger.LogInformation("✅ Verbonden met mixer op {ip}", ip);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Fout bij verbinden met mixer");
            return false;
        }
    }

    public void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        ConnectedMixerIp = Option.None<string>();
        _logger.LogInformation("🔌 Verbinding verbroken");
    }

    public async Task<List<MixerInfo>> ScanViaBroadcastAsync()
    {
#if ANDROID
        AcquireMulticastLock();
#endif
        try
        {
            var mixers = new List<MixerInfo>();
            var broadcast = IPAddress.Broadcast;
            var data = Encoding.ASCII.GetBytes("/xinfo");

            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Client.ReceiveTimeout = 2000;

            _logger.LogInformation("Broadcasting /xinfo → {port}", Port);
            await udp.SendAsync(data, data.Length, new IPEndPoint(broadcast, Port));

            var started = DateTime.UtcNow;
            while ((DateTime.UtcNow - started).TotalMilliseconds < 2000)
            {
                var remaining = 2000 - (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (remaining <= 0) break;

                var receiveTask = udp.ReceiveAsync();
                var timeout = Task.Delay(remaining);
                var completed = await Task.WhenAny(receiveTask, timeout);

                if (completed != receiveTask) break;
                var result = await receiveTask;
                var rip = result.RemoteEndPoint.Address.ToString();
                var response = Encoding.ASCII.GetString(result.Buffer);

                if (response.ToLowerInvariant().Contains("xinfo") && !mixers.Any(m => m.IpAddress == rip))
                    mixers.Add(new MixerInfo(rip, response));
            }

            _logger.LogInformation("🔍 Scan voltooid. Mixers: {count}", mixers.Count);
            return mixers;
        }
        finally
        {
#if ANDROID
            ReleaseMulticastLock();
#endif
        }
    }

    // ---------------- OSC processing ----------------

    private void HandleIncomingPacket(object? s, OscPacket packet)
    {
        if (packet is not OscMessage msg) return;

        OnOscLog?.Invoke($"⬅️ {msg.Address} {string.Join(' ', msg.Select(a => a.ToString()))}", false);

        // centraal verwerken in cache
        _stateCache.UpdateFromMessage(msg);

        // Specifieke bus-events naar UI
        if (msg.Address.EndsWith("/config/name") || msg.Address.EndsWith("/config/color"))
        {
            int bus = ExtractBusIndex(msg.Address);
            if (bus > 0)
            {
                var name = _stateCache.BusNames.GetValueOrDefault(bus, "");
                var color = _stateCache.BusColors.GetValueOrDefault(bus, MixerColor.Red);
                OnBusUpdated?.Invoke(bus, name, color);
                OnBusStateReceived?.Invoke();
            }
        }
    }

    public Task SendCustomAsync(OscMessage msg)
        => _client?.SendAsync(msg) ?? Task.CompletedTask;


    // ---------------- Helpers ----------------

#if ANDROID
    private void AcquireMulticastLock()
    {
        var context = Android.App.Application.Context;
        var wifiManager = (WifiManager)context.GetSystemService(Context.WifiService)!;
        _multicastLock = wifiManager.CreateMulticastLock("eggbox-multicast");
        _multicastLock.SetReferenceCounted(true);
        _multicastLock.Acquire();
    }

    private void ReleaseMulticastLock()
    {
        if (_multicastLock?.IsHeld == true)
        {
            _multicastLock.Release();
            _multicastLock = null;
        }
    }
#endif

    private static int ExtractBusIndex(string addr)
    {
        try
        {
            var parts = addr.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && int.TryParse(parts[1], out var i) ? i : -1;
        }
        catch { return -1; }
    }

    private static ConnectState CheckWifiMismatch(string mixerIp)
    {
        if (!IPAddress.TryParse(mixerIp, out var ip)) return ConnectState.WifiMismatch;

#if ANDROID || IOS
        var subnetLocal = GetLocalSubnet();
        var subnetMixer = string.Join('.', ip.GetAddressBytes().Take(3)) + ".";
        if (subnetLocal != subnetMixer)
            return ConnectState.WifiMismatch;
#endif
        return ConnectState.Connected;
    }

    private static string GetLocalSubnet()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var b = unicast.Address.GetAddressBytes();
                    return $"{b[0]}.{b[1]}.{b[2]}.";
                }
            }
        }
        throw new InvalidOperationException("Geen lokaal IPv4-adres gevonden");
    }

    public void Dispose() => _client?.Dispose();
}
