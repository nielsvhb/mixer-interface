namespace Eggbox.Models;

public record MixerInfo(string IpAddress, string RawResponse)
{
    public string Name => ExtractParts(RawResponse)[3];
    public string Type => ExtractParts(RawResponse)[4];
    public string Firmware => ExtractParts(RawResponse)[5];

    private static string[] ExtractParts(string raw) =>
        raw.Trim().Split("\0\0");
}