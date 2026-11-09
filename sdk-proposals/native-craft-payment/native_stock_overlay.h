// SPDX-License-Identifier: MIT
// Adapter for the original Enshrouded stock-mode-2 recipe payer.
// The game's base-stock lookup returns TWO different (pointer,count) pairs:
//   0: inventory owners for normal crafting items
//   1: fuel/water stock, with different transaction semantics
// Never append item owners to the fuel list, never replace the payer or
// commit/rollback, and never retain a borrowed list across RecipeApply.
#pragma once

#include "stock_sources.h"
#include <cstddef>
#include <cstdint>
#include <span>
#include <type_traits>
#include <vector>

namespace cbpay::stock::native {

// From current server build 26430253, SHA256 f0d824be7fcd48ebfe2458ff5c28da26060c2074e3ec1fcac587e5a1d508aeb2:
// original game's 0x2081c0 stock selector returns four qwords; ordinary
// source array is from a base record +0x30/+0x38, fuel is +0x58/+0x60.
// The current game native ABI, not these numbers, MUST be independently
// resolved before wiring the detour. These pointers are BORROWED, not owned.
struct alignas(8) GameStockLists {
    const uint32_t* item_owners = nullptr;
    std::uint64_t item_count = 0;
    const uint32_t* fuel_owners = nullptr;
    std::uint64_t fuel_count = 0;
};
static_assert(std::is_standard_layout_v<GameStockLists>);
static_assert(sizeof(GameStockLists) == 32);
static_assert(offsetof(GameStockLists, item_owners) == 0);
static_assert(offsetof(GameStockLists, item_count) == 8);
static_assert(offsetof(GameStockLists, fuel_owners) == 16);
static_assert(offsetof(GameStockLists, fuel_count) == 24);

// Proof must be populated from native original tx and live server-player
// records, never the user-facing CrossBaseStorage C# scanner.
struct Context {
    uint32_t actor = 0;
    uint32_t transaction_operator = 0;
    uint8_t stock_mode = 0;
    bool on_original_server_tick = false;
    bool pinned_executable_hash = false;
    bool real_original_stock_callsite = false;
    bool native_game_selector_success = false;
    bool complete_authoritative_roster = false;
    bool original_commit_and_rollback_bound = false;
    // Solo phase: actor has one authenticated game player record, with
    // inventory/base editing rights. Never assume AABB or template grants.
    bool game_player_record_verified = false;
    bool single_connected_player = false;
    bool game_inventory_access_permission = false;
    bool game_base_edit_permission = false;
    // These are additional original-game tests on all candidate chests, not
    // synthetic Flags ("magic" in template) or arbitrary World.Build.Bases.
    bool original_game_stock_registration_verified = false;
    bool current_actor_base_entitlement_verified = false;
};

enum class Status : uint8_t {
    Ready, Inactive, UnsafeCallsite, MalformedOriginal, OriginalMismatch,
    Unauthorised, NoExtraSources
};

// This object MUST outlive the original game payer's consumption of its
// modified itemOwners pointer. It is deliberately neither copyable nor
// movable so ownership cannot be silently transferred while an ABI caller
// holds the pointer. Enclose it in a per-thread, per-RecipeApply scope.
// The original game stock selector and payer must still be called normally.
class ScopedItemUnion final {
    std::vector<uint32_t> owned_item_ids_;
    GameStockLists joined_{};
    Status status_ = Status::Inactive;
public:
    ScopedItemUnion() = default;
    ScopedItemUnion(const ScopedItemUnion&) = delete;
    ScopedItemUnion& operator=(const ScopedItemUnion&) = delete;
    ScopedItemUnion(ScopedItemUnion&&) = delete;
    ScopedItemUnion& operator=(ScopedItemUnion&&) = delete;

