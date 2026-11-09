#!/usr/bin/env python3
"""Add an optional native-only snapshot of real InventorySetup links/slots
inside an ORIGINAL borrowed game InventoryTx. Exact published Kiln source only.

No persistent link writes, detours, payer changes, item removals, trusted
Magic/base claims, or standalone mod DLL installation. The normal public
Kiln.Items.read API runs on Pre/Post and is NOT valid inside payer hooks.
"""
import os
from pathlib import Path
import re
import subprocess
import sys

if len(sys.argv) != 2:
    raise SystemExit("Usage: tx-inventory-snapshot.py <disposable-Kiln-checkout>")
root=Path(sys.argv[1]).resolve()
pin=os.environ.get("KILN_UPSTREAM_SHA","")
if not re.fullmatch(r"[0-9a-f]{40}",pin):
    raise SystemExit("Exact published KILN_UPSTREAM_SHA required")
got=subprocess.check_output(["git","-C",str(root),"rev-parse","HEAD"],text=True).strip()
if got!=pin:raise SystemExit(f"Upstream SHA mismatch {got} != {pin}")

def edit(path,old,new):
    p=root/path
    raw=p.read_text(encoding="utf8")
    n=raw.count(old)
    if n!=1:raise SystemExit(f"Unsafe anchor {path}: {old[:65]!r} count={n}")
    p.write_text(raw.replace(old,new),encoding="utf8")
    print("PATCH",path)

abi="native/core/abi/services/kiln_items.h"
edit(abi,"typedef struct KilnItemsApi {",r"""/* Experimental: detached, AUTHORITATIVE contents and link identities
 * from an EXISTING original game InventoryTx, NOT an authorisation to pay.
 * The transaction must stay alive throughout the call. Never retain pointers.
 * This native-only function is not surfaced to managed mods. The source's
 * base/player rights and Magic status remain separately UNVERIFIED.
 * size must be sizeof(KilnItemsTxSnapshot). flags==0 means no permissions
 * proven. All slot records come from the ORIGINAL transaction accessors. */
typedef struct KilnItemsTxSnapshot {
    uint32_t size;
    KilnEntity actor;
    KilnEntity owner;
    uint32_t slot_total;
    uint32_t inventory_count;
    uint32_t flags;     /* RESERVED 0: no native authorisation performed */
    KilnEntity inventory_entities[KILN_ITEMS_MAX_INVENTORIES];
} KilnItemsTxSnapshot;

typedef struct KilnItemsApi {""")

edit(abi,
"""    KilnResult(KILN_CALL* peek_result)(uint64_t ticket, KilnItemsResult* out);
} KilnItemsApi;""",
"""    KilnResult(KILN_CALL* peek_result)(uint64_t ticket, KilnItemsResult* out);
    /* Optional native ABI tail; check header.size. The original transaction
     * operator must match actor, authoritative server world only; snapshots ALL pages of
     * owner using the same native read-other accessors as game's transaction.
     * No partial results: KILN_ERR_BUFFER_TOO_SMALL if capacity < slot_total.
     * This does not check Magic, base membership or actor permission. */
    KilnResult(KILN_CALL* read_in_tx)(void* borrowed_tx, KilnEntity actor,
                                     KilnEntity owner, KilnItemsTxSnapshot* snapshot,
                                     KilnItemsSlot* slots, uint32_t capacity);
} KilnItemsApi;""")

source="native/services/items/items.cpp"
edit(source,
"static_assert(sizeof(KilnItemsInventory) == 184 && offsetof(KilnItemsInventory, categories) == 152);",
"""static_assert(sizeof(KilnItemsInventory) == 184 && offsetof(KilnItemsInventory, categories) == 152);
static_assert(sizeof(KilnItemsTxSnapshot) == 152 &&
              offsetof(KilnItemsTxSnapshot, inventory_entities) == 24);""")

