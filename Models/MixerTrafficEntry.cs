using System;

namespace Eggbox.Models;

public record MixerTrafficEntry(
    DateTime Timestamp,
    bool IsTx,
    string Address,
    object[] Arguments,
    bool Handled,
    DateTime? RxTime,
    DateTime? ParseStart,
    DateTime? ParseEnd
);