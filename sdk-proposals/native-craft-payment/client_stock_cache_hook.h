// SPDX-License-Identifier: MIT
// Cross-base native client stock iterator extension, experiment only.
// Algorithmic approach independently adapted from Enshrouded Building Tools
// (c) 2026 Aleksander Wojtal, MIT:
// https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools
// The original game's client iterator builds the stock totals asynchronously.
// Instead of forging a "craftable" count, pass the union of actual game-
// registered Magic Chest owners into its ORIGINAL iterator for that call.
//
// Safeguards: exact current client hash; cache_crafting_stock ECS system
// entry confirmed by Kiln.Types; unique signatures and two original code
// constraints; single-player HOST only; max 4 client-world caches; bounded
// original chest lists; original call always made; original list restored.
// No client-side material additions via Item.Give/Take, no save mutations.
// NOT YET GAMEPLAY-VERIFIED; this source must not be packaged as a release.
#pragma once

#include "server_live_roster.h"
#include "safetyhook.hpp"
#include <windows.h>
#include <array>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <unordered_set>
#include <vector>

namespace cbpay::stock::clientcache {

using StockIterator = uint8_t* (__fastcall*)(
    uint8_t* result, uint8_t* client_bases, const float* position, uint32_t actor);
using BaseAt = uint8_t* (__fastcall*)(
    uint8_t* result, uint8_t* client_bases, const float* position);

inline SafetyHookInline h_stock;
inline BaseAt g_base_at=nullptr;
inline bool enabled=false;

struct CacheState {
    uint8_t* system=nullptr;
    std::mutex mutex;
    std::vector<uint32_t> owners;
    int here=-2;
    uint32_t base_count=0;
    // Stable address for the vanilla player_ui chest-order check when the
    // current base has no local Magic Chest. Real item totals still come
    // from cache_crafting_stock, and actual payment remains server-native.
    uint32_t menu_first_chest=0;
};
inline std::mutex g_registry_lock;
inline std::array<std::unique_ptr<CacheState>,4> g_registry;

inline CacheState* state_for(uint8_t* sys) {
    if(!sys)return nullptr;
    std::lock_guard<std::mutex> lock(g_registry_lock);
    for(auto& x:g_registry) if(x && x->system==sys)return x.get();
    for(auto& x:g_registry) if(!x) {
        x=std::make_unique<CacheState>();
        x->system=sys;
        return x.get();
    }
    return nullptr; // world churn beyond 4 identities: fail closed
}
// A read-only lookup for the game's per-frame player_ui selector. The UI
// asks thousands of times per second; never allocate or enumerate here.
inline CacheState* known_state(uint8_t* sys) {
    if(!sys) return nullptr;
    std::lock_guard<std::mutex> lock(g_registry_lock);
    for(auto& x:g_registry)if(x && x->system==sys)return x.get();
    return nullptr;
}
// Original menu chooses "backpack only" with zero current-base chests,
// even when its already-cached remote material totals are sufficient.
// Expose ONE previously verified remote chest so the game's menu chooses
// normal chest stock mode; the cache, not this alias, supplies quantities.
// The game server's payer always rechecks ingredients inside InventoryTx.
inline bool menu_chest_mode(uint8_t* sys,uint32_t player,
                            const uint32_t** first_owner) {
    if(!enabled || !sys || !player || !first_owner)return false;
    CacheState* state=known_state(sys);
    if(!state)return false;
    std::lock_guard<std::mutex> lock(state->mutex);
    if(!state->menu_first_chest || state->owners.empty())return false;
    *first_owner=&state->menu_first_chest;
    return true;
}

inline bool read32(const void* p, uintptr_t off, uint32_t* out) {
    if(!p || !out)return false;
    __try {
        std::memcpy(out,static_cast<const uint8_t*>(p)+off,sizeof *out);
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
inline bool write_original_base_list(
    uint8_t* base_record,const uint32_t* owner_ids,uint64_t count,
    uint64_t* previous_ptr,uint64_t* previous_count) {
    if(!base_record || !previous_ptr || !previous_count)return false;
    __try {
        uint64_t old_ptr=0,old_count=0;
        std::memcpy(&old_ptr,base_record,8);
        std::memcpy(&old_count,base_record+8,8);
        const uint64_t ptr=reinterpret_cast<uint64_t>(owner_ids);
        // First publish the valid owner buffer, then its new count.
        std::memcpy(base_record,&ptr,8);
        std::memcpy(base_record+8,&count,8);
        *previous_ptr=old_ptr;
        *previous_count=old_count;
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
inline void restore_original_base_list(
    uint8_t* base_record,uint64_t previous_ptr,uint64_t previous_count) {
    if(!base_record)return;
    __try {
        // First restore the old count (always <= original bounds), then its ptr.
        std::memcpy(base_record+8,&previous_count,8);
        std::memcpy(base_record,&previous_ptr,8);
    } __except(EXCEPTION_EXECUTE_HANDLER){}
}

struct OriginalRecordRestore {
    uint8_t* rec=nullptr;
    uint64_t ptr=0,count=0;
    OriginalRecordRestore(uint8_t* r,const uint32_t* list,uint64_t n)
        : rec(r) {
        if(!write_original_base_list(rec,list,n,&ptr,&count))rec=nullptr;
    }
    ~OriginalRecordRestore(){if(rec)restore_original_base_list(rec,ptr,count);}
    OriginalRecordRestore(const OriginalRecordRestore&)=delete;
    OriginalRecordRestore& operator=(const OriginalRecordRestore&)=delete;
    bool valid() const {return rec!=nullptr;}
};

// Read registered game stock lists ONLY; do not count arbitrary containers or
// invent Magic status from names. The game's original client cache iterator
// still resolves loaded inventory pages and their contents itself.
inline bool collect(uint8_t* sys,uint32_t count,int first,
                    std::vector<uint32_t>* ids) {
    if(!ids || !count || count>16 || first<0 ||
       static_cast<uint32_t>(first)>=count)return false;
    std::vector<uint32_t> result;
    std::unordered_set<uint32_t> visited;
    for(uint32_t step=0;step<count;++step) {
        uint32_t b=step==0?static_cast<uint32_t>(first):
            (step<=static_cast<uint32_t>(first)?step-1:step);
        if(b>=count)return false;
        const uint8_t* rec=sys+0x30+static_cast<uintptr_t>(b)*0x130;
        uint64_t pointer=0,n=0;
        if(!cbpay::stock::live::read_u64(rec,0,&pointer) ||
           !cbpay::stock::live::read_u64(rec,8,&n) || n>4096 ||
           (n && !pointer))return false;
        std::vector<uint32_t> chunk;
        if(!cbpay::stock::live::copy_owners(
             reinterpret_cast<const uint32_t*>(pointer),n,&chunk))
            return false;
        for(uint32_t owner:chunk) {
            if(!owner || !visited.insert(owner).second)return false;
            if(result.size()>=4096)return false;
            result.push_back(owner);
        }
    }
    *ids=std::move(result);
    return true;
}

// The original game BaseAt decides which base contains this position.
// First functional slice requires an actual base to be selected; outside-
// base crafting and an altar with no local Magic Chest still need a verified
// menu-side stock-order path before general release.
inline int original_base_at(uint8_t* sys,const float* pos,uint32_t bases) {
    if(!g_base_at || !pos)return -1;
    uint8_t reply[16]{};
    const uint8_t* returned=g_base_at(reply,sys,pos);
    if(returned!=reply || reply[0]!=0 || reply[8]>=bases)return -1;
    return reply[8];
}

inline uint8_t* __fastcall hk_client_stock(
    uint8_t* result,uint8_t* sys,const float* pos,uint32_t actor) {
    const auto original=[&]() -> uint8_t* {
        return h_stock.call<uint8_t*>(result,sys,pos,actor);
    };
    if(!enabled || !result || !sys || !pos || !actor ||
       !cbpay::stock::live::S.build ||
       cbpay::stock::live::S.build->side!=
           cbpay::stock::build::Side::ClientHosted ||
       !cbpay::stock::live::authoritative_world())
        return original();
    CacheState* state=state_for(sys);
    uint32_t bases=0,part1=0,part2=0;
    if(!state || !read32(sys,0x24,&bases) || !bases || bases>16 ||
       !read32(sys,0x16c8,&part1) || !read32(sys,0x16cc,&part2))
        return original();

    std::lock_guard<std::mutex> lock(state->mutex);
    const int here=original_base_at(sys,pos,bases);
    if(here<0)return original();
    if((part1==0 && part2==0) || here!=state->here ||
       bases!=state->base_count || state->owners.empty()) {
        std::vector<uint32_t> new_owners;
        if(!collect(sys,bases,here,&new_owners))return original();
        state->owners=std::move(new_owners);
        state->menu_first_chest=state->owners.empty()?0:state->owners.front();
        state->here=here;
        state->base_count=bases;
    }
    uint8_t* rec=sys+0x30+static_cast<uintptr_t>(here)*0x130;
    uint64_t n_local=0;
    if(!cbpay::stock::live::read_u64(rec,8,&n_local) || n_local>4096 ||
       state->owners.size()<=n_local)
        return original();

    // This borrowed pointer is visible to the original game ONLY during
    // its own stock iterator; the vector never reallocates during the call.
    OriginalRecordRestore restore(rec,state->owners.data(),state->owners.size());
    if(!restore.valid())return original();
    return original();
}

// The source prologues and offsets below are grounded in the working MIT
// implementation above. Require exact one-match PE text signatures AND
// an original direct E8 call from the game's named cache_crafting_stock
// system to the matched client iterator. Any mismatch leaves vanilla intact.
struct CodeRange {
    const uint8_t* start=nullptr;
    size_t length=0;
};
inline bool text_range(CodeRange* out) {
    if(!out)return false;
    HMODULE image=GetModuleHandleW(nullptr);
    if(!image)return false;
    __try {
        const auto* base=reinterpret_cast<const uint8_t*>(image);
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if(dos->e_magic!=IMAGE_DOS_SIGNATURE || dos->e_lfanew<0 ||
           dos->e_lfanew>0x1000)return false;
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(
            base+dos->e_lfanew);
        if(nt->Signature!=IMAGE_NT_SIGNATURE ||
           nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC)return false;
        const IMAGE_SECTION_HEADER* sections=IMAGE_FIRST_SECTION(nt);
        for(uint16_t i=0;i<nt->FileHeader.NumberOfSections && i<96;++i) {
            if(std::memcmp(sections[i].Name,".text",5)==0 &&
               sections[i].VirtualAddress && sections[i].Misc.VirtualSize>4096 &&
               sections[i].Misc.VirtualSize<0x20000000u) {
                *out={base+sections[i].VirtualAddress,
                      sections[i].Misc.VirtualSize};
                return true;
            }
        }
    } __except(EXCEPTION_EXECUTE_HANDLER) {return false;}
    return false;
}
inline const uint8_t* unique_text(CodeRange text,
                                  const uint8_t* pattern,size_t n) {
    if(!text.start || !pattern || !n || text.length<n)return nullptr;
    const uint8_t* found=nullptr;
    for(size_t i=0;i+n<=text.length;++i) {
        if(text.start[i]!=pattern[0])continue;
        if(std::memcmp(text.start+i,pattern,n)!=0)continue;
        if(found)return nullptr; // ambiguous signature
        found=text.start+i;
    }
    return found;
}
inline bool e8_targets(const uint8_t* call,const uint8_t* target) {
    if(!call || !target || call[0]!=0xE8)return false;
    int32_t diff=0;
    std::memcpy(&diff,call+1,4);
    return call+5+diff==target;
}
inline bool cache_calls(CodeRange text,const uint8_t* cache_fn,
                        const uint8_t* stock_fn) {
    if(!text.start || !cache_fn || !stock_fn ||
       cache_fn<text.start || cache_fn>=text.start+text.length)return false;
    // No unbounded scan over unrelated game code. Strict 8192 bytes;
    // the original cache update must have a direct E8 to this iterator.
    const size_t available=static_cast<size_t>(text.start+text.length-cache_fn);
    const size_t n=std::min<size_t>(available,8192);
    unsigned count=0;
    for(size_t i=0;i+5<=n;++i)
        if(cache_fn[i]==0xE8 && e8_targets(cache_fn+i,stock_fn))
            ++count;
    return count==1;
}

inline bool install(const KilnApi* api) {
    enabled=false;
    if(!api || !api->side || api->side()!=KILN_SIDE_CLIENT ||
       !cbpay::stock::live::S.build ||
       cbpay::stock::live::S.build!=
           &cbpay::stock::build::kClientHosted ||
       !cbpay::stock::live::S.original_client_cache_update_rva ||
       !cbpay::stock::live::authoritative_world())
        return false;
    CodeRange text{};
    if(!text_range(&text))return false;
    static constexpr uint8_t kStock[]={
        0x48,0x89,0x4C,0x24,0x08,0x53,0x55,0x56,0x57,
        0x48,0x83,0xEC,0x58,0x8B,0x82,0xD4,0x16,0,0,
        0x49,0x8B,0xD8,0x2B,0x82,0xD0,0x16,0,0
    };
    static constexpr uint8_t kBaseAt[]={
        0x48,0x89,0x5C,0x24,0x08,0x57,0x48,0x83,0xEC,
        0x10,0x8B,0x7A,0x24,0x45,0x33,0xDB,
        0x48,0x8B,0xDA,0x4C,0x8B,0xD1,0x48,0x85,0xFF
    };
    const uint8_t* stock=unique_text(text,kStock,sizeof kStock);
    const uint8_t* base_at=unique_text(text,kBaseAt,sizeof kBaseAt);
    const uint8_t* image=reinterpret_cast<const uint8_t*>(GetModuleHandleW(nullptr));
    const uint8_t* cache=image+
        cbpay::stock::live::S.original_client_cache_update_rva;
    if(!stock || !base_at || !cache_calls(text,cache,stock))
        return false;
    // Source reverse engineering verifies the iterator asks BaseAt here,
    // and reads one 0x130-sized game base record here. Do not infer layout
    // merely from a type name or a stale RVA.
    static constexpr uint8_t kRecord[]={
        0x48,0x69,0xC8,0x30,0x01,0x00,0x00,
        0x4C,0x8B,0x44,0x31,0x38,0x4C,0x8B,0x54,0x31,0x30
    };
    if(!e8_targets(stock+0xEF,base_at) ||
       std::memcmp(stock+0x12A,kRecord,sizeof kRecord)!=0)
        return false;
    g_base_at=reinterpret_cast<BaseAt>(const_cast<uint8_t*>(base_at));
    h_stock=safetyhook::create_inline(
        const_cast<uint8_t*>(stock),
        reinterpret_cast<void*>(&hk_client_stock));
    if(!h_stock){g_base_at=nullptr;return false;}
    enabled=true;
    return true;
}
} // namespace cbpay::stock::clientcache
