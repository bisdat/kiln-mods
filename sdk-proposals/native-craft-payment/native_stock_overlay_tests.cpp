// SPDX-License-Identifier: MIT
// Pure unit fixtures; NO original Enshrouded game process or transactions.
#include "native_stock_overlay.h"
#include <array>
#include <cassert>
#include <cstdio>
#include <functional>
#include <type_traits>
#include <vector>
using namespace cbpay::stock;
using namespace cbpay::stock::native;
using NativeStatus = cbpay::stock::native::Status;

static_assert(!std::is_copy_constructible_v<ScopedItemUnion>);
static_assert(!std::is_move_constructible_v<ScopedItemUnion>);
static constexpr uint32_t Player=1, Local=1412, Nearby=1420, Remote=1081, Ordinary=1035;
// The original base-stock list contains chests only; the native payer
// deducts the player's backpack separately BEFORE consulting these owners.
static std::array<uint32_t,2> local_ids{Local,Nearby};
static std::array<uint32_t,1> fuel_ids{4777};
static bool allow=true;
static int grants=0;
static bool grant(uint32_t actor,const Source& source) {
    ++grants;
    return allow && actor==Player && source.owner!=Ordinary;
}
static Source make(uint32_t id,bool same_base,bool magic=true) {
    Source s;
    s.owner=id; s.linked={id,id+50000};
    s.native_magic_component=magic;
    s.inventory_initialized=s.loaded=s.game_source_eligible=true;
    s.same_base=same_base;
    s.actor_authorized=s.base_authorized=true;
    return s;
}
static Context secure() {
    return {Player,Player,2,true,true,true,true,true,true,true,true,true,true,true,true};
}
static GameStockLists lists() {
    return {local_ids.data(),local_ids.size(),fuel_ids.data(),fuel_ids.size()};
}
static std::vector<Source> original() {
    return {make(Local,true),make(Nearby,true)};
}
static LivePlayerRecord live() {
    return {Player,2,0x18,0x06,1,1,true,true,false};
}
static NativeStatus run(ScopedItemUnion& x,const Context& ctx,
                  GameStockLists game, std::span<const uint32_t> copied,
                  const std::vector<Source>& normal,
                  const std::vector<Source>& extras,
                  NativeGrant callback=&grant) {
    return x.assemble(ctx,game,copied,normal,extras,callback);
}
int main() {
    unsigned tested=0;
    auto test=[&](const char* label, const std::function<void()>& fn) {
        allow=true;grants=0;
        fn();++tested;std::printf("PASS %02u %s\n",tested,label);
    };
    test("verified native base stock-mode2 joins remote owner IDs after local chests",[] {
        ScopedItemUnion x;
        assert(run(x,secure(),lists(),local_ids,original(),{make(Remote,false)})==NativeStatus::Ready);
        GameStockLists result{};
        assert(x.publish(&result));
        assert(result.item_count==3 && result.item_owners[0]==Local &&
               result.item_owners[1]==Nearby && result.item_owners[2]==Remote);
        assert(result.fuel_count==fuel_ids.size() &&
               result.fuel_owners==fuel_ids.data() &&
               result.fuel_owners[0]==fuel_ids[0]);
        assert(x.borrowed_owner_count()==3 && grants==1);
    });
    test("old owner pointer is never overwritten when no extra stock",[] {
        ScopedItemUnion x;
        auto game=lists();
        assert(run(x,secure(),game,local_ids,original(),{})==NativeStatus::NoExtraSources);
        auto before=game;
        assert(!x.publish(&game));
        assert(game.item_owners==before.item_owners && game.item_count==before.item_count);
    });
    test("already vanilla local chest is not counted twice",[] {
        ScopedItemUnion x;
        assert(run(x,secure(),lists(),local_ids,original(),{make(Local,true)})==NativeStatus::NoExtraSources);
        assert(grants==0);
    });
    test("local extra sources precede distant sources without replacing original base chests",[] {
        ScopedItemUnion x;
        auto second=make(2000,true);
        assert(run(x,secure(),lists(),local_ids,original(),{make(Remote,false),second})==NativeStatus::Ready);
        auto game=lists();
        assert(x.publish(&game) && game.item_count==4 &&
               game.item_owners[0]==Local && game.item_owners[1]==Nearby &&
               game.item_owners[2]==2000 && game.item_owners[3]==Remote);
    });
    test("unknown original item owner is a refused input",[] {
        ScopedItemUnion x;
        std::array<uint32_t,2> incorrect{Local,99999};
        assert(run(x,secure(),lists(),incorrect,original(),{make(Remote,false)})
               ==NativeStatus::OriginalMismatch);
        assert(x.borrowed_owner_count()==0);
    });
    test("no original buffer or count mismatch fails before grant",[] {
        ScopedItemUnion x;
        auto g=lists();g.item_owners=nullptr;
        assert(run(x,secure(),g,local_ids,original(),{make(Remote,false)})
               ==NativeStatus::MalformedOriginal);
        g=lists();g.item_count=1;
        assert(run(x,secure(),g,local_ids,original(),{make(Remote,false)})
               ==NativeStatus::MalformedOriginal);
        assert(grants==0);
    });
    test("fuel list pointer missing is invalid and never altered",[] {
        ScopedItemUnion x;
        auto g=lists();g.fuel_owners=nullptr;
        assert(run(x,secure(),g,local_ids,original(),{make(Remote,false)})
               ==NativeStatus::MalformedOriginal);
    });
    test("original getter must have succeeded",[] {
        ScopedItemUnion x;auto p=secure();p.native_game_selector_success=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
    });
    test("original server tick and stock mode 2 mandatory",[] {
        ScopedItemUnion x;auto p=secure();p.stock_mode=1;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
        p=secure();p.on_original_server_tick=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
    });
    test("original caller must match actor and transaction operator",[] {
        ScopedItemUnion x;auto p=secure();p.transaction_operator++;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
    });
    test("unverified executable build cannot call native selector",[] {
        ScopedItemUnion x;auto p=secure();p.pinned_executable_hash=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
    });
    test("requires full native source roster and original rollback",[] {
        ScopedItemUnion x;auto p=secure();p.complete_authoritative_roster=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
        p=secure();p.original_commit_and_rollback_bound=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::UnsafeCallsite);
    });
    test("game player record and solo status are both mandatory",[] {
        ScopedItemUnion x;auto p=secure();p.game_player_record_verified=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::Unauthorised);
        p=secure();p.single_connected_player=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::Unauthorised);
    });
    test("original game base and source permissions are mandatory",[] {
        ScopedItemUnion x;auto p=secure();p.current_actor_base_entitlement_verified=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::Unauthorised);
        p=secure();p.original_game_stock_registration_verified=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::Unauthorised);
    });
    test("disallow ordinary chest despite spare inventory pages",[] {
        ScopedItemUnion x;
        assert(run(x,secure(),lists(),local_ids,original(),{make(Ordinary,false,false)})
               ==NativeStatus::Unauthorised);
        assert(!x.publish(nullptr));
    });
    test("revoke current server grant and refuse any remote extension",[] {
        ScopedItemUnion x;allow=false;
        assert(run(x,secure(),lists(),local_ids,original(),{make(Remote,false)})
               ==NativeStatus::Unauthorised);
        assert(grants==1);
    });
    test("ambiguous linked pages cannot be included in original payment",[] {
        ScopedItemUnion x;
        auto r=make(Remote,false);r.linked[1]=Local+50000;
        assert(run(x,secure(),lists(),local_ids,original(),{r})==NativeStatus::OriginalMismatch);
    });
    test("source with duplicated owner cannot be paid twice",[] {
        ScopedItemUnion x;
        auto r=make(Remote,false);
        assert(run(x,secure(),lists(),local_ids,original(),{r,r})==NativeStatus::OriginalMismatch);
    });
    test("failed subsequent assembly forgets previously owned list",[] {
        ScopedItemUnion x;
        assert(run(x,secure(),lists(),local_ids,original(),{make(Remote,false)})==NativeStatus::Ready);
        auto p=secure();p.real_original_stock_callsite=false;
        assert(run(x,p,lists(),local_ids,original(),{make(Remote,false)})==NativeStatus::UnsafeCallsite);
        assert(x.borrowed_owner_count()==0);
        GameStockLists out{};
        assert(!x.publish(&out));
    });
    test("permission needs a real, unambiguous, one-player game record",[] {
        assert(verified_solo_actor(Player,live()));
        auto p=live();p.source_records=false;
        assert(!verified_solo_actor(Player,p));
        p=live();p.layout_ok=false;
        assert(!verified_solo_actor(Player,p));
        p=live();p.simulated=true;
        assert(!verified_solo_actor(Player,p));
        p=live();p.in_game=2;
        assert(!verified_solo_actor(Player,p));
        p=live();p.connected=2;
        assert(!verified_solo_actor(Player,p));
    });
    test("actor with no inventory or base permission cannot use other chest",[] {
        auto p=live();p.rights=0x04;
        assert(!verified_solo_actor(Player,p));
        p=live();p.rights=0x02;
        assert(!verified_solo_actor(Player,p));
        p=live();p.flags=0x10;
        assert(!verified_solo_actor(Player,p));
        p=live();p.entity=2;
        assert(!verified_solo_actor(Player,p));
        assert(!verified_solo_actor(0,live()));
    });
    test("one source aliased to vanilla owner with mismatched pages is rejected",[] {
        ScopedItemUnion x;
        auto alias=make(Local,true);alias.linked[0]++;
        assert(run(x,secure(),lists(),local_ids,original(),{alias})
               ==NativeStatus::OriginalMismatch);
    });
    test("oversized original list fails without reading out of bounds",[] {
        ScopedItemUnion x;
        auto v=lists();v.item_count=4097;
        assert(run(x,secure(),v,local_ids,original(),{make(Remote,false)})
               ==NativeStatus::MalformedOriginal);
        v=lists();v.fuel_count=4097;
        assert(run(x,secure(),v,local_ids,original(),{make(Remote,false)})
               ==NativeStatus::MalformedOriginal);
    });
    std::printf("PASS: %u scoped original-game stock-list adapter cases. Native collector still inactive.\n",tested);
}
