// SPDX-License-Identifier: MIT
// Native InventoryRemoveItems adapter, STRICTLY experimental.
// No runtime installation and no engine detour. Tested against a synthetic
// transaction with the byte layouts from released Kiln docs/re/items.md.
// The REAL game's rollback, native authority and permissions are not proven.
#pragma once
#include "payment_plan.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <limits>
#include <vector>

namespace cbpay {
#ifdef _WIN32
#define CBPAY_CALL __fastcall
#else
#define CBPAY_CALL
#endif

// Kiln native items Result12; MS x64 hidden return pointer is FIRST parameter.
struct NativeResult12 {
    uint8_t error;
    uint8_t reserved[3];
    uint32_t unknown;
    uint32_t still_missing;
};
static_assert(sizeof(NativeResult12) == 12);
using NativeRemoveFn = NativeResult12* (CBPAY_CALL*)(NativeResult12*, void*, uint32_t, uint32_t, uint64_t, uint32_t);

struct NativeOpCursor {
    const uint8_t* pointer = nullptr;
    uint64_t count = 0;
};

// Proven only for the pinned current release's InventoryTx: operator 0xe0,
// operation pointer 0xf0, op count 0xf8, record stride 0x34.
// Before enabling the adapter in a real native service, each must be matched
// against the runtime's trusted symbols/constants and protected by SEH.
inline bool read_native_tx(void* tx, uint32_t actor, NativeOpCursor* out) {
    if (!tx || !actor || !out) return false;
    auto* b = static_cast<const uint8_t*>(tx);
    uint32_t op = 0;
    std::memcpy(&op, b + 0xe0, sizeof op);
    if (op != actor) return false;
    std::memcpy(&out->pointer, b + 0xf0, sizeof out->pointer);
    std::memcpy(&out->count, b + 0xf8, sizeof out->count);
    return out->pointer && out->count <= 4096;
}

// The engine's op bytes are authoritative AFTER each remove_items call.
// An "exact" preferred slot can still be redirected by native engine rules.
inline bool exact_native_delta(const NativeOpCursor& before, const NativeOpCursor& after, const Take& expected) {
    if (!before.pointer || !after.pointer || after.count < before.count ||
        after.count - before.count > 128 || after.count == before.count ||
        after.count > SIZE_MAX / 0x34u ||
        reinterpret_cast<uintptr_t>(after.pointer) > UINTPTR_MAX - after.count * 0x34u)
        return false;
    uint64_t taken = 0;
    for (uint64_t i = before.count; i < after.count; ++i) {
        const uint8_t* op = after.pointer + i * 0x34u;
        uint64_t slot = 0;
        uint32_t item = 0;
        uint16_t qty = 0;
        uint8_t kind = op[0x26];
        std::memcpy(&slot, op, sizeof slot);
        std::memcpy(&item, op + 8, sizeof item);
        std::memcpy(&qty, op + 0x24, sizeof qty);
        if (kind != 1 || slot != expected.slot || item != expected.item || !qty)
            return false;
        taken += qty;
        if (taken > expected.units) return false;
    }
    return taken == expected.units;
}

// Explicitly requires the *original recipe's* transaction and the game
// function pointer resolved from current native Kiln symbols. The caller
// must verify RecipePaid callsite, current player, source permissions and
// native Magic/base entitlement before using this adapter.
//
// Never commits or rolls back! On AbortNeedsRollback, return a payer FAILURE
// that causes the OUTER original RecipeApply transaction to roll back.
// If the rollback semantics cannot be proven, do NOT hook/enable this.
inline PaymentOutcome stage_native_plan(const Plan& plan, uint32_t actor, void* tx, NativeRemoveFn remove) {
    if (!actor || !remove || !plan) return PaymentOutcome::Declined;
    if (plan.takes.empty() ||
        std::none_of(plan.takes.begin(), plan.takes.end(),
                     [actor](const Take& t) { return t.owner != actor; }))
        return PaymentOutcome::Declined;
    NativeOpCursor before{};
    if (!read_native_tx(tx, actor, &before)) return PaymentOutcome::Declined;
    for (const Take& t : plan.takes) {
        // All remote proof checks happen in the native resolver BEFORE calling
        // this function, and must be current at the moment of each take.
        if (!t.owner || !t.slot || !t.item || !t.units ||
            t.units > std::numeric_limits<uint16_t>::max()) return PaymentOutcome::AbortNeedsRollback;
        NativeResult12 res{};
        NativeResult12* returned = remove(&res, tx, t.owner, t.item, t.slot, t.units);
        NativeOpCursor after{};
        if (returned != &res || !read_native_tx(tx, actor, &after) ||
            res.error || res.still_missing ||
            !exact_native_delta(before, after, t))
            return PaymentOutcome::AbortNeedsRollback;
        before = after;
    }
    return PaymentOutcome::StagedExact;
}
} // namespace cbpay
