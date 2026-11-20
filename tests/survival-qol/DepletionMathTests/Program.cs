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


// Stamina regen: the tracker multiplies only the observed positive recovery.
// No baseline, 1x, spending and unchanged stamina must never add points.
var stamina = new StaminaRegenScale();
Check(stamina.Extra(40, 100, 10) == 0, "no stamina boost on first sample");
stamina.Sample(40, 100, 10);
Check(stamina.Extra(40, 100, 10) == 0, "stationary stamina has no synthetic regeneration");
Check(stamina.Extra(35, 100, 10) == 0, "spending stamina cannot trigger regeneration");

for (int factor = 2; factor <= 10; factor++)
{
    stamina.Reset();
    stamina.Sample(20, 200, factor);
    Check(stamina.Extra(23, 200, factor) == 3 * (factor - 1),
        $"native stamina gain is scaled exactly {factor}x");
}

stamina.Reset();
stamina.Sample(90, 100, 10);
Check(stamina.Extra(93, 100, 10) == 7,
    "extra stamina is capped at the current maximum");

stamina.Reset();
stamina.Sample(40, 100, 10);
Check(stamina.Extra(50, 120, 10) == 0, "changed stamina maximum resets boost");
Check(stamina.Extra(50, 100, 5) == 0, "changing multiplier does not retroactively credit old gain");
stamina.Reset();
Check(stamina.Extra(50, 100, 10) == 0, "reconnect/reset cannot reuse stale stamina data");
Check(stamina.Extra(50, 100, 1) == 0, "1x is vanilla even with old sampling");
stamina.Reset();
stamina.Sample(0, int.MaxValue, 10);
Check(stamina.Extra(int.MaxValue - 100, int.MaxValue, 10) == 100,
    "large natural gains use safe arithmetic and clamp at maximum");

// Staff spells use spell ammunition. Wands are deliberately excluded.
Check(StaffChargeScale.IsSpellAmmunition("Ammo_T1_Spell_Fire_Ball_lvl10"),
    "Fireball spell ammunition is eligible for charge scaling");
Check(StaffChargeScale.IsSpellAmmunition("Ammo_T6_Spell_Ice_Bolt"),
    "Ice spell ammunition is eligible for charge scaling");
Check(!StaffChargeScale.IsSpellAmmunition("Weapon_T5_1H_Wand_01_Ice"),
    "wand attack sequences are excluded");
Check(!StaffChargeScale.IsSpellAmmunition("Ammo_Arrow_T1_Flint"),
    "non-spell ammunition is excluded");
Check(StaffChargeScale.ChargeTimeConfigId == "0x579b2657",
    "staff-spell Charge Time config uses the researched identifier");
for (int factor = 1; factor <= 10; factor++)
{
    double result = StaffChargeScale.ScaledSeconds(2.5, factor);
    Check(Math.Abs(result - (2.5 / factor)) < 0.0000001,
        $"staff charge-time multiplier {factor}x is calculated from vanilla");
}
Check(StaffChargeScale.ScaledSeconds(2.5, 1) == 2.5,
    "1x charge time is the original unmodified time");

Console.WriteLine("All stamina regeneration, depletion and charge-time arithmetic tests passed.");

