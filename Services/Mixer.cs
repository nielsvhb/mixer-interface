using System;
using System.Linq;
using System.Threading.Tasks;
using Eggbox.Models;
using OscCore;

namespace Eggbox.Services;

public class Mixer
{
    private readonly MixerModel _model;
    private readonly MixerIO _io;
    private readonly MixerParser _parser;
    private readonly MixerTrafficLogService _traffic;

    public Mixer(
        MixerModel model,
        MixerIO io,
        MixerParser parser,
        MixerTrafficLogService traffic)
    {
        _model = model;
        _io = io;
        _parser = parser;
        _traffic = traffic;

        _io.MessageSent += OnSent;
        _io.MessageReceived += OnReceived;
    }
    
    public ChannelControl Channel(int index) => new(this, _io, index);
    public BusControl Bus(int index) => new(this, _io, index);
    public FxControl Fx(int index) => new(this, _io, index);
    public MainControl Main() => new(this, _io);
    
    public abstract class MixerControlBase
    {
        protected readonly Mixer Mixer;
        protected readonly MixerIO IO;

        protected MixerControlBase(Mixer mixer, MixerIO io)
        {
            Mixer = mixer;
            IO = io;
        }
    }

    private void OnSent(OscMessage msg, DateTime t)
    {
        _traffic.AddTx(msg);
    }

    private void OnReceived(OscMessage msg, DateTime rxTime)
    {
        var handled = _parser.ApplyOscMessage(msg, out var parseStart, out var parseEnd);
        _traffic.AddRx(msg, handled, rxTime, parseStart, parseEnd);
    }
    
    private void Configure(string mixerType)
    {
        _model.Info.MixerType = mixerType.ToLowerInvariant();

        switch (_model.Info.MixerType)
        {
            case "xr18":
                _model.Info.ChannelCount = 18;
                _model.Info.BusCount = 6;
                break;
            case "xr16":
            default:
                _model.Info.ChannelCount = 16;
                _model.Info.BusCount = 4;
                break;
        }

        _model.Channels = Enumerable.Range(1, _model.Info.ChannelCount)
            .Select(i => new Channel { Index = i, Name = $"CH{i:00}" })
            .ToList();

        _model.Busses = Enumerable.Range(1, _model.Info.BusCount)
            .Select(i => new Bus { Index = i, Name = $"BUS{i:00}" })
            .ToList();
    }
    
    public async Task ConnectAsync(MixerInfo info)
    {
        await _io.ConnectAsync(info.IpAddress!);

        Configure(info.MixerType);

        _model.IsConnected = true;
        _model.IpAddress = info.IpAddress;

        await InitAsync();
    }


    private async Task InitAsync()
    {
        var chCount = _model.Info.ChannelCount;
        var busCount = _model.Info.BusCount;

        var tasks = new List<Task>();

        for (int ch = 1; ch <= chCount; ch++)
            tasks.Add(Channel(ch).RequestRefreshAsync());

        for (int bus = 1; bus <= busCount; bus++)
            tasks.Add(Bus(bus).RequestRefreshAsync());

        tasks.Add(Main().RequestRefreshAsync());

        tasks.Add(Fx(1).RequestRefreshAsync());
        tasks.Add(Fx(2).RequestRefreshAsync());

        await Task.WhenAll(tasks);
    }

    internal Task SendAsync(OscMessage msg) => _io.SendAsync(msg);
    
    public class ChannelControl : MixerControlBase
    {
        private readonly int _index;
        public ChannelControl(Mixer mixer, MixerIO io, int index) : base(mixer, io)
        {
            _index = index;
        }

        public Task SetFader(float value)
            => IO.SendAsync(new OscMessage($"/ch/{_index:D2}/mix/fader", value));

        public Task SetMute(bool muted)
            => IO.SendAsync(new OscMessage($"/ch/{_index:D2}/mix/on", muted ? 0 : 1));
        
        public Task SetColor(MixerColor color)
            => IO.SendAsync(new OscMessage($"/ch/{_index:D2}/config/color", color.MappedValue));
        
        public Task RequestRefreshAsync()
        {
            return Task.WhenAll(
                IO.SendAsync(new OscMessage($"/ch/{_index:D2}/mix/fader")),
                IO.SendAsync(new OscMessage($"/ch/{_index:D2}/mix/on")),
                IO.SendAsync(new OscMessage($"/ch/{_index:D2}/preamp/gain")),
                IO.SendAsync(new OscMessage($"/ch/{_index:D2}/config/color"))
            );
        }
    }
    
    public class BusControl : MixerControlBase
    {
        private readonly int _bus;
        public BusControl(Mixer mixer, MixerIO io, int busIndex) : base(mixer, io)
        {
            _bus = busIndex;
        }

        public Task SetFader(float value)
            => IO.SendAsync(new OscMessage($"/bus/{_bus:D2}/mix/fader", value));

        public Task RequestRefreshAsync()
        {
            return Task.WhenAll(
                IO.SendAsync(new OscMessage($"/bus/{_bus:D2}/mix/fader")),
                IO.SendAsync(new OscMessage($"/bus/{_bus:D2}/mix/on")),
                IO.SendAsync(new OscMessage($"/bus/{_bus:D2}/config/name")),
                IO.SendAsync(new OscMessage($"/bus/{_bus:D2}/config/color"))
            );
        }

        public ChannelSendControl Channel(int ch) => new(Mixer, IO, ch, _bus);
    }

    public class ChannelSendControl : MixerControlBase
    {
        private readonly int _ch;
        private readonly int _bus;

        public ChannelSendControl(Mixer mixer, MixerIO io, int ch, int bus) : base(mixer, io)
        {
            _ch = ch;
            _bus = bus;
        }

        public Task SetFader(float value)
            => IO.SendAsync(new OscMessage($"/ch/{_ch:D2}/mix/{_bus:D2}/level", value));

        public Task SetMute(bool mute)
            => IO.SendAsync(new OscMessage($"/ch/{_ch:D2}/mix/{_bus:D2}/on", mute ? 0 : 1));
    }

    public class MainControl : MixerControlBase
    {
        public MainControl(Mixer mixer, MixerIO io) : base(mixer, io) {}

        public Task SetFader(float value)
            => IO.SendAsync(new OscMessage("/lr/mix/fader", value));

        public Task RequestRefreshAsync()
        {
            return Task.WhenAll(
                IO.SendAsync(new OscMessage("/lr/mix/fader")),
                IO.SendAsync(new OscMessage("/lr/mix/on"))
            );
        }

        public ChannelControl Channel(int index)
            => new ChannelControl(Mixer, IO, index);
    }

    public class FxControl : MixerControlBase
    {
        private readonly int _index;

        public FxControl(Mixer mixer, MixerIO io, int index) : base(mixer, io)
        {
            _index = index;
        }

        public Task SetReturnFader(float value)
            => IO.SendAsync(new OscMessage($"/fxr/{_index}/mix/fader", value));

        public Task SetMute(bool mute)
            => IO.SendAsync(new OscMessage($"/fxr/{_index}/mix/on", mute ? 0 : 1));

        public Task RequestRefreshAsync()
        {
            return Task.WhenAll(
                IO.SendAsync(new OscMessage($"/fxr/{_index}/mix/fader")),
                IO.SendAsync(new OscMessage($"/fxr/{_index}/mix/on"))
            );
        }
    }
}
