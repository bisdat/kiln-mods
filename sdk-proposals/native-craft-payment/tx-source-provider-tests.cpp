// SPDX-License-Identifier: MIT
// Host fixtures for native ORIGINAL-transaction inventory source snapshot.
// NO real game inventory, grant, database or transaction is touched.
#include "tx-source-provider.h"
#include <algorithm>
#include <cassert>
#include <cstdio>
#include <functional>
#include <map>
#include <vector>

using namespace cbpay;
using namespace cbpay::sources;
constexpr uint32_t A=1, MAGIC=1412, DISTANT=1081, ORDINARY=1900, ITEM=0x18feb214;
constexpr uint64_t slot(uint32_t inv,uint32_t at) {return inv | (uint64_t(at)<<32);}
OwnerView owner(uint32_t actor,uint32_t id,std::vector<uint32_t> links,
                uint32_t item=ITEM,uint32_t count=7) {
    OwnerView v{};v.actor=actor;v.owner=id;v.inventory_ids=links;
    for(auto link:links)for(uint32_t i=0;i<8;++i)
        v.slots.push_back({slot(link,i),i==0?item:0,i==0?count:0});
    return v;
}
static std::map<uint32_t,OwnerView> views;
static std::map<uint32_t,Grant> rights;
static int reads=0,grants=0;
static Stock collect_from(std::vector<uint32_t> owners) {
    reads=grants=0;
    return collect(A,owners,[&](uint32_t actor,uint32_t id,OwnerView* out) {
        ++reads;
        auto it=views.find(id);
        if(it==views.end())return false;
        *out=it->second;
        return actor==A;
    },[&](uint32_t actor,uint32_t id) {
        ++grants;assert(actor==A);
        auto it=rights.find(id);
        return it==rights.end()?Grant{}:it->second;
    });
}
static void reset() {
    views={{A,owner(A,A,{200},ITEM,3)},
           {MAGIC,owner(A,MAGIC,{1412,1421,1422},ITEM,4)},
           {DISTANT,owner(A,DISTANT,{1081},ITEM,5)},
           {ORDINARY,owner(A,ORDINARY,{1900},ITEM,9)}};
    rights={{MAGIC,{true,true,true,true}},
            {DISTANT,{true,true,true,false}},
            {ORDINARY,{false,true,true,false}}};
}
int main(){
    int passed=0;
    auto test=[&](const char* title,const std::function<void()>& f){
        reset();f();++passed;std::printf("PASS %02d %s\n",passed,title);
    };
    test("player local + verified Magic local and distant preserve actual slots",[] {
        const auto r=collect_from({A,MAGIC,DISTANT});
        assert(r.status==Result::Ready && r.owner_count==3 && r.entries.size()==5);
        assert(r.entries[0].owner==A && r.entries[0].slot==slot(200,0));
        assert(r.entries[1].owner==MAGIC && r.entries[1].slot==slot(1412,0));
        assert(r.entries[2].slot==slot(1421,0) && r.entries[3].slot==slot(1422,0));
        assert(r.entries[4].owner==DISTANT && !r.entries[4].same_base);
    });
    test("ordinary chest is not read even if it has raw inventory data",[] {
        const auto r=collect_from({A,ORDINARY});
        assert(r.status==Result::Ready && r.owner_count==1 &&
            r.skipped_unauthorised==1 && reads==1 && r.entries.size()==1);
    });
    test("missing Magic proof prevents any remote disclosure",[] {
        rights[MAGIC].native_magic=false;
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::Ready && r.skipped_unauthorised==1 &&
            reads==1 && r.entries.size()==1);
    });
    test("missing player grant prevents remote read",[] {
        rights[MAGIC].player_access=false;
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::Ready && reads==1 && r.entries.size()==1);
    });
    test("missing Flame Altar/base entitlement prevents remote read",[] {
        rights[DISTANT].base_access=false;
        const auto r=collect_from({A,DISTANT});
        assert(r.status==Result::Ready && reads==1 && r.entries.size()==1);
    });
    test("no remote owner requires no native grant and preserves local stock",[] {
        const auto r=collect_from({A});
        assert(r.status==Result::Ready && r.entries.size()==1 && !grants &&
               !r.entries[0].magic_verified && r.entries[0].permission_verified);
    });
    test("missing player owner rejects entire snapshot",[] {
        const auto r=collect_from({MAGIC});
        assert(r.status==Result::InvalidOwners && reads==0 && r.entries.empty());
    });
    test("duplicate owner IDs reject before native reader",[] {
        const auto r=collect_from({A,MAGIC,A});
        assert(r.status==Result::InvalidOwners && reads==0 && r.entries.empty());
    });
    test("empty owner list and actor zero reject",[] {
        assert(collect_from({}).status==Result::InvalidOwners);
        const std::vector<uint32_t> owners{A};
        auto r=collect(0,owners,
                       [](auto,auto,auto*){return true;},
                       [](auto,auto){return Grant{};});
        assert(r.status==Result::InvalidActor && r.entries.empty());
    });
    test("mismatched transaction operator cannot authorise stock",[] {
        views[MAGIC].actor=1000;
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("truncated owner inventory links and pages reject every source",[] {
        views[MAGIC].slots.pop_back();
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("malformed packed 8-slot page identity rejects all",[] {
        views[MAGIC].slots[0].id=slot(65535,0);
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("invalid high slot index or extra highbits rejected",[] {
        views[MAGIC].slots[2].id=slot(1412,8);
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("same underlying inventory ID shared across two owners rejected",[] {
        views[DISTANT]=owner(A,DISTANT,{1421});
        const auto r=collect_from({A,MAGIC,DISTANT});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("duplicates of linked pages under same owner rejected",[] {
        views[MAGIC]=owner(A,MAGIC,{1412,1412});
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("zero item but nonzero quantity and vice versa rejected",[] {
        views[MAGIC].slots[0].item=0;
        auto r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
        reset();
        views[MAGIC].slots[0].units=0;
        r=collect_from({A,MAGIC});
        assert(r.status==Result::InvalidSnapshot && r.entries.empty());
    });
    test("native reader failure is fatal and suppresses previous player results",[] {
        views.erase(MAGIC);
        const auto r=collect_from({A,MAGIC});
        assert(r.status==Result::NativeReadFailed && r.entries.empty());
    });
    test("multiple sources allow planner but never turn slots into permissions",[] {
        const auto r=collect_from({A,MAGIC,DISTANT});
        const Plan p=make_plan(A,std::vector<Requirement>{{ITEM,10}},r.entries);
        assert(r.status==Result::Ready && p && p.takes.size()==3);
        assert(p.takes[0].owner==A && p.takes[1].owner==MAGIC);
    });
    test("revoked rights make previously seen inventory unable to pay",[] {
        const auto first=collect_from({A,MAGIC});
        assert(first.entries.size()==4);
        rights[MAGIC].player_access=false;
        const auto second=collect_from({A,MAGIC});
        const Plan p=make_plan(A,std::vector<Requirement>{{ITEM,5}},second.entries);
        assert(second.entries.size()==1 && !p && p.takes.empty());
    });
    std::printf("PASS: %d exact original-tx owner/link/slot source tests; native permissions still external.\n",passed);
}
