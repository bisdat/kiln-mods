// SPDX-License-Identifier: MIT
// Construct exact candidate source slots from DETACHED ORIGINAL-TX INVENTORY
// snapshots. Every remote owner is checked by an independent transaction-time
// AUTHORITATIVE grant callback BEFORE inventory contents are read. A cached
// scan or "_Magic" template does not qualify.
// No game writes/allocations to inventory/transaction state here.
#pragma once
#include "payment_plan.h"

#include <algorithm>
#include <cstdint>
#include <span>
#include <unordered_set>
#include <vector>

namespace cbpay::sources {

struct Slot {
    uint64_t id = 0;  // low32 actual Inventory entity; high32 slot index
    uint32_t item = 0;
    uint32_t units = 0;
};
struct OwnerView {
    uint32_t actor = 0;
    uint32_t owner = 0;
    // One ID per REAL linked 8-slot inventory; never the visible chest count.
    std::vector<uint32_t> inventory_ids;
    std::vector<Slot> slots;
};
struct Grant {
    bool native_magic = false;
    bool player_access = false;
    bool base_access = false;
    bool same_base = false;
    explicit operator bool() const {
        return native_magic && player_access && base_access;
    }
};
enum class Result : uint8_t {
    Ready,
    InvalidActor,
    InvalidOwners,
    InvalidSnapshot,
    LimitExceeded,
    NativeReadFailed,
};
struct Stock {
    Result status = Result::InvalidActor;
    std::vector<Source> entries;
    uint32_t owner_count = 0;
    uint32_t skipped_unauthorised = 0;
};

// The read(actor, owner, OwnerView*) callback must call genuine Kiln Items
// read_in_tx(borrowed OriginalRecipeTx...) native ABI, not Items.read()
// (wrong thread) or a managed scan, returning a complete slot set.
// The grant(actor, owner) callback MUST resolve true native Magic/base/player
// rights from the SAME original world transaction. This helper never
// establishes those rights.
template<class Read, class Authorise>
Stock collect(uint32_t actor, std::span<const uint32_t> owners,
              Read&& read, Authorise&& grant) {
    Stock output{};
    if (!actor) return output;
    if (owners.empty() || owners.size() > 1024) {
        output.status = Result::InvalidOwners;
        return output;
    }
    const auto invalid = [&](Result code) {
        Stock r{};
        r.status = code;
        return r; // discard EVERY partial source on any bad owner or link
    };
    std::unordered_set<uint32_t> seen_owners;
    std::unordered_set<uint64_t> seen_slots;
    bool has_actor = false;
    for (auto owner : owners) {
        if (!owner || !seen_owners.insert(owner).second)
            return invalid(Result::InvalidOwners);
        if (owner == actor) has_actor = true;
    }
    if (!has_actor) return invalid(Result::InvalidOwners);
    for (auto owner : owners) {
        Grant verifiedGrant{};
        if (owner != actor) {
            verifiedGrant = grant(actor, owner);
            if (!verifiedGrant) {
                ++output.skipped_unauthorised;
                continue; // don't even read other players' private storage
            }
        }
        OwnerView view{};
        if (!read(actor, owner, &view))
            return invalid(Result::NativeReadFailed);
        if (view.actor != actor || view.owner != owner ||
            view.inventory_ids.empty() || view.inventory_ids.size() > 32 ||
            view.slots.size() != view.inventory_ids.size() * 8u)
            return invalid(Result::InvalidSnapshot);
        std::unordered_set<uint32_t> links;
        for (uint32_t id : view.inventory_ids)
            if (!id || !links.insert(id).second)
                return invalid(Result::InvalidSnapshot);
        for (size_t i = 0; i < view.slots.size(); ++i) {
            const Slot& slot = view.slots[i];
            if (static_cast<uint32_t>(slot.id) != view.inventory_ids[i / 8] ||
                (slot.id >> 32) != (i % 8) ||
                !seen_slots.insert(slot.id).second ||
                (slot.item == 0 && slot.units != 0) ||
                (slot.item != 0 && slot.units == 0))
                return invalid(Result::InvalidSnapshot);
            if (slot.item == 0) continue;
            if (output.entries.size() >= 8192)
                return invalid(Result::LimitExceeded);
            output.entries.push_back({
                owner,slot.id,slot.item,slot.units,
                true, owner != actor && verifiedGrant.native_magic,
                owner == actor || verifiedGrant.player_access,
                owner == actor || verifiedGrant.base_access,
                owner != actor && verifiedGrant.same_base
            });
        }
        ++output.owner_count;
    }
    output.status = Result::Ready;
    return output;
}
} // namespace cbpay::sources
