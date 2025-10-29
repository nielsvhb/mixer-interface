using OscCore;
using Eggbox.Models;
using System.Collections.Concurrent;

namespace Eggbox.Services;

/// <summary>
/// Centrale OOP-API bovenop OSC, zonder directe afhankelijkheid van MixerConnectorService.
/// </summary>
public sealed class MixerCommandService
{
    private readonly IMixerTransport _transport;
    private readonly MixerStateCacheService _cache;

    public MixerCommandService(IMixerTransport transport, MixerStateCacheService cache)
    {
        _transport = transport;
        _cache = cache;
    }

    // Hiërarchie
    public MainMixProxy Main() => new(_transport, _cache);
    public MixProxy Mix(int index) => new(_transport, _cache, index);
    public BusProxy Bus(int index) => new(_transport, _cache, index);

    // Init alle busnamen/kleuren opvragen
    public async Task InitializeBussesAsync(int maxBus = 6)
    {
        var tasks = new List<Task>();
        for (int i = 1; i <= maxBus; i++)
        {
            tasks.Add(_transport.SendAsync(new OscMessage($"/bus/{i:D2}/config/name")));
            tasks.Add(_transport.SendAsync(new OscMessage($"/bus/{i:D2}/config/color")));
        }
        await Task.WhenAll(tasks);
    }

    // -------- PROXIES --------

    public sealed class MainMixProxy
    {
        private readonly IMixerTransport _t;
        private readonly MixerStateCacheService _cache;

        public MainMixProxy(IMixerTransport t, MixerStateCacheService cache)
        {
            _t = t; _cache = cache;
        }

        public ChannelProxy Channel(int ch) => new(_t, _cache, ch, isMain: true);
    }

    public sealed class MixProxy
    {
        private readonly IMixerTransport _t;
        private readonly MixerStateCacheService _cache;
        public int Index { get; }

        public MixProxy(IMixerTransport t, MixerStateCacheService cache, int index)
        {
            _t = t; _cache = cache; Index = index;
        }

        public ChannelProxy Channel(int ch) => new(_t, _cache, ch, mixIndex: Index);
    }

    public sealed class BusProxy
    {
        private readonly IMixerTransport _t;
        private readonly MixerStateCacheService _cache;
        public int Index { get; }

        public BusProxy(IMixerTransport t, MixerStateCacheService cache, int index)
        {
            _t = t; _cache = cache; Index = index;
        }

        private string Addr(string path) => $"/bus/{Index:D2}/{path}";

        public Task SetName(string name)
            => _t.SendAsync(new OscMessage(Addr("config/name"), name));

        public Task SetColor(MixerColor color)
            => _t.SendAsync(new OscMessage(Addr("config/color"), color.MappedValue));

        public Task RequestName()  => _t.SendAsync(new OscMessage(Addr("config/name")));
        public Task RequestColor() => _t.SendAsync(new OscMessage(Addr("config/color")));
    }

    public sealed class ChannelProxy
    {
        private readonly IMixerTransport _t;
        private readonly MixerStateCacheService _cache;
        public int ChannelIndex { get; }
        public int? MixIndex { get; }
        public bool IsMain { get; }

        public ChannelProxy(IMixerTransport t, MixerStateCacheService cache,
            int channelIndex, bool isMain = false, int? mixIndex = null)
        {
            _t = t; _cache = cache;
            ChannelIndex = channelIndex;
            IsMain = isMain;
            MixIndex = mixIndex;
        }

        private string Path(string suffix)
        {
            var ch = ChannelIndex.ToString("D2");
            return IsMain
                ? $"/ch/{ch}/mix/{suffix}"            // /ch/01/mix/fader, /ch/01/mix/on
                : $"/ch/{ch}/mix/{MixIndex:D2}/{suffix}"; // /ch/01/mix/01/level, /ch/01/mix/01/on
        }

        // --- Set ---
        public Task SetFader(float value)
            => _t.SendAsync(new OscMessage(Path(IsMain ? "fader" : "level"), value));

        public Task SetMute(bool muted = true)
            => _t.SendAsync(new OscMessage(Path("on"), muted ? 0f : 1f));

        // --- Get ---
        public async Task<float> GetFaderAsync(int timeoutMs = 800)
        {
            var address = Path(IsMain ? "fader" : "level");
            var tcs = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, OscPacket p)
            {
                if (p is OscMessage m && m.Address == address)
                    tcs.TrySetResult(Convert.ToSingle(m[0]));
            }

            _t.MessageReceived += Handler;
            await _t.SendAsync(new OscMessage(address));

            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) == tcs.Task)
            {
                _t.MessageReceived -= Handler;
                return tcs.Task.Result;
            }

            _t.MessageReceived -= Handler;
            return _cache.TryGetFader(address) ?? 0f;
        }

        public async Task<bool> GetMuteAsync(int timeoutMs = 800)
        {
            var address = Path("on");
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, OscPacket p)
            {
                if (p is OscMessage m && m.Address == address)
                    tcs.TrySetResult(Convert.ToSingle(m[0]) < 0.5f);
            }

            _t.MessageReceived += Handler;
            await _t.SendAsync(new OscMessage(address));

            if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) == tcs.Task)
            {
                _t.MessageReceived -= Handler;
                return tcs.Task.Result;
            }

            _t.MessageReceived -= Handler;
            return _cache.TryGetMute(address) ?? false;
        }

        public Task RequestRefresh()
        {
            return Task.WhenAll(
                _t.SendAsync(new OscMessage(Path(IsMain ? "fader" : "level"))),
                _t.SendAsync(new OscMessage(Path("on")))
            );
        }
    }
}
