using System.Net;
using System.Net.Sockets;
using OscCore;

namespace MixerInterface.Services;

public class OscService
{
    private readonly UdpClient client;
    private readonly CancellationTokenSource cts;
    public event EventHandler<OscPacket> PacketReceived;

    
    public OscService(string hostName, int port = 10024)
    {
        client = new UdpClient();
        client.Connect(hostName, port);
        cts = new CancellationTokenSource();
        StartReceiving();
    }
    
    public async Task SendAsync(OscPacket packet)
    {
        var data = packet.ToByteArray();
        await client.SendAsync(data, data.Length).ConfigureAwait(false);
    }

    public async Task SendAsync(OscPacket packet, IPEndPoint endPoint)
    {
        var data = packet.ToByteArray();
        await client.SendAsync(data, data.Length, endPoint).ConfigureAwait(false);
    }
    
    private async void StartReceiving()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync();
                var buffer = result.Buffer;
                var packet = OscPacket.Read(buffer, 0, buffer.Length);
                // TODO: Maybe do this asynchronously...
                PacketReceived?.Invoke(this, packet);
            }
        }
        catch (Exception)
        {
            // If the exception is caused by the client being disposed
            // after the cancellation token being cancelled, that's fine.
            if (cts.IsCancellationRequested)
            {
                return;
            }
            // TODO: Log the exception.
            // It's probably because the mixer is unavailable. The caller
            // is expected to notice this, dispose and reconnect.
        }
    }
    
    public void Dispose()
    {
        cts.Cancel();
        try
        {
            client.Dispose();
        }
        catch
        {
            // If disposing the UdpClient fails, there's really nothing we can
            // we about it, and it shouldn't do any further harm. Just swallow the error.
        }
    }
}