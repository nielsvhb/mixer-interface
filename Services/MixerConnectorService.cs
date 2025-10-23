using System.Diagnostics;
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

public sealed class MixerConnectorService(ILogger<MixerConnectorService> logger)
{
    public Option<string> ConnectedMixerIp { get; private set; }

    private UdpOscClient? _client;
    private const int Port = 10024;
    private readonly Dictionary<int, string> _busNames = new();
    private readonly Dictionary<int, MixerColor> _busColors = new();
    public IReadOnlyDictionary<int, string> BusNames => _busNames;
    public IReadOnlyDictionary<int, MixerColor> BusColors => _busColors;
    private TaskCompletionSource<bool>? _pingTcs;
    public event Action<ConnectState>? OnConnectionStateChanged;
    public event Action<int, string, MixerColor>? OnBusUpdated;


#if ANDROID
    private WifiManager.MulticastLock? _multicastLock;

    private void AcquireMulticastLock()
    {
        var context = Android.App.Application.Context;
        var wifiManager = (WifiManager)context.GetSystemService(Context.WifiService)!;

        _multicastLock = wifiManager.CreateMulticastLock("eggbox-multicast-lock");
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

    public async Task TryAutoReconnectAsync()
    {
        var lastMixer = Storage.Get<MixerInfo?>(StorageKeys.LastMixer);
        bool hadPreviousMixer = false;

        await lastMixer.MatchSomeAsync(async lm =>
        {
            hadPreviousMixer = true;

            var wifiState = CheckWifiMismatch(lm.IpAddress);
            if (wifiState == ConnectState.WifiMismatch)
            {
                OnConnectionStateChanged?.Invoke(ConnectState.WifiMismatch);
                return;
            }

            OnConnectionStateChanged?.Invoke(ConnectState.Connecting);

            Connect(lm);

            var success = await PingMixerAsync(lm.IpAddress);
            if (success)
            {
                OnConnectionStateChanged?.Invoke(ConnectState.Connected);
            }
            else
            {
                OnConnectionStateChanged?.Invoke(ConnectState.ScanRequired);
            }
        });

        if (!hadPreviousMixer)
        {
            OnConnectionStateChanged?.Invoke(ConnectState.ScanRequired);
        }
    }

    private async Task<bool> PingMixerAsync(string ip, int timeoutMs = 500)
    {
        if (_client == null)
            return false;

        _pingTcs = new TaskCompletionSource<bool>();

        void Handler(object? sender, OscPacket packet)
        {
            if (packet is OscMessage msg && msg.Address == "/xinfo")
            {
                _pingTcs?.TrySetResult(true);
            }
        }

        _client.PacketReceived += Handler;

        try
        {
            await _client.SendAsync(new OscMessage("/xinfo"));

            // wacht op respons of timeout
            var task = await Task.WhenAny(_pingTcs.Task, Task.Delay(timeoutMs));
            return task == _pingTcs.Task && _pingTcs.Task.Result;
        }
        catch
        {
            return false;
        }
        finally
        {
            _client.PacketReceived -= Handler;
            _pingTcs = null;
        }
    }
    public void InitializeBusState(int maxBus = 6)
    {
        if (_client == null) return;

        for (int bus = 1; bus <= maxBus; bus++)
        {
            _client.SendAsync(new OscMessage($"/bus/{bus}/config/name"));
            _client.SendAsync(new OscMessage($"/bus/{bus}/config/color"));
        }

        // de PacketReceived handler van _client verwerkt de antwoorden en vult _busNames/_busColors
    }
    
    public async Task<List<MixerInfo>> ScanViaBroadcast()
    {
        #if ANDROID
        AcquireMulticastLock();
        #endif

        try
        {
            var foundMixers = new List<MixerInfo>();
            var broadcastAddress = IPAddress.Broadcast;
            var oscMessage = Encoding.ASCII.GetBytes("/xinfo");
    
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            udpClient.Client.ReceiveTimeout = 2000;
    
            logger.LogInformation("Broadcasting /xinfo naar 255.255.255.255:{port}", Port);
    
            await udpClient.SendAsync(oscMessage, oscMessage.Length, new IPEndPoint(broadcastAddress, Port));
    
            var timeoutMs = 2000;
            var started = DateTime.UtcNow;
            while ((DateTime.UtcNow - started).TotalMilliseconds < timeoutMs)
            {
                var remainingTime = timeoutMs - (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (remainingTime <= 0) break;

                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(remainingTime);

                var completed = await Task.WhenAny(receiveTask, timeoutTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    var ip = result.RemoteEndPoint.Address.ToString();
                    var response = Encoding.ASCII.GetString(result.Buffer);

                    if (IsMixerResponse(response) && !foundMixers.Any(m => m.IpAddress == ip))
                    {
                        foundMixers.Add(new MixerInfo(ip, response));
                        logger.LogInformation("Mixer gevonden op {ip}: {response}", ip, response);
                    }
                }
                else
                {
                    break;
                }
            }
    
            logger.LogInformation("Scan voltooid. Mixers gevonden: {count}", foundMixers.Count);
            return foundMixers;
        }
        finally
        {
            #if ANDROID
            ReleaseMulticastLock();
            #endif
        }
        
    }
    
    private static string GetLocalSubnet()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            var ipProps = ni.GetIPProperties();
            foreach (var unicast in ipProps.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    // neem eerste 3 octetten als subnet, bv. 192.168.1.
                    var bytes = unicast.Address.GetAddressBytes();
                    return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";
                }
            }
        }