anchor="const KilnItemsApi kTable = {"
func=r"""// Native-only borrowed original transaction read. Kiln.Items.read (Pre/Post)
// cannot be used in a game payment hook. This uses the ALREADY GUARDED
// kitems::access_from_tx, read_pages, and read_slots helpers. No memory is
// written back to the transaction or the game's ECS: output is a detached copy.
// The actor match only establishes tx.operator identity, NOT access rights.
bool tx_operator_is(void* tx, KilnEntity actor) {
    __try {
        uint32_t op = 0;
        const auto* b = static_cast<const uint8_t*>(tx);
        std::memcpy(&op, b + E.tx_operator, sizeof op);
        return op == actor;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// The client's single-player session also owns an AUTHORITATIVE server world.
// Never confuse it with a joined remote client's presentation/prediction world.
bool cbpay_original_server_world() {
    if (!g_api || !g_api->session || !g_api->world ||
        !g_api->server_world || !g_api->side) return false;
    KilnSessionInfo state{};
    state.size = sizeof state;
    if (g_api->session(&state) != KILN_OK) return false;
    const auto side = g_api->side();
    if (side == KILN_SIDE_SERVER) {
        if (state.state != KILN_SESSION_DEDICATED ||
            state.side != KILN_SIDE_SERVER) return false;
    } else if (side == KILN_SIDE_CLIENT) {
        if (state.state != KILN_SESSION_HOST ||
            state.side != KILN_SIDE_CLIENT) return false;
    } else return false;
    const uintptr_t server = g_api->world(KILN_WORLD_SERVER);
    return server && server == g_api->server_world() &&
           server == state.worlds[KILN_WORLD_SERVER];
}

KilnResult KILN_CALL t_read_in_tx(void* tx, KilnEntity actor, KilnEntity owner,
                                 KilnItemsTxSnapshot* snapshot,
                                 KilnItemsSlot* slots, uint32_t capacity) {
    if (!tx || !actor || !owner || !snapshot ||
        snapshot->size < sizeof(KilnItemsTxSnapshot) ||
        capacity > KILN_ITEMS_MAX_INVENTORIES * 8u ||
        (capacity && !slots))
        return KILN_ERR_INVALID_ARGUMENT;
    if (!E.tx_ok || !E.read_other || !E.job_storage || !E.component_map_find ||
        !cbpay_original_server_world() ||
        !tx_operator_is(tx, actor))
        return KILN_ERR_UNAVAILABLE;

    Access access{};
    if (!access_from_tx(tx, &access)) return KILN_ERR_UNAVAILABLE;
    std::vector<PageSet> pages(1);
    bool initialized = false;
    if (!read_pages(access, owner, &pages[0], &initialized) ||
        !initialized || !pages[0].n ||
        pages[0].n > KILN_ITEMS_MAX_INVENTORIES)
        return KILN_ERR_NOT_FOUND;
    const auto count = pages[0].n * 8u;
    if (capacity < count) return KILN_ERR_BUFFER_TOO_SMALL;

    std::vector<KilnItemsSlot> detached(count);
    if (read_slots(access, owner, detached.data(), count) != count)
        return KILN_ERR_UNAVAILABLE;

    KilnItemsTxSnapshot value{};
    value.size = sizeof value;
    value.actor = actor;
    value.owner = owner;
    value.slot_total = count;
    value.inventory_count = pages[0].n;
    value.flags = 0; // NEVER infer permission from an InventorySetup link.
    for (uint32_t i = 0; i < pages[0].n; ++i) {
        const KilnEntity id = pages[0].p[i].entity;
        if (!id) return KILN_ERR_UNAVAILABLE;
        value.inventory_entities[i] = id;
        for (uint32_t j = 0; j < 8; ++j) {
            const KilnItemsSlot& slot = detached[i * 8 + j];
            if (static_cast<uint32_t>(slot.slot) != id ||
                (slot.slot >> 32) != j ||
                (slot.item == 0 && slot.count != 0) ||
                (slot.item != 0 && slot.count == 0))
                return KILN_ERR_UNAVAILABLE;
        }
    }
    // Write output only after ALL native reads and link checks succeeded.
    if (count) std::memcpy(slots, detached.data(), count * sizeof(KilnItemsSlot));
    *snapshot = value;
    return KILN_OK;
}

const KilnItemsApi kTable = {"""
edit(source,anchor,func)
edit(source,
"""    &t_peek_result,
};""",
"""    &t_peek_result, &t_read_in_tx,
};""")

print("PASS: native-only transaction-owned link and slot snapshot, no permissions guessed or native writes.")
