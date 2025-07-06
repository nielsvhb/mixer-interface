namespace Eggbox.Models;

public class BusConfig
{
    public int Index { get; init; }
    public string Name { get; set; } = "";
    public MixerColor Color { get; set; } = MixerColor.Red;
}