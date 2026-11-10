// SPDX-License-Identifier: MIT
namespace Bisdat.SurvivalQoL;

/// <summary>
/// Calculate a fractional refund for resource depletion without floating-point
/// drift. Example: at 10x, restore 9 of every 10 points consumed (over time).
/// Each tracked resource/player holds its own remainder between server ticks.
/// </summary>
internal static class DepletionMath
{
    public static (int Refund, int Remainder) Refund(int lost, int multiplier, int previousRemainder)
    {
        if (lost <= 0 || multiplier <= 1)
            return (0, 0);

        multiplier = Math.Clamp(multiplier, 1, 10);
        // 64-bit intermediate: an attribute can legitimately contain large values.
        long numerator = (long)lost * (multiplier - 1)
            + Math.Clamp(previousRemainder, 0, multiplier - 1);
        return ((int)(numerator / multiplier), (int)(numerator % multiplier));
    }
}
