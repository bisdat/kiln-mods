// SPDX-License-Identifier: MIT
// Read-only recognition of loaded storage prop templates.
// The game's native crafting entitlement is not inferred here.
namespace CrossBaseStorage;

internal static class StorageTemplate
{
    // Template suffix, NOT a particular tier or storage capacity.
    // Examples: Prop_Decoration_T6_Storage_72_Magic,
    // Prop_Decoration_T5_Storage_40_Magic.
    public static bool IsMagic(string? template) =>
        template?.EndsWith("_Magic", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsStorageCandidate(string? template, int actualSlots) =>
        actualSlots > 0 &&
        template is not null &&
        template.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase) &&
        (IsMagic(template) ||
         template.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
         template.Contains("Chest", StringComparison.OrdinalIgnoreCase));
}
