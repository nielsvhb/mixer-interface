// Services/MixerIO.cs
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OscCore;

namespace Eggbox.Services;

public sealed class MixerIO : IDisposable
{
    private readonly ILogger<MixerIO> _logger;
    private UdpClient? _client;
    private MixerParser _parser;
    private MixerTrafficLogService _traffic;
    private CancellationTokenSource? _cts;
    private Timer? _subscriptionTimer;
    private IPEndPoint? _remoteEndPoint;

    public event Action<OscMessage, DateTime>? MessageReceived;
    public event Action<OscMessage, DateTime>? MessageSent;

    public int LocalPort { get; private set; }

    private const int DefaultLocalPort = 10025; // zoals je oude GetFreePort()

    public MixerIO(ILogger<MixerIO> logger, MixerParser parser, MixerTrafficLogService traffic)
    {
        _logger = logger;
        _parser = parser;
        _traffic = traffic;
    }

    public async Task ConnectAsync(string hostName, int port = 10024, int? localPort = null)
    {
        // als er al een client bestond, eerst netjes opruimen
        await DisconnectAsync();

        LocalPort = localPort ?? DefaultLocalPort;
        _client = new UdpClient(LocalPort);
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostName), port);

        _logger.LogInformation("UDP client bound to local endpoint {EP}", _client.Client.LocalEndPoint);

        _cts = new CancellationTokenSource();
        _ = StartReceivingAsync(_cts.Token);

        // init /xremote + /xinfo + keepalive zoals je UdpOscClient
        _ = SendAsync(new OscMessage("/xremote"));
        _ = SendAsync(new OscMessage("/xinfo"));

        _subscriptionTimer = new Timer(_ =>
        {
            _ = SendAsync(new OscMessage("/xremote"));
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        await Task.CompletedTask;
    }

    public async Task SendAsync(OscMessage msg)
    {
        if (_client == null || _remoteEndPoint == null)
            throw new InvalidOperationException("MixerIO is not connected.");

        var packet = (OscPacket)msg;
        var data = packet.ToByteArray();
        var sentAt = DateTime.UtcNow;

        _logger.LogInformation("➡️ Sending OSC: {Address}", msg.Address);
        await _client.SendAsync(data, data.Length, _remoteEndPoint).ConfigureAwait(false);

        MessageSent?.Invoke(msg, sentAt);
    }

    private Task StartReceivingAsync(CancellationToken token) => Task.Run(async () =>
    {
        if (_client == null) return;

        _logger.LogInformation("📡 Start receiving on local port {Port}", LocalPort);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _client.ReceiveAsync(token).ConfigureAwait(false);
                var buffer = result.Buffer;
                var packet = OscPacket.Read(buffer, 0, buffer.Length);
                var rxTime = DateTime.UtcNow;

                if (packet is OscMessage msg)
                {
                    _logger.LogInformation("✅ Received OSC: {Address}", msg.Address);
                    MessageReceived?.Invoke(msg, rxTime);
                    bool handled = _parser.ApplyOscMessage(msg,
                        out var parseStart,
                        out var parseEnd);
                    _traffic.AddRx(msg, handled, rxTime, parseStart, parseEnd);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normaal bij disconnect
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fout tijdens ontvangen van OSC");
        }
    });

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        _cts = null;

        _subscriptionTimer?.Dispose();
        _subscriptionTimer = null;

        _client?.Dispose();
        _client = null;

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
