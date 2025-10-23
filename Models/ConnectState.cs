namespace Eggbox.Models;

public enum ConnectState
{
    Idle,
    Connecting,
    Connected,
    ScanRequired,
    MixersFound,
    NoMixerFound,
    WifiMismatch,
    ManualEntry
}
