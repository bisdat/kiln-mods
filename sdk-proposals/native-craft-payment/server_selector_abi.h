// SPDX-License-Identifier: MIT
// Current Enshrouded server 26430253 (exe SHA256
// f0d824be7fcd48ebfe2458ff5c28da26060c2074e3ec1fcac587e5a1d508aeb2)
// original base-stock selector entry: 0x2081c0, CURRENT BUILD ONLY.
//
// Native disassembly verifies:
// RCX -> status result buffer, RDX -> 32-byte GameStockLists output,
// R8  -> primary PlayerBasesSystemServer* (may be null),
// R9  -> fallback PlayerBasesSystemServer* (may be null);
// returns the original RCX status pointer, status byte at +0,
// item/fuel arrays are written into RDX before original returns.
//
// This code is not a hook installer and is never invoked with a replacement
// provider unless the actual running executable & caller are independently
// verified. It cannot authorize a stock source by itself.
#pragma once
#include "server_stock_scope.h"
#include <cstdint>
#include <type_traits>

#if defined(_MSC_VER)
#  define CBPAY_STOCK_CALL __fastcall
#else
#  define CBPAY_STOCK_CALL
#endif

namespace cbpay::stock::native {
struct SelectorStatus {
    uint8_t error = 0;
    uint8_t pad[3]{};
    uint32_t count = 0;
};
static_assert(sizeof(SelectorStatus) == 8);
static_assert(std::is_standard_layout_v<SelectorStatus>);

// Exact Microsoft x64 by-pointer result ABI; the game owns all 4 pointers.
using OriginalSelector = SelectorStatus* (CBPAY_STOCK_CALL*)(
    SelectorStatus*, GameStockLists*, void* primary_bases,
    void* fallback_bases);

// The original returned item/fuel owner pointers stay borrowed.
// Caller must SEH-check pointers, executable hash and return address before
// marking original_return_and_buffers_probed = true. NEVER rely on a
// template name or client-supplied owner list as a substitute.
inline SelectorStatus* selector_after_original(
    OriginalSelector original,
    SelectorStatus* result, GameStockLists* lists,
    void* primary_bases, void* fallback_bases,
    uint32_t actor, void* original_recipe_tx,
    bool exact_original_selector_callsite,
    bool original_return_and_buffers_probed) noexcept
{
    if (!original) return nullptr;
    // Always invoke the game's genuine original selector first exactly once.
    // The shadow cannot cause free crafting or bypass a failed game selector.
    SelectorStatus* returned=original(result,lists,primary_bases,fallback_bases);
    if (!result || !lists || returned != result || result->error != 0 ||
        !exact_original_selector_callsite ||
        !original_return_and_buffers_probed)
        return returned;
    OriginalPayerScope* scope=OriginalPayerScope::current();
    if (scope) scope->after_original_selector(actor,original_recipe_tx,lists);
    return returned;
}
} // namespace cbpay::stock::native
