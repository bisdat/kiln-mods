// SPDX-License-Identifier: MIT
// Tests of ordered original crafting stock: NO inventory reads, writes or hooks.
#include "stock_sources.h"
#include <cassert>
#include <cstdio>
#include <functional>
#include <vector>

using namespace cbpay::stock;
static constexpr uint32_t Player=1, Local=1412, Remote=1081, Ordinary=1035;
static bool permit=true;
static unsigned calls=0;
static bool grant(uint32_t actor,const Source& s) {
    ++calls;
    return permit && actor==Player && s.owner!=Ordinary;
}
static Source source(uint32_t owner, bool local, bool magic=true) {
    Source s;
    s.owner=owner;s.linked={owner, owner+10000};s.native_magic_component=magic;
    s.inventory_initialized=true;s.loaded=true;s.same_base=local;
    s.actor_authorized=true;s.base_authorized=true;s.game_source_eligible=true;
    return s;
}
static Result merge(std::vector<Source> vanilla, std::vector<Source> extra) {
    return extend(Player,vanilla,extra,&grant,true,true);
}
int main() {
    int passed=0;
    auto test=[&](const char* label,const std::function<void()>& f){
        permit=true;calls=0;f();++passed;std::printf("PASS %02d %s\n",passed,label);
    };
    test("no remote sources preserves original player and local ordering",[] {
        auto r=merge({source(Player,true),source(Local,true)},{});
        assert(r && r.ordered.size()==2 && r.ordered[0].owner==Player &&
               r.ordered[1].owner==Local && !r.had_remote && calls==0);
    });
    test("remote chest appended after vanilla player and local chest",[] {
        auto r=merge({source(Player,true),source(Local,true)},{source(Remote,false)});
        assert(r && r.ordered.size()==3 && r.ordered[2].owner==Remote &&
               r.had_remote && calls==1);
    });
    test("unsorted extra candidates place same-base ahead of remote",[] {
        auto r=merge({source(Player,true)},{source(Remote,false),source(Local,true)});
        assert(r && r.ordered.size()==3 && r.ordered[1].owner==Local &&
               r.ordered[2].owner==Remote && r.had_remote);
    });
    test("duplicate source from real vanilla stock is not counted again",[] {
        auto r=merge({source(Player,true),source(Local,true)},
                     {source(Remote,false),source(Local,true)});
        assert(r && r.ordered.size()==3 && r.ordered[2].owner==Remote &&
               calls==1);
    });
    test("overlap with changed linked inventory identity is rejected",[] {
        auto fake=source(Local,true);fake.linked[1]++;
        auto r=merge({source(Player,true),source(Local,true)},{fake});
        assert(!r && r.status==Status::AmbiguousLinks && r.ordered.empty());
    });
    test("duplicate extra owner rejected rather than double-counted",[] {
        auto r=merge({source(Player,true)},{source(Remote,false),source(Remote,false)});
        assert(!r && r.status==Status::AmbiguousLinks);
    });
    test("two different owners cannot share one underlying eight-slot page",[] {
        auto local=source(Local,true),remote=source(Remote,false);
        remote.linked[1]=local.linked[1];
        auto r=merge({source(Player,true),local},{remote});
        assert(!r && r.status==Status::AmbiguousLinks);
    });
    test("ordinary non-magic chest cannot join remote sources",[] {
        auto r=merge({source(Player,true)},{source(Ordinary,false,false)});
        assert(!r && r.status==Status::Unauthorised && r.ordered.empty());
    });
    test("Magic without actor permission is denied",[] {
        auto remote=source(Remote,false);remote.actor_authorized=false;
        auto r=merge({source(Player,true)},{remote});
        assert(!r && r.status==Status::Unauthorised);
    });
    test("Magic without base permission is denied",[] {
        auto remote=source(Remote,false);remote.base_authorized=false;
        auto r=merge({source(Player,true)},{remote});
        assert(!r && r.status==Status::Unauthorised);
    });
    test("revoked native game grant is denied",[] {
        permit=false;
        auto r=merge({source(Player,true)},{source(Remote,false)});
        assert(!r && r.status==Status::Unauthorised && calls==1);
    });
    test("unloaded remote source cannot become phantom stock",[] {
        auto remote=source(Remote,false);remote.loaded=false;
        auto r=merge({source(Player,true)},{remote});
        assert(!r && r.status==Status::Malformed);
    });
    test("uninitialised source has no usable stock",[] {
        auto remote=source(Remote,false);remote.inventory_initialized=false;
        auto r=merge({source(Player,true)},{remote});
        assert(!r && r.status==Status::Malformed);
    });
    test("malformed, missing and duplicated inventory page refused",[] {
        auto remote=source(Remote,false);remote.linked={Remote,0};
        assert(merge({source(Player,true)},{remote}).status==Status::AmbiguousLinks);
        remote.linked={Remote,Remote};
        assert(merge({source(Player,true)},{remote}).status==Status::AmbiguousLinks);
        remote.linked.clear();
        assert(merge({source(Player,true)},{remote}).status==Status::Malformed);
    });
    test("unbounded linked inventory fan-out refused",[] {
        auto remote=source(Remote,false);remote.linked.clear();
        for(uint32_t i=0;i<33;++i)remote.linked.push_back(Remote+i*1000);
        auto r=merge({source(Player,true)},{remote});
        assert(!r && r.status==Status::Malformed);
    });
    test("original collector must be bound and complete",[] {
        auto p=source(Player,true),r=source(Remote,false);
        std::vector<Source> v={p},ex={r};
        assert(extend(Player,v,ex,&grant,false,true).status==Status::Incomplete);
        assert(extend(Player,v,ex,&grant,true,false).status==Status::Incomplete);
        assert(extend(Player,v,ex,nullptr,true,true).status==Status::Incomplete);
        assert(extend(0,v,ex,&grant,true,true).status==Status::Incomplete);
        assert(calls==0);
    });
    test("source flagged not eligible by the native game collector refused",[] {
        auto remote=source(Remote,false);remote.game_source_eligible=false;
        auto r=merge({source(Player,true)},{remote});
        assert(!r && r.status==Status::Malformed);
    });
    test("vanilla source identity is checked just like appended identities",[] {
        auto original=source(Player,true);original.linked={Player,Player};
        auto r=merge({original},{source(Remote,false)});
        assert(!r && r.status==Status::AmbiguousLinks);
    });
    test("more than 4096 sources rejected with no partial allocation",[] {
        std::vector<Source> v(4097,source(Player,true));
        auto r=merge(v,{});
        assert(!r && r.status==Status::TooMany && r.ordered.empty());
    });
    std::printf("PASS: %d shared original-game stock eligibility tests; engine-side source enumeration is not installed.\n",passed);
}
