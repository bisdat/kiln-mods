// SPDX-License-Identifier: MIT
namespace Bisdat.SurvivalQoL;

/// <summary>
/// Tracks the last corrected stamina value so only observed *positive* native
/// recovery earns extra points. Does not synthesize regeneration when the game
/// is not regenerating stamina. Reset on player, configuration or max changes.
/// </summary>
internal sealed class StaminaRegenScale
{
    private int? _previousAdjusted;
    private int _previousMax;
    private int _previousMultiplier;

    public int Extra(int current, int maximum, int multiplier)
    {
        if (multiplier is < 2 or > 10 || maximum <= 0
            || current < 0 || current > maximum
            || _previousAdjusted is not int previous
            || _previousMax != maximum || _previousMultiplier != multiplier
            || current <= previous)
            return 0;

        // Extra=(factor-1)*the game's observed positive gain.
        // Work in long so large attributes cannot overflow before clamping.
        long extra = (long)(current - previous) * (multiplier - 1);
        return (int)Math.Min(maximum - current, extra);
    }

    public void Sample(int corrected, int maximum, int multiplier)
    {
        _previousAdjusted = corrected;
        _previousMax = maximum;
        _previousMultiplier = multiplier;
    }

    public void Reset()
    {
        _previousAdjusted = null;
        _previousMax = 0;
        _previousMultiplier = 0;
    }
}
