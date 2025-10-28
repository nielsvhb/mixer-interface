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
    public event Action? OnBusStateReceived;


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

            await Connect(lm);

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

        _pingTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);


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
            await _client.SendAsync(new OscMessage("/xinfo")).ConfigureAwait(false);

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
    public async Task InitializeBusState(int maxBus = 6)
    {
        if (_client == null) return;

        for (int bus = 1; bus <= maxBus; bus++)
        {
            await _client.SendAsync(new OscMessage($"/bus/{bus}/config/name")).ConfigureAwait(false);
            await _client.SendAsync(new OscMessage($"/bus/{bus}/config/color")).ConfigureAwait(false);
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
    public async Task Connect(MixerInfo mixer)
    {
        var ip = mixer.IpAddress;
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
            if (packet is not OscMessage msg) return;
            if (msg.Address.EndsWith("/config/name"))
            {
                var busIndex = ExtractBusIndex(msg.Address);
                if (busIndex <= 0) return; 
                var name = msg[0]?.ToString() ?? string.Empty;
                _busNames[busIndex] = name;
                OnBusUpdated?.Invoke(busIndex, name, _busColors.GetValueOrDefault(busIndex, MixerColor.Red));
                OnBusStateReceived?.Invoke();

                logger.LogInformation("Bus naam geüpdatet {busIndex}: {name}", busIndex, name);
            }
            else if (msg.Address.EndsWith("/config/color"))
            {
                var busIndex = ExtractBusIndex(msg.Address);
                if (busIndex <= 0) return; 
                var color = MixerColor.FromMappedValue(Convert.ToInt32(msg[0])).ValueOr(MixerColor.Red);
                _busColors[busIndex] = color;
                OnBusUpdated?.Invoke(busIndex, _busNames.GetValueOrDefault(busIndex, ""), color);
            }
        };
        ConnectedMixerIp = ip.SomeNotNull();
        await InitializeBusState(6);
    }
    private static int ExtractBusIndex(string address)
    {
        try
        {
            var parts = address.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3 || parts[0] != "bus")
                return -1;

            return int.TryParse(parts[1], out var index) ? index : -1;
        }
        catch
        {
            return -1;
        }
    }

    private void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        ConnectedMixerIp = Option.None<string>();
    }
    
    private static bool IsMixerResponse(string response)
        => response.ToLowerInvariant().Contains("xinfo");
    
    public async Task<List<InstrumentSetup>> LoadInputChannelsAsync()
    {
        try
        {
            var instruments = new Dictionary<int, InstrumentSetup>();

            void Handler(object? sender, OscPacket packet)
            {
                if (packet is not OscMessage msg) return;
                int index = ExtractChannelIndex(msg.Address);
                if (index <= 0) return;

                if (!instruments.TryGetValue(index, out var instrument))
                {
                    instrument = new InstrumentSetup(index, "", 0);
                    instruments[index] = instrument;
                }

                if (msg.Address.EndsWith("/config/name"))
                    instrument.Name = msg[0]?.ToString() ?? $"CH{index}";
                else if (msg.Address.EndsWith("/mix/fader"))
                    instrument.Gain = Convert.ToDouble(msg[0]);
            }

            _client!.PacketReceived += Handler;

            for (int ch = 1; ch <= 16; ch++)
            {
                await _client.SendAsync(new OscMessage($"/ch/{ch}/config/name"));
                await _client.SendAsync(new OscMessage($"/ch/{ch}/mix/fader"));
            }

            await Task.Delay(500);

            _client.PacketReceived -= Handler;

            return instruments.Values.OrderBy(i => i.ChannelIndex).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Unhandled error in LoadInputChannelsAsync");
            return new List<InstrumentSetup>();
        }
    }


    private static int ExtractChannelIndex(string address)
    {
        try
        {
            var parts = address.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // verwacht minimaal: "ch", "{index}", "config" of "mix"
            if (parts.Length < 2 || parts[0] != "ch")
                return -1;

            return int.TryParse(parts[1], out var index) ? index : -1;
        }
        catch
        {
            return -1;
        }
    }
    
    public async Task SaveBandSetupAsync(List<BandMemberSetup> members)
    {
        if (_client == null)
            throw new InvalidOperationException("Mixer not connected.");

        foreach (var member in members)
        {
            logger.LogInformation("💾 Saving setup for bus {BusIndex}: {Name}", member.BusIndex, member.Name);

            await _client.SendAsync(new OscMessage($"/bus/{member.BusIndex}/config/name", member.Name));
            await _client.SendAsync(new OscMessage($"/bus/{member.BusIndex}/config/color", member.Color.MappedValue));

            foreach (var instrument in member.Instruments)
            {
                var levelAddress = $"/ch/{instrument.ChannelIndex}/mix/{member.BusIndex}/level";
                await _client.SendAsync(new OscMessage(levelAddress, instrument.Gain));
                logger.LogDebug("  🎚️ {Instr} -> Bus {BusIndex} Gain={Gain}", instrument.Name, member.BusIndex, instrument.Gain);
            }
        }

        // Markeer setup als voltooid op mixer zelf (bijv. in LR-naam)
        await _client.SendAsync(new OscMessage("/lr/config/name", "EggBoxSetup:1"));
    }
    
    public async Task<bool> IsSetupCompletedAsync()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, OscPacket p)
        {
            if (p is OscMessage msg && msg.Address == "/lr/config/name")
                tcs.TrySetResult(msg[0]?.ToString() ?? "");
        }

        _client!.PacketReceived += Handler;
        await _client.SendAsync(new OscMessage("/lr/config/name"));
        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        _client.PacketReceived -= Handler;

        return false;  // response.Contains("EggBoxSetup:1");
    }
}