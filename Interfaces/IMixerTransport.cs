using System.Threading.Tasks;
using OscCore;

namespace Eggbox.Services;

public interface IMixerTransport
{
    Task SendAsync(OscMessage message);
    event Action<object?, OscPacket>? MessageReceived;

}