        throw new InvalidOperationException("Kan geen lokaal IPv4-adres vinden");
    }
    
    private ConnectState CheckWifiMismatch(string mixerIpAddress)
    {
        if (!IPAddress.TryParse(mixerIpAddress, out var mixerIp))
            return ConnectState.WifiMismatch; // ongeldig IP → treat as mismatch

#if ANDROID || IOS
        var localSubnet = GetLocalSubnet(); // bijv. "192.168.1."
        var mixerSubnet = string.Join('.', mixerIp.GetAddressBytes().Take(3)) + ".";
        
        if (localSubnet != mixerSubnet)
            return ConnectState.WifiMismatch;
#endif
        return ConnectState.Connected; // subnet klopt
    }
    public void Connect(MixerInfo mixer)
    {
        var ip = mixer.IpAddress;
        
        // check Wi-Fi subnet
        var state = CheckWifiMismatch(ip);
        if (state == ConnectState.WifiMismatch)
        {
            OnConnectionStateChanged?.Invoke(ConnectState.WifiMismatch);
            return;
        }
        
        Storage.Set(StorageKeys.LastMixer, mixer);
        Disconnect(); // als er al een verbinding is
        _client = new UdpOscClient(ip.ToString(), Port, logger);
        
        _client.PacketReceived += (sender, packet) =>
        {
            if (packet is OscMessage msg)
            {
                if (msg.Address.EndsWith("/config/name"))
                {
                    int busIndex = ExtractBusIndex(msg.Address);
                    string name = msg[0]?.ToString() ?? string.Empty;
                    _busNames[busIndex] = name;
                    OnBusUpdated?.Invoke(busIndex, name, _busColors.GetValueOrDefault(busIndex, MixerColor.Red));
                    logger.LogInformation("Bus naam geüpdatet {busIndex}: {name}", busIndex, name);
                }
                else if (msg.Address.EndsWith("/config/color"))
                {
                    int busIndex = ExtractBusIndex(msg.Address);
                    MixerColor color = MixerColor.FromMappedValue(Convert.ToInt32(msg[0])).ValueOr(MixerColor.Red);
                    _busColors[busIndex] = color;
                    OnBusUpdated?.Invoke(busIndex, _busNames.GetValueOrDefault(busIndex, ""), color);
                }
            }
        };
        ConnectedMixerIp = ip.SomeNotNull();
        InitializeBusState(6);

    }
    private static int ExtractBusIndex(string address)
    {
        var parts = address.Split('/');
        return int.Parse(parts[2]); // /bus/{index}/config/...
    }
    public void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        ConnectedMixerIp = Option.None<string>();
    }

    public void SetBusName(int busIndex, string name)
    {
         _client?.SendAsync(new OscMessage($"/bus/{busIndex}/config/name", name));
    }

    public void SetBusColor(int busIndex, MixerColor color)
    {
        _client?.SendAsync(new OscMessage($"/bus/{busIndex}/config/color", color.MappedValue));
    }
    

    private static bool IsMixerResponse(string response)
        => response.ToLowerInvariant().Contains("xinfo");
    
}