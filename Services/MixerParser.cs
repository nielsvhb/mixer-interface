using System;
using System.Linq;
using Eggbox.Models;
using OscCore;

namespace Eggbox.Services;

public class MixerParser
{
    private readonly MixerModel _model;

    public MixerParser(MixerModel model)
    {
        _model = model;
    }

    public bool ApplyOscMessage(OscMessage msg, out DateTime parseStart, out DateTime parseEnd)
    {
        parseStart = DateTime.UtcNow;
        bool handled = false;

        string addr = msg.Address;

        if (addr.StartsWith("/ch/"))
        {
            handled = HandleChannel(addr, msg);
        }
        else if (addr.StartsWith("/bus/"))
        {
            handled = HandleBus(addr, msg);
        }
        else if (addr.StartsWith("/fxr/"))
        {
            handled = HandleFx(addr, msg);
        }
        else if (addr.StartsWith("/main/"))
        {
            handled = HandleMain(addr, msg);
        }

        parseEnd = DateTime.UtcNow;
        return handled;
    }

    private static object[] GetArgs(OscMessage msg)
        => msg.Select(a => (object)a).ToArray();

    private bool HandleChannel(string addr, OscMessage msg)
    {
        var parts = addr.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[1], out int chIndex)) return false;

        var ch = _model.Channels.FirstOrDefault(c => c.Index == chIndex);
        if (ch == null) return false;

        var args = GetArgs(msg);
        if (args.Length == 0) return false;

        // /ch/01/mix/fader
        if (parts.Length >= 4 && parts[2] == "mix" && parts[3] == "fader")
        {
            ch.Fader = Convert.ToSingle(args[0]);
            _model.RaiseStateChanged(addr);
            return true;
        }

        // /ch/01/mix/on  (mute)
        if (parts.Length >= 5 && parts[2] == "mix" &&
            int.TryParse(parts[3], out var busIndex2) &&
            parts[4] == "on")
        {
            if (!ch.Sends.TryGetValue(busIndex2, out var send))
            {
                send = new ChannelSend();
                ch.Sends[busIndex2] = send;
            }

            send.Mute = Convert.ToInt32(args[0]) == 0;

            _model.RaiseStateChanged(addr);
            return true;
        }

        // /ch/01/preamp/gain
        if (parts.Length >= 4 && parts[2] == "preamp" && parts[3] == "gain")
        {
            ch.Gain = Convert.ToSingle(args[0]);
            _model.RaiseStateChanged(addr);
            return true;
        }

        // /ch/01/mix/01/level (bus send)
        if (parts.Length >= 5 && parts[2] == "mix" && 
            int.TryParse(parts[3], out var busIndex) &&
            parts[4] == "level")
        {
            if (!ch.Sends.TryGetValue(busIndex, out var send))
            {
                send = new ChannelSend();
                ch.Sends[busIndex] = send;
            }

            send.Level = Convert.ToSingle(args[0]);

            _model.RaiseStateChanged(addr);
            return true;
        }

        return false;
    }

    private bool HandleBus(string addr, OscMessage msg)
    {
        var parts = addr.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[1], out int busIndex)) return false;

        var bus = _model.Busses.FirstOrDefault(b => b.Index == busIndex);
        if (bus == null) return false;

        var args = GetArgs(msg);
        if (args.Length == 0) return false;

        if (parts.Length >= 4 && parts[2] == "mix" && parts[3] == "fader")
        {
            bus.Fader = Convert.ToSingle(args[0]);
            _model.RaiseStateChanged(addr);
            return true;
        }

        if (parts.Length >= 4 && parts[2] == "mix" && parts[3] == "on")
        {
            var val = Convert.ToInt32(args[0]);
            bus.Mute = val == 0;
            _model.RaiseStateChanged(addr);
            return true;
        }

        return false;
    }

    private bool HandleFx(string addr, OscMessage msg)
    {
        // /fxr/1/mix/fader
        var parts = addr.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[1], out int fxIndex)) return false;

        var fx = fxIndex == 1 ? _model.Fx1 : _model.Fx2;
        var args = GetArgs(msg);
        if (args.Length == 0) return false;

        if (parts.Length >= 4 && parts[2] == "mix" && parts[3] == "fader")
        {
            fx.Fader = Convert.ToSingle(args[0]);
            _model.RaiseStateChanged(addr);
            return true;
        }

        if (parts.Length >= 4 && parts[2] == "mix" && parts[3] == "on")
        {
            var val = Convert.ToInt32(args[0]);
            fx.Mute = val == 0;
            _model.RaiseStateChanged(addr);
            return true;
        }

        return false;
    }

    private bool HandleMain(string addr, OscMessage msg)
    {
        var args = GetArgs(msg);
        if (args.Length == 0) return false;

        if (addr.EndsWith("/mix/fader", StringComparison.Ordinal))
        {
            _model.Main.Fader = Convert.ToSingle(args[0]);
            _model.RaiseStateChanged(addr);
            return true;
        }

        if (addr.EndsWith("/mix/on", StringComparison.Ordinal))
        {
            var val = Convert.ToInt32(args[0]);
            _model.Main.Mute = val == 0;
            _model.RaiseStateChanged(addr);
            return true;
        }

        return false;
    }
}
