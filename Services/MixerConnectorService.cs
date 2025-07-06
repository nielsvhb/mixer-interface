using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using BuildSoft.OscCore;
using Eggbox.Models;
using Microsoft.Extensions.Logging;
using Optional;
using OscCore;
#if ANDROID
using Android.Net.Wifi;
using Android.Content;
#endif

namespace Eggbox.Services;

public sealed class MixerConnectorService(ILogger<MixerConnectorService> logger)
{
    public Option<string> ConnectedMixerIp { get; private set; }

    private OscClient? _client;
    private const int Port = 10024;

    
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
    
    public async Task<List<MixerInfo>> ScanViaBroadcast()
    {
        #if ANDROID
        AcquireMulticastLock();
        #endif

        try
        {
            var foundMixers = new List<MixerInfo>();
            var port = Port;
            var broadcastAddress = IPAddress.Broadcast;
            var oscMessage = Encoding.ASCII.GetBytes("/xinfo");
    
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            udpClient.Client.ReceiveTimeout = 2000;
    
            logger.LogInformation("Broadcasting /xinfo naar 255.255.255.255:{port}", port);
    
            await udpClient.SendAsync(oscMessage, oscMessage.Length, new IPEndPoint(broadcastAddress, port));
    
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

    public void Connect(MixerInfo mixer)
    {
        var ip = mixer.IpAddress;
        Storage.Set(StorageKeys.LastMixer, mixer);
        Disconnect(); // als er al een verbinding is
        _client = new OscClient(ip, Port);
        ConnectedMixerIp = ip.SomeNotNull();
    }

    public void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        ConnectedMixerIp = Option.None<string>();
    }

    public void SetBusName(int busIndex, string name)
        => _client?.Send($"/bus/{busIndex}/config/name", name);
    
    public void SetBusColor(int busIndex, MixerColor color)
        => _client?.Send($"/bus/{busIndex}/config/color", color.MappedValue);
    
    private async Task<string> SendAndReceiveString(string address, int timeoutMs)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = OscServer.GetOrCreate(Port);
        server.TryAddMethod(address, values =>
        {
            var s = values.ReadStringElement(0) ?? "";
            tcs.TrySetResult(s);
        });

        _client?.Send(address);
        var delay = Task.Delay(timeoutMs);
        var done = await Task.WhenAny(tcs.Task, delay);
        return done == tcs.Task ? await tcs.Task : "";
    }
    
    private static bool IsMixerResponse(string response)
        => response.ToLowerInvariant().Contains("xinfo");
    
}