// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.AutomaticDoors;

public sealed class AutomaticDoorsConfig
{
    [Comment("Enable automatic opening and closing.")]
    public bool Enabled { get; set; } = true;
    [Comment("Open for approaching players.")]
    public bool OpenForPlayers { get; set; } = true;
    [Comment("Open for friendly villagers and crafters, not hostile enemies.")]
    public bool OpenForNpcs { get; set; } = true;
    [Range(0.5, 8.0), Comment("Horizontal opening radius, metres.")]
    public double OpenRadius { get; set; } = 2.5;
    [Range(1.0, 12.0), Comment("Clearance before closing, metres.")]
    public double CloseRadius { get; set; } = 4.0;
    [Range(1.0, 5.0), Comment("Vertical tolerance, metres, to exclude other floors.")]
    public double VerticalTolerance { get; set; } = 2.5;
    [Range(0, 10000), Comment("Delay after everyone has cleared the door, milliseconds.")]
    public int CloseDelayMs { get; set; } = 1500;
    [Range(100, 2000), Comment("Polling interval in milliseconds.")]
    public int PollIntervalMs { get; set; } = 250;
}
