using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OscCore;

namespace Eggbox.Services;

public sealed class UdpOscClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly UdpClient client;
    private readonly CancellationTokenSource cts;
    private readonly Timer _subscriptionTimer;
    private readonly IPEndPoint _remoteEndPoint;

    public event EventHandler<OscPacket>? PacketReceived;
    public event EventHandler<OscMessage>? PacketSent;

    public int LocalPort { get; }

    public UdpOscClient(string hostName, int port, ILogger logger, int? localPort = null)
    {
        _logger = logger;

        LocalPort = localPort ?? GetFreePort();
        client = new UdpClient(LocalPort);
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostName), port);
        _logger.LogInformation("UDP client bound to local endpoint {EP}", client.Client.LocalEndPoint);

        cts = new CancellationTokenSource();
        StartReceivingAsync();

        _ = SendAsync(new OscMessage("/xremote"));
        _ = SendAsync(new OscMessage("/xinfo"));
        _subscriptionTimer = new Timer(_ => 
        {
            _ = SendAsync(new OscMessage("/xremote"));
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public async Task SendAsync(OscPacket packet)
    {
        var data = packet.ToByteArray();
        var msg = packet as OscMessage;
        _logger.LogInformation("➡️ Sending OSC: {Address}", msg?.Address ?? packet.ToString());
        await client.SendAsync(data, data.Length, _remoteEndPoint).ConfigureAwait(false);
        if (msg != null)
            PacketSent?.Invoke(this, msg);
    }

    private Task StartReceivingAsync() => Task.Run(async () =>
    {
        _logger.LogInformation("📡 Start receiving on local port {Port}", LocalPort);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cts.Token);
                var buffer = result.Buffer;
                var packet = OscPacket.Read(buffer, 0, buffer.Length);
                if (packet is OscMessage msg)
                {
                    _logger.LogInformation("✅ Received OSC: {Address}", msg.Address);
                    foreach (var arg in msg)
                        _logger.LogInformation("Argument: {Arg}", arg);
                }

                try
                {
                    PacketReceived?.Invoke(this, packet);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error during PacketReceived for address {Addr}", 
                        (packet as OscMessage)?.Address);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normaal bij Dispose
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fout tijdens ontvangen van OSC");
        }
    });

    public void Dispose()
    {
        cts.Cancel();
        _subscriptionTimer?.Dispose();
        client.Dispose();
    }

    private static int GetFreePort()
    {
        //XR16 lusitert enkel naar 10025???
        return 10025;
        using var udp = new UdpClient(0);
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
