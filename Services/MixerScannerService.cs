using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

public class MixerScannerService
{
    private readonly ILogger<MixerScannerService> _logger;

    public MixerScannerService(ILogger<MixerScannerService> logger)
    {
        _logger = logger;
    }

    public async Task<List<MixerInfo>> ScanAsync()
    {
        var foundMixers = new List<MixerInfo>();
        var subnet = GetLocalSubnet(); // bv. "192.168.129"
        var port = 10024;

        _logger.LogInformation("Start scan op subnet {subnet}.x", subnet);

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
                                    IPAddress = ip,
                                    RawResponse = response
                                });
                                _logger.LogInformation("Mixer gevonden op {ip}: {response}", ip, response);
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

        _logger.LogInformation("Scan voltooid. Mixers gevonden: {count}", foundMixers.Count);
        return foundMixers;
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
