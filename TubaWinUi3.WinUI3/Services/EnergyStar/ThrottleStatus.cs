// Throttle status enum.
// Ported from EnergyStarX (https://github.com/JasonWei512/EnergyStarX)
// Copyright 2022 Bingxing Wang — MIT licensed (see Services/EnergyStar/LICENSE.txt).

namespace TubaWinUi3.Services;

public enum ThrottleStatus
{
    /// <summary>Throttling paused by user, or service not initialized.</summary>
    Stopped = 0,

    /// <summary>Plugged in and <see cref="EnergyStarService.ThrottleWhenPluggedIn"/> is disabled.</summary>
    OnlyBlacklist = 1,

    /// <summary>On battery, or plugged in with throttling enabled.</summary>
    BlacklistAndAllButWhitelist = 2
}
