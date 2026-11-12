// SPDX-License-Identifier: MIT
using Kiln;

namespace Bisdat.DebugTools;

/// <summary>
/// The scope of each debug-building session is one administrator's character.
/// These options tune that session; they grant nothing until /debugbuild on.
/// </summary>
public sealed class DebugToolsConfig
{
    [Comment("When debug building is enabled, bypass the Shroud placement restriction.")]
    public bool BuildInShroud { get; set; } = true;

    [Comment("When debug building is enabled, bypass no-build zones at dungeons and points of interest. Other altar-specific limits still apply.")]
    public bool BuildInDungeons { get; set; } = true;

    [Comment("When debug building is enabled, allow placement even with enemies nearby.")]
    public bool IgnoreEnemiesNearby { get; set; } = true;

    [Comment("Keep placements outside Flame Altar bases in Kiln.Build's saved regions. Does not guarantee altar-specific persistence.")]
    public bool KeepBuildsOutsideBases { get; set; } = true;

    [Comment("Optional: waive material cost for blocks, terrain and placeable props; dismantling refunds nothing. Default off.")]
    public bool FreeBuilding { get; set; } = false;
}
