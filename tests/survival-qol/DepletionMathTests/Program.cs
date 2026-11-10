using Bisdat.SurvivalQoL;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}

// 1x must never alter vanilla depletion, even if some remainder exists.
Check(DepletionMath.Refund(9, 1, 3) == (0, 0), "1x leaves vanilla depletion unchanged");

// The fractional carries guarantee an *actual* x-fold time increase over
// repeated one-point spends; do not round each tick independently.
for (int factor = 2; factor <= 10; factor++)
{
    int current = 200;
    int remainder = 0;
    for (int tick = 0; tick < 100; tick++)
    {
        int spent = 1;
        var (refund, rest) = DepletionMath.Refund(spent, factor, remainder);
        current -= spent - refund;
        remainder = rest;
    }
    int effectiveSpent = 200 - current;
    Check(effectiveSpent == 100 / factor + (100 % factor == 0 ? 0 : 1),
        $"{factor}x prolongs 100 ticks of one-point depletion");
}

var (burstRefund, burstRemainder) = DepletionMath.Refund(25, 10, 0);
Check(burstRefund == 22 && burstRemainder == 5, "10x burst depletion is proportionally scaled");
var (followUp, followRemainder) = DepletionMath.Refund(1, 10, burstRemainder);
Check(followUp == 1 && followRemainder == 4, "fractional remainder survives uneven spends");

var (largeRefund, largeRemainder) = DepletionMath.Refund(int.MaxValue, 10, 9);
Check(largeRefund > 0 && largeRefund < int.MaxValue && largeRemainder is >= 0 and < 10,
    "large attributes cannot overflow intermediate arithmetic");

// Each player/attribute has its own remainder in the mod. Simulate the
// oxygen and heat tracks receiving different losses during the same tick.
int oxygenRemainder = 0, heatRemainder = 0;
(_, oxygenRemainder) = DepletionMath.Refund(1, 10, oxygenRemainder);
(_, heatRemainder) = DepletionMath.Refund(7, 3, heatRemainder);
Check(oxygenRemainder == 9 && heatRemainder == 2, "independent fractional accrual");

Console.WriteLine("All DepletionMath tests passed.");
