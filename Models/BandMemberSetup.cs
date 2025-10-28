namespace Eggbox.Models;

public class BandMemberSetup
{
    public int BusIndex { get; set; }
    public string Name { get; set; } = "";
    public MixerColor Color { get; set; }
    public List<InstrumentSetup> Instruments { get; set; } = new();

    public BandMemberSetup(int busIndex, string name, MixerColor color)
    {
        BusIndex = busIndex;
        Name = name;
        Color = color;
    }
}