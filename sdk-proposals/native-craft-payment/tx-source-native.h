// SPDX-License-Identifier: MIT
// REAL native Kiln.Items optional transaction-snapshot ABI adapter, for use
// strictly inside the borrowed original recipe payment hook. This is not
// compiled into managed CrossBaseStorage and is NOT a mod-accessible hook.
#pragma once
#include "tx-source-provider.h"
#include "services/kiln_items.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace cbpay::sources {

// Converts the latest native ITEMS API tail's copy of real game slot IDs and
// quantities into Source input. No remote Magic, base or player rights are
// established here. Only a separate trusted game-world grant may do that.
inline bool read_from_original_tx(const KilnItemsApi* api,
                                  void* original_tx, uint32_t actor,
                                  uint32_t owner, OwnerView* out) {
    if (!api || !original_tx || !actor || !owner || !out ||
        api->header.size < offsetof(KilnItemsApi, read_in_tx) +
                               sizeof(api->read_in_tx) ||
        !api->read_in_tx)
        return false;

    KilnItemsTxSnapshot native{};
    native.size = sizeof native;
    std::array<KilnItemsSlot, KILN_ITEMS_MAX_INVENTORIES * 8u> slots{};
    if (api->read_in_tx(original_tx, actor, owner, &native,
                        slots.data(), static_cast<uint32_t>(slots.size())) != KILN_OK)
        return false;
    if (native.size != sizeof native || native.actor != actor ||
        native.owner != owner || native.flags != 0 ||
        native.inventory_count == 0 ||
        native.inventory_count > KILN_ITEMS_MAX_INVENTORIES ||
        native.slot_total != native.inventory_count * 8u)
        return false;

    OwnerView copy{};
    copy.actor = actor;
    copy.owner = owner;
    copy.inventory_ids.reserve(native.inventory_count);
    copy.slots.reserve(native.slot_total);
    for (uint32_t p = 0; p < native.inventory_count; ++p) {
        const auto id = native.inventory_entities[p];
        if (id == 0) return false;
        copy.inventory_ids.push_back(id);
        for (uint32_t j = 0; j < 8; ++j) {
            const auto& sl = slots[p * 8 + j];
            if (static_cast<uint32_t>(sl.slot) != id ||
                (sl.slot >> 32) != j)
                return false;
            copy.slots.push_back({sl.slot, sl.item, sl.count});
        }
    }
    *out = std::move(copy);
    return true;
}

// A native caller supplies owners from the LIVE original world, NOT a
// managed scanner. The Grant callback must establish real Magic/base/player
// permissions inside the same transaction or the source is NOT read/usable.
template<typename NativeGrant>
Stock collect_original_tx(const KilnItemsApi* items, void* original_tx,
                          uint32_t actor, std::span<const uint32_t> owners,
                          NativeGrant&& grant) {
    return collect(actor, owners, [&](uint32_t p, uint32_t owner, OwnerView* out) {
        return read_from_original_tx(items, original_tx, p, owner, out);
    }, std::forward<NativeGrant>(grant));
}

} // namespace cbpay::sources
