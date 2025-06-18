using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using BuildSoft.OscCore;
using Microsoft.Extensions.Logging;

namespace Eggbox.Services;

public class MixerConnectorService
{
    private readonly ILogger<MixerConnectorService> _logger;
    private OscClient? _client;
    public string? ConnectedMixerIp { get; private set; }
    private const int Port = 10024;
    
    public MixerConnectorService(ILogger<MixerConnectorService> logger)
    {
        _logger = logger;
    }

    public async Task<List<MixerInfo>> ScanAsync()
    {
        var foundMixers = new List<MixerInfo>();
        var subnet = GetLocalSubnet(); // bv. "192.168.129"

        _logger.LogInformation("Start scan op subnet {subnet}.x", subnet);

        var tasks = new List<Task>();

        for (int i = 1; i < 255; i++)
        {
            var ip = $"{subnet}.{i}";
            tasks.Add(Task.Run(async () =>
            {
                var response = await SendInfoRequest(ip);
                
                if (IsMixerResponse(response ?? ""))
                {
                    lock (foundMixers)
                    {
                        foundMixers.Add(new MixerInfo
                        {
                            IPAddress = ip,
                            RawResponse = response
                        });
                        _logger.LogInformation("Mixer gevonden op {ip}: {response}", ip, response);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("Scan voltooid. Mixers gevonden: {count}", foundMixers.Count);
        return foundMixers;
    }

    private async Task<string?> SendInfoRequest(string ip)
    {
        try
        {
            string? response = null;
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 500;

            var endpoint = new IPEndPoint(IPAddress.Parse(ip), Port);
            var oscBytes = Encoding.ASCII.GetBytes("/xinfo");

            await client.SendAsync(oscBytes, oscBytes.Length, endpoint);
            var received = false;
            
            var receiveTask = client.ReceiveAsync();
            var timeoutTask = Task.Delay(500); // 500ms per IP

            var completed = await Task.WhenAny(receiveTask, timeoutTask);
            var timeout = Task.Delay(2000);
            while (!received && !timeout.IsCompleted)
            {
                await Task.Delay(50); // kleine delay om CPU niet te belasten
            }

            if (completed == receiveTask)
            {
                var result = receiveTask.Result;
                response = Encoding.ASCII.GetString(result.Buffer);
            }
            
            return response;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Fout bij verbinding met mixer: {ex.Message}");
            return null;
        }
    }
    
    public async Task<bool> TryConnectToMixer(MixerInfo mixer)
    {
        try
        {
            var response = await SendInfoRequest(mixer.IPAddress);
            var received = IsMixerResponse(response ?? "");
            
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
            Console.WriteLine($"Fout bij verbinding met mixer: {ex.Message}");
            return false;
        }
    }

    public void Connect(MixerInfo mixer)
    {
        var ip = mixer.IPAddress;
        Preferences.Set("LastMixerIP", ip);
        Preferences.Set("LastMixerResponse", mixer.RawResponse);
        Disconnect(); // als er al een verbinding is
        _client = new OscClient(ip, Port);
        ConnectedMixerIp = ip;
    }

    public void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        ConnectedMixerIp = null;
    }

    public void SetFaderLevel(string channel, float level)
    {
        _client?.Send($"/{channel}/fader", level); // bv. "/ch/01/mix/fader"
    }
    
    
    private string GetLocalIPAddress()
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

    private string GetLocalSubnet()
    {
        var localIp = GetLocalIPAddress(); // bv. 192.168.1.42
        var parts = localIp.Split('.');
        return $"{parts[0]}.{parts[1]}.{parts[2]}"; // 192.168.1
    }

    private bool IsMixerResponse(string response)
    {
        var r = response.ToLowerInvariant();
        return r.Contains("xinfo");
    }
}


public class MixerInfo
{
    public string IPAddress { get; set; } = "";
    public string RawResponse { get; set; } = "";
}