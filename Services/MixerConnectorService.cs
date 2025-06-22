using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using BuildSoft.OscCore;
using Eggbox.Models;
using Microsoft.Extensions.Logging;
using Optional;

namespace Eggbox.Services;

public sealed class MixerConnectorService(ILogger<MixerConnectorService> logger)
{
    public Option<string> ConnectedMixerIp { get; private set; }

    private OscClient? _client;
    private const int Port = 10024;

    public async Task<List<MixerInfo>> ScanAsync()
    {
        var foundMixers = new List<MixerInfo>();
        var subnet = GetLocalSubnet(); // bv. "192.168.129"
        var port = 10024;

        logger.LogInformation("Start scan op subnet {subnet}.x", subnet);

        var tasks = new List<Task>();

        for (int i = 1; i < 255; i++)
        {
            var ip = $"{subnet}.{i}";
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var client = new UdpClient();
                    client.Client.ReceiveTimeout = 500;

                    var endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
                    var oscBytes = Encoding.ASCII.GetBytes("/xinfo");

                    await client.SendAsync(oscBytes, oscBytes.Length, endpoint);

                    // Luister op zelfde client
                    var receiveTask = client.ReceiveAsync();
                    var timeoutTask = Task.Delay(500); // 500ms per IP

                    var completed = await Task.WhenAny(receiveTask, timeoutTask);
                    if (completed == receiveTask)
                    {
                        var result = receiveTask.Result;
                        var response = Encoding.ASCII.GetString(result.Buffer);

                        if (IsMixerResponse(response))
                        {
                            lock (foundMixers)
                            {
                                foundMixers.Add(new MixerInfo
                                {
                                    IpAddress = ip,
                                    RawResponse = response
                                });
                                logger.LogInformation("Mixer gevonden op {ip}: {response}", ip, response);
                            }
                        }
                    }
                }
                catch
                {
                    // stilletjes negeren – geen mixer op dit IP
                }
            }));
        }

        await Task.WhenAll(tasks);

        logger.LogInformation("Scan voltooid. Mixers gevonden: {count}", foundMixers.Count);
        return foundMixers;
    }
    
    public async Task<bool> TryConnectToMixer(MixerInfo mixer)
    {
        try
        {
            var response = await SendInfoRequest(mixer.IpAddress);
            var received = response.Map(IsMixerResponse).ValueOr(false);
            
            if(received)
                Connect(mixer);
            
            return received;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogInformation($"Fout bij verbinding met mixer: {ex.Message}");
            return false;
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

    public void SetFaderLevel(string channel, float level)
    {
        _client?.Send($"/{channel}/fader", level); // bv. "/ch/01/mix/fader"
    }
    

    private static async Task<Option<string>> SendInfoRequest(string ip)
    {
        using var client = new UdpClient();
        client.Client.ReceiveTimeout = 500;

        var endpoint = new IPEndPoint(IPAddress.Parse(ip), Port);
        var oscBytes = Encoding.ASCII.GetBytes("/xinfo");

        await client.SendAsync(oscBytes, oscBytes.Length, endpoint);
        
        var receiveTask = client.ReceiveAsync();

        var completed = await Task.WhenAny(receiveTask, Task.Delay(500));
        while (!Task.Delay(2000).IsCompleted)
        {
            await Task.Delay(50); // kleine delay om CPU niet te belasten
        }

        return completed == receiveTask 
            ? Encoding.ASCII.GetString(receiveTask.Result.Buffer).SomeNotNull() 
            : Option.None<string>();
    }
    
    private static string GetLocalIpAddress()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            var ipProps = ni.GetIPProperties();
            foreach (var addr in ipProps.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address))
                {
                    return addr.Address.ToString();
                }
            }
        }

        throw new Exception("Geen geldig IP-adres gevonden.");
    }

    private static string GetLocalSubnet()
    {
        var localIp = GetLocalIpAddress(); // bv. 192.168.1.42
        var parts = localIp.Split('.');
        return $"{parts[0]}.{parts[1]}.{parts[2]}"; // 192.168.1
    }

    private static bool IsMixerResponse(string response)
    {
        var r = response.ToLowerInvariant();
        return r.Contains("xinfo");
    }
}