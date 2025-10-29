using System.Collections.Concurrent;
using OscCore;
using Eggbox.Models;

namespace Eggbox.Services;

public sealed class MixerStateCacheService
{
    private readonly ConcurrentDictionary<string, float> _faders = new();
    private readonly ConcurrentDictionary<string, bool> _mutes = new();
    private readonly ConcurrentDictionary<int, string> _busNames = new();
    private readonly ConcurrentDictionary<int, MixerColor> _busColors = new();

    public void UpdateFromMessage(OscMessage msg)
    {
        var addr = msg.Address;

        // Faders: main (/ch/01/mix/fader) of mix-level (/ch/01/mix/01/level)
        if (addr.EndsWith("/mix/fader") || addr.Contains("/mix/") && addr.EndsWith("/level"))
        {
            if (msg.Count > 0)
                _faders[addr] = Convert.ToSingle(msg[0]);
            return;
        }

        // Mutes: /ch/01/mix/on of /ch/01/mix/01/on
        if (addr.EndsWith("/mix/on") || (addr.Contains("/mix/") && addr.EndsWith("/on")))
        {
            if (msg.Count > 0)
                _mutes[addr] = Convert.ToSingle(msg[0]) < 0.5f;
            return;
        }

        // Bus naam/ kleur
        if (addr.Contains("/bus/") && addr.EndsWith("/config/name"))
        {
            int i = ExtractBusIndex(addr);
            if (i > 0) _busNames[i] = msg[0]?.ToString() ?? "";
            return;
        }
        if (addr.Contains("/bus/") && addr.EndsWith("/config/color"))
        {
            int i = ExtractBusIndex(addr);
            if (i > 0) _busColors[i] = MixerColor.FromMappedValue(Convert.ToInt32(msg[0])).ValueOr(MixerColor.Red);
            return;
        }
    }

    public float? TryGetFader(string address) => _faders.TryGetValue(address, out var v) ? v : null;
    public bool? TryGetMute(string address) => _mutes.TryGetValue(address, out var v) ? v : null;

    public IReadOnlyDictionary<int, string> BusNames => _busNames;
    public IReadOnlyDictionary<int, MixerColor> BusColors => _busColors;

    private static int ExtractBusIndex(string addr)
    {
        try
        {
            var parts = addr.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && int.TryParse(parts[1], out var i) ? i : -1;
        }
        catch { return -1; }
    }
}
