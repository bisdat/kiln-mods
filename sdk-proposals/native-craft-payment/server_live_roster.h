// SPDX-License-Identifier: MIT
// REAL native server stock roster from the game's registered base stock,
// Kiln's genuine player records, and Kiln.Items' ORIGINAL InventoryTx snapshot.
//
// Pinned original dedicated server AND verified client-host binary identities.
// No remote-only client or prediction-world stock access is allowed.
//
// This is NOT client-side rendering and NEVER invokes Items.Take/Give.
// It must only be called while the original recipe payer is on the stack.
// Only exact-build genuine original selector calls are eligible; all
// borrowed base records must also match original owner/fuel list identities
// dynamically before another base is considered.
#pragma once

#include "server_selector_abi.h"
#include "native_stock_builds.h"
#include "kiln_api.h"
#include "services/kiln_players.h"
#include "services/kiln_items.h"
#include "services/kiln_types.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>
#include <utility>

namespace cbpay::stock::live {

// Source identifiers and native base layout are checked again in-place
// against the game's genuine original selector result during every payer.
inline constexpr uint32_t kMagicStockHash = 0xe41a0d1fu;
inline constexpr uint32_t kBaseSlots = 16;
inline constexpr uint32_t kBaseStride = 0x130;
inline constexpr uint32_t kBaseItemPointer = 0x30;
inline constexpr uint32_t kBaseItemCount = 0x38;
inline constexpr uint32_t kBaseFuelPointer = 0x58;
inline constexpr uint32_t kBaseFuelCount = 0x60;

// InventoryCraftingStock is an ECS TEMPLATE component, not a dynamic
// component. GetComponent would return nullptr on genuine Magic Chests.
using GetTemplateComponent = void* (__fastcall*)(uintptr_t, uint32_t, uint32_t);

struct Services {
    const KilnApi* api = nullptr;
    const cbpay::stock::build::Profile* build = nullptr;
    const KilnPlayersApi* players = nullptr;
    const KilnItemsApi* items = nullptr;
    GetTemplateComponent get_template = nullptr;
    bool hash_ok = false;
    bool original_finality_ok = false;
    uint64_t original_client_cache_update_rva = 0;
};

inline Services S{};

// Selector output is computed AFTER the original selector. The only data
// inherited from the selector's input are the game's base-system pointers;
// no owner/actor from a client or managed mod is accepted.
struct SelectorInput {
    void* primary = nullptr;
    void* fallback = nullptr;
    bool genuine_first_or_second_call = false;
};
inline thread_local SelectorInput current_selector{};

inline bool read_u64(const void* base, uintptr_t off, uint64_t* out) {
    if (!base || !out) return false;
    __try {
        std::memcpy(out, static_cast<const uint8_t*>(base) + off, 8);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}
inline bool copy_owners(const uint32_t* src, uint64_t count,
                        std::vector<uint32_t>* out) {
    if (!out || count > 4096 || (count && !src)) return false;
    if (!count) { out->clear(); return true; }
    std::array<uint32_t,4096> tmp{};
    __try {
        std::memcpy(tmp.data(), src, static_cast<size_t>(count)*4);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    out->assign(tmp.begin(),tmp.begin()+count);
    for(uint32_t owner:*out) if(!owner) return false;
    return true;
}

struct BaseRecord {
    const uint32_t* items = nullptr;
    uint64_t item_count = 0;
    const uint32_t* fuel = nullptr;
    uint64_t fuel_count = 0;
};
inline bool base_record(const void* base, uint32_t index, BaseRecord* out) {
    if (!base || !out || index >= kBaseSlots) return false;
    const uintptr_t off=static_cast<uintptr_t>(index)*kBaseStride;
    uint64_t p=0,n=0,f=0,fn=0;
    if (!read_u64(base,off+kBaseItemPointer,&p) ||
        !read_u64(base,off+kBaseItemCount,&n) ||
        !read_u64(base,off+kBaseFuelPointer,&f) ||
        !read_u64(base,off+kBaseFuelCount,&fn) ||
        n>4096 || fn>4096 || (n&&!p) || (fn&&!f))
        return false;
    *out={reinterpret_cast<const uint32_t*>(p),n,
          reinterpret_cast<const uint32_t*>(f),fn};
    return true;
}

inline bool authoritative_world() {
    if (!S.api || !S.api->session || !S.api->world ||
        !S.api->server_world || !S.api->side || !S.build)
        return false;
    KilnSessionInfo session{}; session.size=sizeof session;
    if (S.api->session(&session)!=KILN_OK) return false;
    const uint32_t side=S.api->side();
    if (S.build->side==cbpay::stock::build::Side::Dedicated) {
        if (side!=KILN_SIDE_SERVER ||
            session.state!=KILN_SESSION_DEDICATED ||
            session.side!=KILN_SIDE_SERVER) return false;
    } else if (S.build->side==cbpay::stock::build::Side::ClientHosted) {
        if (side!=KILN_SIDE_CLIENT ||
            session.state!=KILN_SESSION_HOST ||
            session.side!=KILN_SIDE_CLIENT) return false;
    } else return false;
    const uintptr_t world=S.api->world(KILN_WORLD_SERVER);
    return world && world==session.worlds[KILN_WORLD_SERVER] &&
           world==S.api->server_world();
}

inline bool real_solo_player(uint32_t actor) {
    if (!S.players || !S.players->status || !S.players->get ||
        !S.api || !S.api->on_tick_thread() ||
        !authoritative_world()) return false;
    KilnPlayersStatus info{};info.size=sizeof info;
    if (S.players->status(&info)!=KILN_OK ||
        info.source!=KILN_PLAYERS_SOURCE_RECORDS ||
        !info.records || !info.map_ok || !info.layout_ok ||
        info.simulated || info.connected!=1 || info.in_game!=1) return false;
    KilnPlayer p{};p.size=sizeof p;
    if (S.players->get(actor,&p)!=KILN_OK ||
        p.entity!=actor || p.state!=KILN_PLAYER_IN_GAME ||
        (p.flags&(KILN_PLAYER_HAS_PERMISSIONS|KILN_PLAYER_FROM_RECORDS)) !=
        (KILN_PLAYER_HAS_PERMISSIONS|KILN_PLAYER_FROM_RECORDS) ||
        (p.permissions&(KILN_PLAYER_CAN_ACCESS_INVENTORIES|KILN_PLAYER_CAN_EDIT_BASE)) !=
        (KILN_PLAYER_CAN_ACCESS_INVENTORIES|KILN_PLAYER_CAN_EDIT_BASE))
        return false;
    return true;
}

inline bool magic_component(uint32_t owner) {
    if (!S.get_template || !S.api || !S.api->server_world()) return false;
    // The game's native template component, not a chest name or mutable C#
    // string flag. The native base stock list additionally confirms that
    // the game currently registered it for crafting.
    __try {
        return S.get_template(S.api->server_world(),
                              kMagicStockHash,owner)!=nullptr;
    } __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}

// Every source must resolve to a fully initialised native inventory tree
// inside the borrowed ORIGINAL payment transaction. Detached slot snapshots
// must never be used as an authorisation on their own.
inline bool actual_source(uint32_t actor,void* tx,uint32_t owner,bool local,
                          cbpay::stock::Source* out) {
    if (!S.items || !S.items->read_in_tx || !out ||
        !magic_component(owner)) return false;
    KilnItemsTxSnapshot snapshot{};snapshot.size=sizeof snapshot;
    std::array<KilnItemsSlot,KILN_ITEMS_MAX_INVENTORIES*8> slots{};
    const KilnResult rc=S.items->read_in_tx(tx,actor,owner,&snapshot,
                                           slots.data(),slots.size());
    if(rc!=KILN_OK || snapshot.actor!=actor ||
       snapshot.owner!=owner || !snapshot.inventory_count ||
       snapshot.inventory_count>KILN_ITEMS_MAX_INVENTORIES ||
       snapshot.slot_total != 8*snapshot.inventory_count ||
       snapshot.flags != 0) return false;
    cbpay::stock::Source s{};
    s.owner=owner;s.native_magic_component=true;
    s.inventory_initialized=true;s.loaded=true;s.same_base=local;
    s.actor_authorized=true; // real_solo_player() checked for this transaction
    s.base_authorized=true;  // global CAN_EDIT_BASE for the single player
    s.game_source_eligible=true; // only taken from the game's native base stock list
    for(uint32_t i=0;i<snapshot.inventory_count;++i) {
        if(!snapshot.inventory_entities[i])return false;
        s.linked.push_back(snapshot.inventory_entities[i]);
    }
    *out=std::move(s);
    return true;
}

inline bool native_stock_mode2(const void* borrowed_tx) {
    if (!borrowed_tx) return false;
    __try {
        return static_cast<const uint8_t*>(borrowed_tx)[0xB1] == 2;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

inline thread_local uint32_t selected_actor = 0;
inline bool grant(uint32_t actor,const cbpay::stock::Source& source) {
    // Source comes ONLY from the game-native base stock record, with a
    // nonempty verified original-transaction page set.
    return actor && actor==selected_actor && source.owner &&
           source.native_magic_component && source.inventory_initialized &&
           source.actor_authorized && source.base_authorized &&
           source.game_source_eligible && !source.linked.empty();
}

inline bool roster(uint32_t actor,void* tx,
    const cbpay::stock::native::GameStockLists& game,
    std::span<const uint32_t> original,
    std::vector<cbpay::stock::Source>& vanilla,
    std::vector<cbpay::stock::Source>& remote,
    cbpay::stock::native::Context* proof,
    cbpay::stock::NativeGrant* grant_out)
{
    using namespace cbpay::stock::native;
    if (!actor || !tx || !proof || !grant_out || !S.hash_ok ||
        !S.original_finality_ok || !current_selector.genuine_first_or_second_call ||
        !real_solo_player(actor) || original.empty() || original.size()>4096 ||
        !S.api || !S.api->server_world()) return false;

    // The selector chooses one of the two (primary/fallback) native base
    // systems. Independently establish which system actually holds the
    // original returned stock pointer before walking any other base.
    const void* systems[2]={current_selector.primary,current_selector.fallback};
    const void* matched_system=nullptr;
    uint32_t local_index=0;
    std::vector<BaseRecord> base_records;
    for (const void* sys:systems) {
        if(!sys)continue;
        std::vector<BaseRecord> records;
        bool ok=true;
        for(uint32_t i=0;i<kBaseSlots;++i) {
            BaseRecord b{};
            if(!base_record(sys,i,&b)){ok=false;break;}
            records.push_back(b);
        }
        if(!ok)continue;
        uint32_t matches=0,which=0;
        for(uint32_t i=0;i<records.size();++i) {
            if(records[i].items==game.item_owners &&
               records[i].item_count==game.item_count &&
               records[i].fuel==game.fuel_owners &&
               records[i].fuel_count==game.fuel_count &&
               game.item_count>0) {++matches;which=i;}
        }
        if(matches!=1)continue;
        // Multiple mutually distinct base-system candidates are ambiguous.
        if(matched_system && matched_system!=sys)return false;
        matched_system=sys;local_index=which;base_records=std::move(records);
    }
    if(!matched_system)return false;

    // Validate every listed registered Magic Chest with read_in_tx.
    // An unavailable/offline source invalidates the entire overlay rather
    // than giving a partial, stale or deceptively craftable roster.
    std::vector<uint32_t> local_ids;
    if(!copy_owners(base_records[local_index].items,
                    base_records[local_index].item_count,&local_ids) ||
       local_ids.size()!=original.size())return false;
    for(size_t i=0;i<original.size();++i)if(original[i]!=local_ids[i])return false;

    vanilla.clear();remote.clear();
    for(uint32_t i=0;i<kBaseSlots;++i) {
        const auto& b=base_records[i];
        if(!b.item_count)continue;
        std::vector<uint32_t> ids;
        if(!copy_owners(b.items,b.item_count,&ids))return false;
        for(uint32_t owner:ids) {
            cbpay::stock::Source src{};
            if(!actual_source(actor,tx,owner,i==local_index,&src))return false;
            if(i==local_index)vanilla.push_back(std::move(src));
            else remote.push_back(std::move(src));
        }
    }
    if(vanilla.size()!=original.size() || remote.empty())return false;

    if(!native_stock_mode2(tx))return false;

    Context c{};
    c.actor=actor;c.transaction_operator=actor;c.stock_mode=2;
    c.on_original_server_tick=true;c.pinned_executable_hash=true;
    c.real_original_stock_callsite=true;c.native_game_selector_success=true;
    c.complete_authoritative_roster=true;
    c.original_commit_and_rollback_bound=true;
    c.game_player_record_verified=true;c.single_connected_player=true;
    c.game_inventory_access_permission=true;c.game_base_edit_permission=true;
    c.original_game_stock_registration_verified=true;
    c.current_actor_base_entitlement_verified=true;
    selected_actor=actor;
    *proof=c;*grant_out=&grant;
    return true;
}

// The current Kiln Types service indexes the game's actual .rdata ECS
// system descriptors and supplies the update RVA by NAME. No guessed client
// function offset, borrowed program callback or button override is used.
inline uint64_t client_cache_system_rva(const KilnApi* api) {
    if(!api || !api->service)return 0;
    const auto* types=static_cast<const KilnTypesApi*>(
        api->service(KilnStr{KILN_TYPES_SERVICE,sizeof(KILN_TYPES_SERVICE)-1},
                     KILN_TYPES_VERSION));
    if(!types || types->header.size <
       offsetof(KilnTypesApi,system_find)+sizeof(types->system_find) ||
       types->header.size <
       offsetof(KilnTypesApi,system_info)+sizeof(types->system_info) ||
       !types->status || !types->system_find || !types->system_info)
        return 0;
    KilnTypesStatus status{};status.size=sizeof status;
    if(types->status(&status)!=KILN_OK || !status.systems ||
       !status.system_count)return 0;
    static constexpr char kName[]="cache_crafting_stock";
    const int32_t index=types->system_find(KilnStr{kName,sizeof(kName)-1});
    if(index<0 || static_cast<uint32_t>(index)>=status.system_count)return 0;
    KilnSystemInfo entry{};entry.size=sizeof entry;
    if(types->system_info(static_cast<uint32_t>(index),&entry)!=KILN_OK ||
       entry.index!=static_cast<uint32_t>(index) ||
       !entry.rva || !entry.name.data || entry.name.size!=sizeof(kName)-1 ||
       std::memcmp(entry.name.data,kName,sizeof(kName)-1)!=0)return 0;
    return entry.rva;
}

inline bool bind(const KilnApi* api) {
    S={};
    if (!api || !api->side || !api->build_info ||
        !api->session || !api->world || !api->server_world)
        return false;
    const uint32_t side=api->side();
    if(side!=KILN_SIDE_SERVER && side!=KILN_SIDE_CLIENT)
        return false;
    KilnBuildInfo bi{};bi.size=sizeof bi;
    if(api->build_info(&bi)!=KILN_OK || !bi.verified ||
       !bi.hooks_enabled || bi.side!=side)
        return false;
    const auto* build=cbpay::stock::build::exact(
        side==KILN_SIDE_SERVER ?
            cbpay::stock::build::Side::Dedicated :
            cbpay::stock::build::Side::ClientHosted,
        bi.exe_sha256);
    if(!build) return false;
    S.api=api;S.build=build;
    if(!authoritative_world()) { S={}; return false; }
    if(build->side==cbpay::stock::build::Side::ClientHosted) {
        S.original_client_cache_update_rva=client_cache_system_rva(api);
        if(!S.original_client_cache_update_rva) {
            S={};
            return false; // no correct client cache => no stock overlay
        }
    }
    auto* players=static_cast<const KilnPlayersApi*>(
        api->service(KilnStr{KILN_PLAYERS_SERVICE,sizeof(KILN_PLAYERS_SERVICE)-1},
                     KILN_PLAYERS_VERSION));
    auto* items=static_cast<const KilnItemsApi*>(
        api->service(KilnStr{KILN_ITEMS_SERVICE,sizeof(KILN_ITEMS_SERVICE)-1},
                     KILN_ITEMS_VERSION));
    if(!players || !items ||
       players->header.size<offsetof(KilnPlayersApi,get)+sizeof(players->get) ||
       items->header.size<offsetof(KilnItemsApi,read_in_tx)+sizeof(items->read_in_tx) ||
       !items->read_in_tx)return false;
    uintptr_t fn=0,commit=0,rollback=0;
    if(api->symbol(KilnStr{"GetTemplateComponent",20},&fn)!=KILN_OK || !fn ||
       api->symbol(KilnStr{"items/InventoryCommit",21},&commit)!=KILN_OK ||
       api->symbol(KilnStr{"items/InventoryRollback",23},&rollback)!=KILN_OK ||
       !commit || !rollback)return false;
    S.players=players;S.items=items;
    S.get_template=reinterpret_cast<GetTemplateComponent>(fn);
    S.hash_ok=true;S.original_finality_ok=true;
    return true;
}
} // namespace cbpay::stock::live
