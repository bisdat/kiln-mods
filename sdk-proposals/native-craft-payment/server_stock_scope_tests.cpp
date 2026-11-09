// SPDX-License-Identifier: MIT
#include "server_stock_scope.h"
#include <array>
#include <cassert>
#include <cstdio>
#include <functional>
#include <type_traits>
using namespace cbpay::stock;
using namespace cbpay::stock::native;
static constexpr uint32_t Player=1, Local=1412, Nearby=1420, Remote=1081;
static std::array<uint32_t,2> originals{Local,Nearby};
static std::array<uint32_t,1> fuel{777};
static bool allow=true,valid=true,deny=false;
static uint32_t calls=0, grants=0;
static Source source(uint32_t id,bool local) {
    Source s{};
    s.owner=id;s.linked={id,id+10000};
    s.native_magic_component=true;
    s.inventory_initialized=true;
    s.loaded=true;s.same_base=local;
    s.actor_authorized=true;s.base_authorized=true;
    s.game_source_eligible=true;
    return s;
}
static Context proof() {
    return {Player,Player,2,true,true,true,true,true,true,true,true,true,true,true,true};
}
static bool grant(uint32_t actor,const Source& s) {
    ++grants;return allow&&actor==Player&&s.owner==Remote;
}
static bool provider(uint32_t actor,void* tx,const GameStockLists& current,
                     std::span<const uint32_t> copied,
                     std::vector<Source>& vanilla,std::vector<Source>& remotes,
                     Context* p,NativeGrant* g) {
    ++calls;
    if(!valid||actor!=Player||!tx||current.item_count!=2||
       copied.size()!=2||copied[0]!=Local||copied[1]!=Nearby)return false;
    vanilla={source(Local,true),source(Nearby,true)};
    if(!deny)remotes.push_back(source(Remote,false));
    *p=proof();*g=&grant;
    return true;
}
static GameStockLists game() {
    return {originals.data(),originals.size(),fuel.data(),fuel.size()};
}
static_assert(!std::is_copy_constructible_v<OriginalPayerScope>);
static_assert(!std::is_move_constructible_v<OriginalPayerScope>);
int main() {
    uint32_t n=0;
    auto test=[&](const char* title,const std::function<void()>& f) {
        valid=allow=true;deny=false;calls=grants=0;f();
        std::printf("PASS %02u %s\n",++n,title);
    };
    test("no active original payer cannot extend game stock",[] {
        assert(OriginalPayerScope::current()==nullptr);
    });
    test("verified game selector appends remote item owner only",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);
        auto r=game(),orig=r;
        assert(scope.after_original_selector(Player,&tx,&r));
        assert(scope.published()==1 && r.item_count==3);
        assert(r.item_owners[0]==Local&&r.item_owners[1]==Nearby&&r.item_owners[2]==Remote);
        assert(r.item_owners!=orig.item_owners && r.fuel_owners==orig.fuel_owners
               && r.fuel_count==orig.fuel_count && r.fuel_owners[0]==777);
        assert(calls==grants && calls==1);
    });
    test("original game stock unchanged on denied native grant",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);allow=false;
        auto r=game(),old=r;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(r.item_owners==old.item_owners && r.item_count==old.item_count);
        assert(r.fuel_owners==old.fuel_owners);
        assert(scope.published()==0);
    });
    test("native provider disabled means original result remains",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,nullptr);
        auto r=game(),old=r;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(r.item_owners==old.item_owners && calls==0);
    });
    test("wrong actor or transaction is refused before provider",[] {
        int tx=0,other=0;OriginalPayerScope scope(Player,&tx,&provider);
        auto r=game(),old=r;
        assert(!scope.after_original_selector(2,&tx,&r));
        assert(!scope.after_original_selector(Player,&other,&r));
        assert(r.item_owners==old.item_owners && calls==0);
    });
    test("failed full native roster refuses without pointer mutation",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);valid=false;
        auto r=game(),old=r;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(r.item_owners==old.item_owners && calls==1);
    });
    test("one original call returns empty extra roster and keeps original",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);deny=true;
        auto r=game(),old=r;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(r.item_owners==old.item_owners);
    });
    test("two independent game selector calls retain both owner vectors",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);
        auto a=game(),b=game();
        assert(scope.after_original_selector(Player,&tx,&a));
        const auto* first=a.item_owners;
        assert(scope.after_original_selector(Player,&tx,&b));
        assert(a.item_owners==first && a.item_owners[2]==Remote &&
               b.item_owners[2]==Remote && a.item_owners!=b.item_owners);
        assert(scope.published()==2 && calls==2);
        auto c=game(),orig=c;
        assert(!scope.after_original_selector(Player,&tx,&c));
        assert(c.item_owners==orig.item_owners && calls==2);
    });
    test("nested original payer scope cannot steal parent game transaction",[] {
        int tx=0,inner=0;
        OriginalPayerScope outer(Player,&tx,&provider);
        {
            OriginalPayerScope nesting(Player,&inner,&provider);
            auto r=game();
            assert(OriginalPayerScope::current()==&outer);
            assert(!nesting.after_original_selector(Player,&inner,&r));
            assert(calls==0);
        }
        auto r=game();
        assert(outer.after_original_selector(Player,&tx,&r) && r.item_count==3);
    });
    test("expired original payer scope revokes TLS lifetime",[] {
        int tx=0;
        {
            OriginalPayerScope scope(Player,&tx,&provider);
            assert(OriginalPayerScope::current()==&scope);
        }
        assert(OriginalPayerScope::current()==nullptr);
    });
    test("malformed original item-owner array rejected before callback",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);
        auto r=game();r.item_owners=nullptr;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(calls==0);
    });
    test("fuel pointer missing rejected without consuming provider",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);
        auto r=game();r.fuel_owners=nullptr;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(calls==0);
    });
    test("zero actor and null transaction refuse original scope",[] {
        int tx=0;
        OriginalPayerScope a(0,&tx,&provider);
        auto r=game();
        assert(!a.after_original_selector(0,&tx,&r));
        OriginalPayerScope b(Player,nullptr,&provider);
        assert(!b.after_original_selector(Player,nullptr,&r));
        assert(calls==0);
    });
    test("bad original live source IDs fail closed",[] {
        int tx=0;OriginalPayerScope scope(Player,&tx,&provider);
        uint32_t ids[2]{Local,12};
        auto r=game();r.item_owners=ids;
        assert(!scope.after_original_selector(Player,&tx,&r));
        assert(r.item_owners==ids);
    });
    std::printf("PASS: %u original payer-scoped selector contract cases; real game provider must be bound before activation.\n",n);
}
