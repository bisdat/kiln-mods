// SPDX-License-Identifier: MIT
// Canonical ordered native crafting-stock source policy.
// Inputs MUST originate from the original game-native stock collector and
// an independently authorised *same-transaction* Magic Chest roster.
//
// The game itself owns recipe selection, consumption, commit/rollback and
// ordinary/remote access. This file NEVER transfers, removes or gives items.
// It is intended for BOTH server original-stock and client cache paths, not
// for synthetic untrusted UI stock totals.
#pragma once

#include <cstdint>
#include <initializer_list>
#include <span>
#include <unordered_set>
#include <vector>

namespace cbpay::stock {

struct Source {
    uint32_t owner = 0;
    // Precisely the original inventory's linked 8-slot entity IDs.
    // A source with duplicated/shared links cannot be charged safely.
    std::vector<uint32_t> linked;
    // These facts MUST be read in the original game-authorised context.
    bool native_magic_component = false;
    bool inventory_initialized = false;
    bool loaded = false;
    bool same_base = false;
    bool actor_authorized = false;
    bool base_authorized = false;
    bool game_source_eligible = false;
};

enum class Status : uint8_t {
    Ready, Malformed, AmbiguousLinks, Unauthorised, Incomplete, TooMany
};

struct Result {
    Status status = Status::Incomplete;
    std::vector<Source> ordered;
    bool had_remote = false;
    explicit operator bool() const { return status == Status::Ready; }
};

// A grant is only verified if the game approves every element. A client
// mirror must not invent missing entitlements from templates, positions,
// read-only inventory snapshots or a stale saved-world identifier.
using NativeGrant = bool (*)(uint32_t actor, const Source& source);

inline Result extend(uint32_t actor,
                     std::span<const Source> vanilla_sources,
                     std::span<const Source> extra_magic_sources,
                     NativeGrant verify,
                     bool native_collector_bound,
                     bool complete_source_roster) {
    if (!actor || !native_collector_bound || !complete_source_roster ||
        !verify)
        return {Status::Incomplete, {}, false};
    if (vanilla_sources.size() > 4096 || extra_magic_sources.size() > 4096 ||
        vanilla_sources.size() + extra_magic_sources.size() > 4096)
        return {Status::TooMany, {}, false};

    std::unordered_set<uint32_t> owners;
    std::unordered_set<uint32_t> linked;
    Result out{Status::Ready,{},false};
    out.ordered.reserve(vanilla_sources.size()+extra_magic_sources.size());

    auto accept = [&](const Source& s, bool original) -> Status {
        if (!s.owner || !s.inventory_initialized || !s.loaded ||
            s.linked.empty() || s.linked.size() > 32 ||
            !s.game_source_eligible)
            return Status::Malformed;
        if (!owners.insert(s.owner).second)
            return Status::AmbiguousLinks;
        std::unordered_set<uint32_t> local;
        for (uint32_t link : s.linked) {
            if (!link || !local.insert(link).second ||
                !linked.insert(link).second)
                return Status::AmbiguousLinks;
        }
        if (!original && (!s.native_magic_component ||
                          !s.actor_authorized || !s.base_authorized ||
                          !verify(actor,s)))
            return Status::Unauthorised;
        out.ordered.push_back(s);
        if (!original && !s.same_base) out.had_remote = true;
        return Status::Ready;
    };
    // Keep the game's already-authorised sources in their exact vanilla
    // priority; do not displace the player's own inventory or local chests.
    for (const auto& s : vanilla_sources) {
        const auto v=accept(s,true);
        if (v != Status::Ready) return {v,{},false};
    }
    // Candidate enumeration may include a source already present in the
    // original stock list. Preserve that original source once if and ONLY if
    // its complete linked inventory identity agrees. Divergent identities
    // are ambiguous, not two inventories to count or pay separately.
    auto in_original = [&](const Source& candidate) -> bool {
        for (const auto& old : vanilla_sources) {
            if (old.owner != candidate.owner) continue;
            return old.linked == candidate.linked;
        }
        return false;
    };
    // Two phases retain local-before-distant order if the game passed a
    // mixed candidate set. Never add unverified local candidates either.
    for (bool local : {true,false}) {
        for (const auto& s : extra_magic_sources) {
            if (s.same_base != local) continue;
            if (owners.contains(s.owner)) {
                if (!in_original(s)) return {Status::AmbiguousLinks,{},false};
                // Already authorised by the original game's collector.
                continue;
            }
            const auto v=accept(s,false);
            if (v != Status::Ready) return {v,{},false};
        }
    }
    return out;
}

} // namespace cbpay::stock
