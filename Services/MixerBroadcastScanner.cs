using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Eggbox.Models;
using Microsoft.Extensions.Logging;

namespace Eggbox.Services;

public class MixerBroadcastScanner
{
    private readonly ILogger<MixerBroadcastScanner> _logger;
    private const int Port = 10024;

    public MixerBroadcastScanner(ILogger<MixerBroadcastScanner> logger)
    {
        _logger = logger;
    }

    public async Task<List<MixerInfo>> ScanAsync(int timeoutMs = 2000)
    {
        var mixers = new List<MixerInfo>();

        using var udp = new UdpClient { EnableBroadcast = true };
        udp.Client.ReceiveTimeout = timeoutMs;

        var broadcast = IPAddress.Broadcast;
        var data = Encoding.ASCII.GetBytes("/xinfo");

        _logger.LogInformation("Broadcasting /xinfo → UDP port {Port}", Port);

        await udp.SendAsync(data, data.Length, new IPEndPoint(broadcast, Port));

        var start = DateTime.UtcNow;

        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            var remaining = timeoutMs - (int)(DateTime.UtcNow - start).TotalMilliseconds;
            if (remaining <= 0) break;

            try
            {
                var receiveTask = udp.ReceiveAsync();
                var timeout = Task.Delay(remaining);

                var completed = await Task.WhenAny(receiveTask, timeout);
                if (completed != receiveTask) break;

                var packet = await receiveTask;
                var ip = packet.RemoteEndPoint.Address.ToString();
                var text = Encoding.ASCII.GetString(packet.Buffer);

                if (!text.Contains("xinfo", StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = ParseMixerInfo(ip, text);

                // geen duplicates
                if (!mixers.Any(x => x.IpAddress == ip))
                    mixers.Add(info);
            }
            catch
            {
                // ignore individual timeouts
            }
        }

        _logger.LogInformation("Scan complete: {Count} mixers found", mixers.Count);
        return mixers;
    }

    private MixerInfo ParseMixerInfo(string ip, string response)
    {
        var info = new MixerInfo
        {
            IpAddress = ip,
            MixerType = "xr16", // default fallback
        };

        // /xinfo string is zoals:
        // "X-Air XR16 1.15 16 4"
        // of soms in varianten

        var parts = response
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Safety check
        if (parts.Length < 2)
            return info;

        // example:
        // X-Air XR16 1.15 16 4

        if (parts.Length >= 2)
            info.Name = parts[0] + " " + parts[1];

        if (parts.Length >= 3)
            info.MixerType = parts[1]; // bij XR16/18 meestal in part[1]

        if (parts.Length >= 4)
            info.FirmwareVersion = parts[2];

        if (parts.Length >= 5 && int.TryParse(parts[3], out var ch))
            info.ChannelCount = ch;

        if (parts.Length >= 6 && int.TryParse(parts[4], out var busses))
            info.BusCount = busses;

        return info;
    }
}