    // original ids are read ONLY through a caller-provided validated span.
    // Native Windows caller must SEH-probe the game pointers before this
    // function and reject any change to those arrays while it assembles.
    Status assemble(
        const Context& ctx,
        const GameStockLists& game,
        std::span<const uint32_t> copied_original_item_ids,
        std::span<const Source> original_authorised_items,
        std::span<const Source> new_candidates,
        NativeGrant authoritative_grant)
    {
        owned_item_ids_.clear();
        joined_ = {};
        status_ = Status::Inactive;

        if (ctx.actor == 0 || ctx.actor != ctx.transaction_operator ||
            ctx.stock_mode != 2 || !ctx.on_original_server_tick ||
            !ctx.pinned_executable_hash || !ctx.real_original_stock_callsite ||
            !ctx.native_game_selector_success || !ctx.complete_authoritative_roster ||
            !ctx.original_commit_and_rollback_bound) {
            return status_ = Status::UnsafeCallsite;
        }
        if (!ctx.game_player_record_verified || !ctx.single_connected_player ||
            !ctx.game_inventory_access_permission || !ctx.game_base_edit_permission ||
            !ctx.original_game_stock_registration_verified ||
            !ctx.current_actor_base_entitlement_verified) {
            return status_ = Status::Unauthorised;
        }
        if ((game.item_count && !game.item_owners) ||
            (game.fuel_count && !game.fuel_owners) ||
            game.item_count > 4096 || game.fuel_count > 4096 ||
            game.item_count != copied_original_item_ids.size() ||
            original_authorised_items.size() != copied_original_item_ids.size()) {
            return status_ = Status::MalformedOriginal;
        }
        for (size_t i = 0; i < copied_original_item_ids.size(); ++i) {
            if (!copied_original_item_ids[i] ||
                copied_original_item_ids[i] != original_authorised_items[i].owner)
                return status_ = Status::OriginalMismatch;
        }
        auto result = cbpay::stock::extend(
            ctx.actor, original_authorised_items, new_candidates,
            authoritative_grant, true, true);
        if (!result) {
            return status_ = result.status == cbpay::stock::Status::Unauthorised ?
                Status::Unauthorised : Status::OriginalMismatch;
        }
        if (result.ordered.size() == original_authorised_items.size())
            return status_ = Status::NoExtraSources; // leave original pointer as-is
        owned_item_ids_.reserve(result.ordered.size());
        for (const auto& source : result.ordered) owned_item_ids_.push_back(source.owner);
        joined_ = game;
        joined_.item_owners = owned_item_ids_.data();
        joined_.item_count = owned_item_ids_.size();
        return status_ = Status::Ready;
    }

    // Original engine data remains unmodified unless the whole scope is
    // verified. Fuel owners, fuel count and their pointers are byte-equal.
    bool publish(GameStockLists* game_return) const {
        if (status_ != Status::Ready || !game_return ||
            !joined_.item_owners || !joined_.item_count)
            return false;
        *game_return = joined_;
        return true;
    }
    Status status() const { return status_; }
    size_t borrowed_owner_count() const { return owned_item_ids_.size(); }
};

// Single player check is intentionally stronger than having admin powers:
// only a verified real in-game record (not a replay/log fallback) on an
// otherwise empty server may enable phase-one remote base sourcing.
struct LivePlayerRecord {
    uint32_t entity = 0;
    uint32_t state = 0; // Kiln's KILN_PLAYER_IN_GAME = 2
    uint32_t flags = 0; // 0x08 HAS_PERMISSIONS, 0x10 FROM_RECORDS
    uint32_t rights = 0; // 0x02 CAN_ACCESS_INVENTORIES, 0x04 CAN_EDIT_BASE
    uint32_t connected = 0;
    uint32_t in_game = 0;
    bool source_records = false;
    bool layout_ok = false;
    bool simulated = false;
};
inline bool verified_solo_actor(uint32_t actor, const LivePlayerRecord& p) {
    return actor != 0 && actor == p.entity &&
           p.state == 2 && (p.flags & 0x18u) == 0x18u &&
           (p.rights & 0x06u) == 0x06u &&
           p.connected == 1 && p.in_game == 1 &&
           p.source_records && p.layout_ok && !p.simulated;
}
} // namespace cbpay::stock::native
