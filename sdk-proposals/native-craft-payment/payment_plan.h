// SPDX-License-Identifier: MIT
// Experimental cross-base recipe-payment preflight. Pure C++20, NO game reads or writes.
// NOT an upstream Kiln API. Plans are tentative until validated by a native
// transaction-time authority and checked against the game's REAL transaction journal.
#pragma once

#include <algorithm>
#include <cstdint>
#include <limits>
#include <span>
#include <unordered_set>
#include <vector>

namespace cbpay {

enum class Failure : uint8_t {
    None,
    InvalidActor,
    InvalidRequirement,
    UnsupportedCategory,
    InvalidSnapshot,
    DuplicateSlot,
    TooMany,
    InsufficientStock,
};

// item==0/category inputs require resolution by the actual recipe payer.
// Never guess a concrete item from a category placeholder.
struct Requirement {
    uint32_t item = 0;
    uint32_t count = 0;
    bool category = false;
};

// An AUTHORITATIVE, NATIVE resolver must establish these proof bits from the
// live transaction context. Template-name matches do NOT establish Magic.
// Every record is ONE packed real inventory slot, not the chest owner.
struct Source {
    uint32_t owner = 0;
    uint64_t slot = 0;
    uint32_t item = 0;
    uint32_t units = 0;
    bool loaded = false;
    bool magic_verified = false;
    bool permission_verified = false;
    bool base_verified = false;
    bool same_base = false;
};

struct Take {
    uint32_t owner = 0;
    uint64_t slot = 0;
    uint32_t item = 0;
    uint32_t units = 0;
};

struct Plan {
    Failure failure = Failure::None;
    uint32_t missing_item = 0;
    uint64_t missing_units = 0;
    std::vector<Take> takes;
    explicit operator bool() const { return failure == Failure::None; }
};

inline Plan fail(Failure e, uint32_t item = 0, uint64_t amount = 0) {
    return {e, item, amount, {}};
}

// Local first, then eligible same-base Magic, then distant Magic; within
// each group deterministic owner and slot order. Unverified/unloaded remote
// sources contribute no stock. Any missing cost leaves NO proposed takes.
inline Plan make_plan(uint32_t actor, std::span<const Requirement> needs,
                      std::span<const Source> observed) {
    constexpr size_t kMaxCosts = 64, kMaxSources = 8192;
    if (!actor) return fail(Failure::InvalidActor);
    if (needs.empty() || needs.size() > kMaxCosts || observed.size() > kMaxSources)
        return fail(needs.size() > kMaxCosts || observed.size() > kMaxSources ? Failure::TooMany
                                                                              : Failure::InvalidRequirement);
    for (const Requirement& need : needs) {
        if (need.category || !need.item) return fail(Failure::UnsupportedCategory);
        if (!need.count) return fail(Failure::InvalidRequirement);
    }
    std::unordered_set<uint64_t> seen;
    seen.reserve(observed.size());
    for (const Source& s : observed) {
        if (!s.owner || !s.slot || !(uint32_t(s.slot)) || (s.slot >> 32) >= 8 || !s.item)
            return fail(Failure::InvalidSnapshot);
        if (!seen.insert(s.slot).second) return fail(Failure::DuplicateSlot);
    }
    std::vector<size_t> eligible;
    eligible.reserve(observed.size());
    for (size_t i = 0; i < observed.size(); ++i) {
        const auto& s = observed[i];
        if (!s.loaded || !s.units) continue;
        if (s.owner != actor && !(s.magic_verified && s.permission_verified && s.base_verified)) continue;
        eligible.push_back(i);
    }
    auto priority = [&](size_t i) -> int {
        const auto& s = observed[i];
        return s.owner == actor ? 0 : (s.same_base ? 1 : 2);
    };
    std::sort(eligible.begin(), eligible.end(), [&](size_t a, size_t b) {
        const auto& x = observed[a];
        const auto& y = observed[b];
        if (priority(a) != priority(b)) return priority(a) < priority(b);
        if (x.owner != y.owner) return x.owner < y.owner;
        return x.slot < y.slot;
    });
    std::vector<uint32_t> available;
    available.reserve(observed.size());
    for (const auto& s : observed) available.push_back(s.units);
    Plan planned;
    for (const Requirement& need : needs) {
        uint64_t left = need.count;
        for (size_t idx : eligible) {
            const Source& src = observed[idx];
            if (src.item != need.item || !available[idx]) continue;
            const uint32_t qty = static_cast<uint32_t>(std::min<uint64_t>(left, available[idx]));
            // The game's journal op amount is a u16; never silently truncate.
            if (qty > std::numeric_limits<uint16_t>::max()) return fail(Failure::TooMany);
            if (qty == 0) continue;
            available[idx] -= qty;
            left -= qty;
            auto existing = std::find_if(planned.takes.begin(), planned.takes.end(), [&](const Take& op) {
                return op.slot == src.slot && op.item == need.item;
            });
            if (existing != planned.takes.end()) {
                if (existing->units > std::numeric_limits<uint16_t>::max() - qty)
                    return fail(Failure::TooMany);
                existing->units += qty;
            } else {
                planned.takes.push_back({src.owner, src.slot, src.item, qty});
            }
            if (!left) break;
        }
        if (left) return fail(Failure::InsufficientStock, need.item, left);
    }
    return planned;
}

struct JournalEntry {
    uint64_t slot = 0;
    uint32_t item = 0;
    uint32_t units = 0;
    bool take = false;
};

struct StageResult {
    uint32_t game_error = 0;
    uint32_t remaining = 0;
    std::vector<JournalEntry> appended;
};

enum class PaymentOutcome : uint8_t {
    Declined,             // no operations staged; vanilla payer may run
    StagedExact,          // full cost staged; ORIGINAL recipe owns commit/rollback
    AbortNeedsRollback,   // partial operations MAY exist; outer tx must abort/rollback
};

// Native adapter MUST invoke InventoryRemoveItems with the original RecipeApply
// tx and read ONLY newly appended journal operations. The preferred-slot arg
// is just a HINT: even a successful removal may choose a different slot.
// NEVER tx.commit() here. On failed staging, caller MUST abort RecipeApply
// and roll back the same tx, never fall through to vanilla payment.
template <typename Stage>
PaymentOutcome stage_plan(const Plan& p, uint32_t actor, Stage&& stage) {
    // Leave vanilla untouched when all ingredients are already payable locally.
    if (!actor || !p || p.takes.empty() ||
        std::none_of(p.takes.begin(), p.takes.end(), [actor](const Take& t) { return t.owner != actor; }))
        return PaymentOutcome::Declined;
    for (const Take& t : p.takes) {
        StageResult r = stage(t);
        if (r.game_error || r.remaining != 0 || r.appended.empty())
            return PaymentOutcome::AbortNeedsRollback;
        uint64_t exact = 0;
        for (const JournalEntry& entry : r.appended) {
            if (!entry.take || entry.slot != t.slot || entry.item != t.item || !entry.units)
                return PaymentOutcome::AbortNeedsRollback;
            exact += entry.units;
        }
        if (exact != t.units) return PaymentOutcome::AbortNeedsRollback;
    }
    return PaymentOutcome::StagedExact;
}

} // namespace cbpay
