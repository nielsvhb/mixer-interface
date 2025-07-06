namespace Eggbox.Models;

public class MixerColor : IntMappedEnumeration<MixerColor>
{
    public readonly string HexCode;
    private MixerColor(string name, string value, int index, string hexCode) : base(name, index)
    {
        HexCode = hexCode;
    }

    public static readonly MixerColor Red = new MixerColor(nameof(Red), "RD", 1, "#FF0000");
    public static readonly MixerColor Green = new MixerColor(nameof(Green), "GN", 2, "#00FF00");
    public static readonly MixerColor Yellow = new MixerColor(nameof(Yellow), "YE", 3, "#FFFF00");
    public static readonly MixerColor Blue = new MixerColor(nameof(Blue), "BL", 4, "#0000FF");
    public static readonly MixerColor Magenta = new MixerColor(nameof(Magenta), "MG", 5, "#FF00FF");
    public static readonly MixerColor Cyan = new MixerColor(nameof(Cyan), "CY", 6, "#00FFFF");
}