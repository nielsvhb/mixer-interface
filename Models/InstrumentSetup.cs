namespace Eggbox.Models;

public class InstrumentSetup
{
    public int ChannelIndex { get; set; }
    public string Name { get; set; } = "";
    public double Gain { get; set; }

    public InstrumentSetup(int channelIndex, string name, double gain)
    {
        ChannelIndex = channelIndex;
        Name = name;
        Gain = gain;
    }
}