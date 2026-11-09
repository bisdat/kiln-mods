// SPDX-License-Identifier: MIT
// Exact original-payer lifetime for the server's stock-mode-2 item-owner union.
// The first live payer hook establishes a TLS scope. Its ORIGINAL native
// base-stock selector then runs, and an optional verified extension callback
// can replace ONLY that selector's returned ordinary owner-list pointer.
// No listener may retain an owner list beyond the original payer return.
#pragma once
#include "native_stock_overlay.h"
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace cbpay::stock::native {

using VerifiedRoster = bool (*)(
    uint32_t actor, void* borrowed_original_tx,
    const GameStockLists& original,
    std::span<const uint32_t> copied_vanilla_owners,
    std::vector<Source>& original_sources,
    std::vector<Source>& extra_sources,
    Context* proof,
    NativeGrant* grant);

// Tests may use this adapter with fake original stock callbacks. Production
// must populate it ONLY after the engine's genuine selector has returned and
// the exact server executable, callsite and tx pointer have been verified.
class OriginalPayerScope final {
    OriginalPayerScope* previous_ = nullptr;
    uint32_t actor_ = 0;
    void* tx_ = nullptr;
    VerifiedRoster provider_ = nullptr;
    std::array<ScopedItemUnion, 2> selected_{};
    size_t used_ = 0;
    bool invalid_ = false;
    uint32_t published_ = 0;

    // TLS address token is valid strictly for the original payer's stack scope.
    // An integer key avoids GCC's false escaping-stack-pointer warning.
    inline static thread_local std::uintptr_t active_key_ = 0;
public:
    // Scope must exist only DURING h_pay.call() / h_alt.call(), after any
    // creative-mode bypass. Nested payers are never eligible for an overlay.
    OriginalPayerScope(uint32_t actor, void* tx, VerifiedRoster provider)
        : previous_(current()), actor_(actor), tx_(tx), provider_(provider) {
        if (!previous_ && actor_ && tx_ && provider_) active_key_ = reinterpret_cast<std::uintptr_t>(this);
        else invalid_ = true;
    }
    OriginalPayerScope(const OriginalPayerScope&) = delete;
    OriginalPayerScope& operator=(const OriginalPayerScope&) = delete;
    OriginalPayerScope(OriginalPayerScope&&) = delete;
    OriginalPayerScope& operator=(OriginalPayerScope&&) = delete;
    ~OriginalPayerScope() {
        if (current() == this)
            active_key_ = reinterpret_cast<std::uintptr_t>(previous_);
    }
    static OriginalPayerScope* current() {
        return reinterpret_cast<OriginalPayerScope*>(active_key_);
    }
    bool eligible(uint32_t actor, void* tx) const {
        return !invalid_ && actor_ == actor && tx_ == tx && provider_ &&
               current() == this && used_ < selected_.size();
    }
    uint32_t published() const { return published_; }

    // call only AFTER the real game selector, never before; the original
    // result remains intact if any proof, linked page, grant or allocation
    // fails. GameStockLists has 32-byte native ABI and distinct fuel entries.
    bool after_original_selector(uint32_t actor, void* original_tx,
                                 GameStockLists* game_result) noexcept {
        if (!game_result || !eligible(actor,original_tx)) return false;
        // Consume one of the game's (normally 2) selector slots even if
        // validation fails. Never reuse a previous result backing vector.
        auto& selected = selected_[used_++];
        try {
            const GameStockLists original = *game_result;
            if (original.item_count > 4096 ||
                (original.item_count && !original.item_owners) ||
                original.fuel_count > 4096 ||
                (original.fuel_count && !original.fuel_owners))
                return false;

            std::vector<uint32_t> copied;
            if (original.item_count) {
                // The native caller MUST SEH-probe the borrowed pointer and
                // its full length before reaching this C++ routine.
                copied.assign(original.item_owners,
                              original.item_owners + original.item_count);
            }
            std::vector<Source> vanilla;
            std::vector<Source> remote;
            Context proof{};
            NativeGrant grant=nullptr;
            if (!provider_(actor_,tx_,original,copied,vanilla,remote,
                           &proof,&grant) || !grant)
                return false;
            if (selected.assemble(proof,original,copied,vanilla,remote,grant)
                != Status::Ready)
                return false;
            GameStockLists shadow = original;
            if (!selected.publish(&shadow)) return false;
            // Recheck the input is still the same original result; no writes
            // to the original borrowed base record or the fuel list.
            if (game_result->item_owners != original.item_owners ||
                game_result->item_count != original.item_count ||
                game_result->fuel_owners != original.fuel_owners ||
                game_result->fuel_count != original.fuel_count)
                return false;
            *game_result = shadow;
            ++published_;
            return true;
        } catch (...) {
            // Fail closed: the game continues with its unmodified original
            // selector result. No exception escapes an original game hook.
            return false;
        }
    }
};

} // namespace cbpay::stock::native
