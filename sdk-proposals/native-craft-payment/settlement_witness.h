// SPDX-License-Identifier: MIT
// Read-only witness for a future isolated original RecipeApply runtime test.
// A successful call to InventoryRollback is NOT proof that stock was restored.
// Never use these counters as RecipePaymentProof.original_rollback.
#pragma once

#include <array>
#include <cstdint>
#include <mutex>

namespace cbpay::settlement {

enum class Payer : uint8_t { InventoryPay = 1, RecipePayAlt = 2 };
enum class Finality : uint8_t { Commit = 1, Rollback = 2 };
struct Receipt {
    // None of these values are accepted from a client or managed mod.
    void* borrowed_original_tx = nullptr;
    uint32_t actor = 0;
    Payer payer = Payer::InventoryPay;
    uint32_t original_payer_result = 0;
    // Counts from native ORIGINAL transaction journal, not a quote.
    uint32_t staged_take_ops = 0;
    bool native_journal_checked = false;
    bool original_recipe_callsite_checked = false;
};
struct Statistics {
    uint64_t recorded = 0;
    uint64_t pending = 0;
    uint64_t committed_success = 0;
    uint64_t rolled_back_success = 0; // e.g. later output failure
    uint64_t rolled_back_failed = 0;
    uint64_t committed_failed = 0; // serious incompatibility
    uint64_t partial_failed_rollback_call = 0; // NOT an inventory conservation proof
    uint64_t ambiguous = 0;
    uint64_t refused = 0;
    uint64_t finality_faults = 0;
    uint64_t resets_with_pending = 0;
};
class Witness final {
    static constexpr size_t Capacity = 128;
    struct Pending {
        void* tx = nullptr;
        Receipt receipt{};
        bool ambiguous = false;
    };
    std::mutex mu_;
    std::array<Pending, Capacity> slots_{};
    Statistics stats_{};
public:
    // The native payer must have RETURNED. Record nothing before calling it.
    bool payer_returned(const Receipt& r) {
        if (!r.borrowed_original_tx || !r.actor ||
            !r.original_recipe_callsite_checked ||
            (r.payer != Payer::InventoryPay && r.payer != Payer::RecipePayAlt)) {
            std::lock_guard lock(mu_);
            ++stats_.refused;
            return false;
        }
        if (r.staged_take_ops && !r.native_journal_checked) {
            std::lock_guard lock(mu_);
            ++stats_.refused;
            return false;
        }
        std::lock_guard lock(mu_);
        for (auto& slot : slots_) {
            if (slot.tx != r.borrowed_original_tx) continue;
            slot.ambiguous = true; // multiple payer calls: never infer one-to-one finality
            ++stats_.ambiguous;
            return false;
        }
        for (auto& slot : slots_) {
            if (slot.tx) continue;
            slot.tx = r.borrowed_original_tx;
            slot.receipt = r;
            slot.ambiguous = false;
            ++stats_.recorded;
            ++stats_.pending;
            return true;
        }
        ++stats_.refused;
        return false; // full: never evict a live transaction or reuse a pointer
    }

    // Invoke ONLY after the actual original InventoryCommit/InventoryRollback
    // has returned normally. A thrown exception does not establish finality.
    void original_finality_returned(void* tx, Finality action) {
        std::lock_guard lock(mu_);
        if (!tx || (action != Finality::Commit && action != Finality::Rollback)) {
            ++stats_.finality_faults;
            return;
        }
        for (auto& slot : slots_) {
            if (slot.tx != tx) continue;
            const Receipt r = slot.receipt;
            const bool bad = slot.ambiguous;
            slot = {};
            --stats_.pending;
            if (bad) return;
            if (action == Finality::Commit) {
                if (r.original_payer_result) ++stats_.committed_failed;
                else ++stats_.committed_success;
            } else {
                if (r.original_payer_result) {
                    ++stats_.rolled_back_failed;
                    if (r.native_journal_checked && r.staged_take_ops)
                        ++stats_.partial_failed_rollback_call;
                } else ++stats_.rolled_back_success;
            }
            return;
        }
        // Other transactions legitimately settle without a recipe payer.
    }

    Statistics snapshot() {
        std::lock_guard lock(mu_);
        return stats_;
    }
    // Call at game-world exit/reload. Retaining tx pointers across worlds is invalid.
    void reset_world() {
        std::lock_guard lock(mu_);
        stats_.resets_with_pending += stats_.pending;
        slots_ = {};
        stats_.pending = 0;
    }
};
} // namespace cbpay::settlement
