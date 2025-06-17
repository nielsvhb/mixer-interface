using System.Net;
using System.Net.Sockets;
using System.Text;
using BuildSoft.OscCore;

namespace Eggbox.Services;

public class MixerConnectorService
{
    private OscClient? _client;
    public string? ConnectedMixerIp { get; private set; }
    
    public async Task<bool> TryConnectToMixer(MixerInfo mixer)
    {
        var port = 10024;
        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 500;

            var endpoint = new IPEndPoint(IPAddress.Parse(mixer.IPAddress), port);
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
                var response = Encoding.ASCII.GetString(result.Buffer);
                received = response.Contains("xinfo");
                Connect(mixer);
            }
            
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
        _client = new OscClient(ip, 10024);
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
}