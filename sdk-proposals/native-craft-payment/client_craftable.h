// SPDX-License-Identifier: MIT
// Native CLIENT craftability policy for ordinary remote Magic Chest payment.
// This is NOT a source scanner, a server payment provider, or a way to waive
// costs. The untrusted game client can only show a button; the server still
// has to validate the whole selected bill and debit it in RecipeApply.
#pragma once

#include <algorithm>
#include <cstdint>

namespace cbpay::client {

struct Call {
    uint32_t recipe = 0;
    uint32_t cap = 0;
    const void* selections = nullptr; // borrowed: do NOT retain
    uint32_t vanilla_result = 0;
};

struct ServerCapability {
    uint32_t recipe = 0;
    uint32_t approved_count = 0;
    // This authorization may only be constructed from the owning server's
    // current session, NOT from a local mod's chest-template/read-only scan.
    bool live_authenticated_server = false;
    bool matching_native_protocol = false;
    bool original_transaction_payment_active = false;
    bool server_has_complete_selected_bill = false;
    bool exact_selection_bound = false;
    bool authoritative_magic_entitlement = false;
    bool authority_epoch_current = false;
    bool no_conflicting_free_craft = false;
};

using NativeQuery = bool (*)(const Call&, ServerCapability*);

inline uint32_t craftable(Call call, NativeQuery native_server_capability) {
    // Preserve a valid vanilla answer, including its count cache. The
    // original function is always invoked BEFORE this helper.
    const uint32_t cap = call.cap & 0xffffu;
    if (!cap || !call.recipe || call.vanilla_result >= cap ||
        !native_server_capability)
        return call.vanilla_result;

    ServerCapability c{};
    if (!native_server_capability(call, &c) ||
        c.recipe != call.recipe || !c.approved_count ||
        c.approved_count > cap ||
        !c.live_authenticated_server || !c.matching_native_protocol ||
        !c.original_transaction_payment_active ||
        !c.server_has_complete_selected_bill ||
        !c.exact_selection_bound || !c.authoritative_magic_entitlement ||
        !c.authority_epoch_current || !c.no_conflicting_free_craft)
        return call.vanilla_result;

    // An estimate may be stale the instant it reaches the UI. This only
    // lights up a button. The original server transaction independently
    // rechecks items, quantities, permissions, and can still refuse.
    return std::max(call.vanilla_result, c.approved_count);
}

} // namespace cbpay::client
