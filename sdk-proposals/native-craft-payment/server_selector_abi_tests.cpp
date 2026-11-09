// SPDX-License-Identifier: MIT
#include "server_selector_abi.h"
#include <array>
#include <cassert>
#include <cstdio>
#include <functional>
using namespace cbpay::stock;
using namespace cbpay::stock::native;
constexpr uint32_t Actor=1, Local=1412, Near=1420, Remote=1081;
static std::array<uint32_t,2> local{Local,Near};
static std::array<uint32_t,1> fuels{83};
static int original_calls=0,provider_calls=0,grant_calls=0;
static uint8_t return_code=0;
static bool wrong_return=false;
static float pos[3]{12.0f,23.0f,34.0f};
static const float* received_pos=nullptr;
static uint32_t received_player=0;
static SelectorStatus* CBPAY_STOCK_CALL original(
    SelectorStatus* st,GameStockLists* lists,void*,void*,
    const float* position,uint32_t player) {
    ++original_calls;received_pos=position;received_player=player;
    if (st)st->error=return_code;
    if (lists)*lists={local.data(),local.size(),fuels.data(),fuels.size()};
    return wrong_return?nullptr:st;
}
static Source src(uint32_t id,bool local_base) {
    Source s{};s.owner=id;s.linked={id,id+9000};s.native_magic_component=true;
    s.inventory_initialized=s.loaded=s.actor_authorized=true;
    s.base_authorized=s.game_source_eligible=true;s.same_base=local_base;
    return s;
}
static bool grant(uint32_t actor,const Source& s) {
    ++grant_calls;return actor==Actor&&s.owner==Remote;
}
static bool provider(uint32_t actor,void* tx,const GameStockLists& g,
                     std::span<const uint32_t> ids,
                     std::vector<Source>& vanilla,std::vector<Source>& remote,
                     Context* ctx, NativeGrant* gr) {
    ++provider_calls;
    if(actor!=Actor||!tx||g.item_count!=2||ids.size()!=2) return false;
    *ctx={Actor,Actor,2,true,true,true,true,true,true,true,true,true,true,true,true};
    vanilla={src(Local,true),src(Near,true)};
    remote={src(Remote,false)};
    *gr=&grant;return true;
}
int main() {
    uint32_t tests=0;
    auto test=[&](const char* t,const std::function<void()>& f){
        original_calls=provider_calls=grant_calls=0;
        return_code=0;wrong_return=false;received_pos=nullptr;received_player=0;f();
        ++tests;std::printf("PASS %02u %s\n",tests,t);
    };
    test("without active payer always delegates original result untouched",[] {
        SelectorStatus st{};GameStockLists out{};
        auto result=selector_after_original(&original,&st,&out,
            nullptr,nullptr,pos,Actor,Actor,reinterpret_cast<void*>(1),true,true);
        assert(result==&st&&original_calls==1&&provider_calls==0);
        assert(out.item_owners==local.data()&&out.fuel_owners==fuels.data());
        assert(received_pos==pos&&received_player==Actor);
    });
    test("after real selector with active proven payer append only ordinary source",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        SelectorStatus st{};GameStockLists out{};
        auto result=selector_after_original(&original,&st,&out,
            nullptr,nullptr,pos,Actor,Actor,&tx,true,true);
        assert(result==&st&&original_calls==1&&provider_calls==1&&grant_calls==1);
        assert(out.item_count==3&&out.item_owners[0]==Local&&
               out.item_owners[1]==Near&&out.item_owners[2]==Remote);
        assert(out.fuel_owners==fuels.data()&&out.fuel_count==1&&out.fuel_owners[0]==83);
    });
    test("game selector original failure never becomes successful overlay",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        return_code=0x1f;SelectorStatus st{};GameStockLists out{};
        assert(selector_after_original(&original,&st,&out,
            nullptr,nullptr,pos,Actor,Actor,&tx,true,true)==&st);
        assert(st.error==0x1f&&out.item_count==2&&provider_calls==0);
    });
    test("wrong original return object is not wrapped",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        wrong_return=true;SelectorStatus st{};GameStockLists out{};
        assert(selector_after_original(&original,&st,&out,
            nullptr,nullptr,pos,Actor,Actor,&tx,true,true)==nullptr);
        assert(original_calls==1&&provider_calls==0&&out.item_count==2);
    });
    test("unverified original game return address cannot append remote stock",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        SelectorStatus st{};GameStockLists out{};
        selector_after_original(&original,&st,&out,nullptr,nullptr,pos,Actor,Actor,&tx,false,true);
        assert(original_calls==1&&provider_calls==0&&out.item_count==2);
    });
    test("unprobed original borrowed pointers cannot append remote stock",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        SelectorStatus st{};GameStockLists out{};
        selector_after_original(&original,&st,&out,nullptr,nullptr,pos,Actor,Actor,&tx,true,false);
        assert(original_calls==1&&provider_calls==0&&out.item_count==2);
    });
    test("wrong actor or tx ignores extension without harming vanilla",[] {
        int tx=0,wrong=0;OriginalPayerScope payer(Actor,&tx,&provider);
        SelectorStatus st{};GameStockLists out{};
        selector_after_original(&original,&st,&out,nullptr,nullptr,pos,Actor,2,&tx,true,true);
        assert(out.item_count==2&&provider_calls==0);
        selector_after_original(&original,&st,&out,nullptr,nullptr,pos,Actor,Actor,&wrong,true,true);
        assert(out.item_count==2&&provider_calls==0&&original_calls==2);
    });
    test("missing selector function is never made a fake success",[] {
        SelectorStatus st{};GameStockLists out{};
        assert(selector_after_original(nullptr,&st,&out,
            nullptr,nullptr,pos,Actor,Actor,nullptr,true,true)==nullptr);
        assert(original_calls==0);
    });
    test("caller actor and true game player must agree",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        SelectorStatus st{};GameStockLists out{};
        selector_after_original(&original,&st,&out,nullptr,nullptr,pos,2,Actor,&tx,true,true);
        assert(out.item_count==2&&provider_calls==0&&received_player==2);
    });
    test("missing position cannot authorize stock extension",[] {
        int tx=0;OriginalPayerScope payer(Actor,&tx,&provider);
        SelectorStatus st{};GameStockLists out{};
        selector_after_original(&original,&st,&out,nullptr,nullptr,nullptr,Actor,Actor,&tx,true,true);
        assert(out.item_count==2&&provider_calls==0&&received_pos==nullptr);
    });
    std::printf("PASS: %u current server native stock selector ABI seam tests; not a live hook.\n",tests);
}
