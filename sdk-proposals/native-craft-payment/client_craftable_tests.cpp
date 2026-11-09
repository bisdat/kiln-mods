// SPDX-License-Identifier: MIT
#include "client_craftable.h"
#include <cassert>
#include <cstdio>
#include <functional>
using namespace cbpay::client;

static ServerCapability g_cap{};
static bool g_query_ok = true;
static int g_queries = 0;
static bool query(const Call&, ServerCapability* out) {
    ++g_queries;
    if (!g_query_ok) return false;
    *out = g_cap;
    return true;
}
static ServerCapability complete(uint32_t recipe=0x23c98255, uint32_t count=1) {
    ServerCapability c{};
    c.recipe=recipe;c.approved_count=count;
    c.live_authenticated_server=true;
    c.matching_native_protocol=true;
    c.original_transaction_payment_active=true;
    c.server_has_complete_selected_bill=true;
    c.exact_selection_bound=true;
    c.authoritative_magic_entitlement=true;
    c.authority_epoch_current=true;
    c.no_conflicting_free_craft=true;
    return c;
}
int main() {
    unsigned n=0;
    auto test=[&](const char* s, const std::function<void()>& f){
        g_cap=complete();g_query_ok=true;g_queries=0;
        f();++n; std::printf("PASS %02u %s\n",n,s);
    };
    const Call base{0x23c98255,1,nullptr,0};
    test("no provider cannot override the game's uncraftable answer",[&] {
        assert(craftable(base,nullptr)==0);
    });
    test("live signed server payment capability enables one craft",[&] {
        assert(craftable(base,query)==1 && g_queries==1);
    });
    test("real vanilla craftability stays intact",[&] {
        const Call q{base.recipe,1,nullptr,1};
        assert(craftable(q,query)==1 && g_queries==0);
    });
    test("disabled native server payer cannot enable button",[&] {
        g_cap.original_transaction_payment_active=false;
        assert(craftable(base,query)==0);
    });
    test("incomplete category and alternative bill cannot enable button",[&] {
        g_cap.server_has_complete_selected_bill=false;
        assert(craftable(base,query)==0);
    });
    test("no authoritative Magic Chest entitlement cannot enable button",[&] {
        g_cap.authoritative_magic_entitlement=false;
        assert(craftable(base,query)==0);
    });
    test("no authenticated server cannot enable button",[&] {
        g_cap.live_authenticated_server=false;
        assert(craftable(base,query)==0);
    });
    test("mismatched client and server native protocol cannot enable button",[&] {
        g_cap.matching_native_protocol=false;
        assert(craftable(base,query)==0);
    });
    test("wrong or missing selections cannot enable button",[&] {
        g_cap.exact_selection_bound=false;
        assert(craftable(base,query)==0);
    });
    test("expired server stock generation cannot enable button",[&] {
        g_cap.authority_epoch_current=false;
        assert(craftable(base,query)==0);
    });
    test("conflicting FreeCrafting cannot enable remote button",[&] {
        g_cap.no_conflicting_free_craft=false;
        assert(craftable(base,query)==0);
    });
    test("wrong recipe identity is rejected",[&] {
        g_cap.recipe++;
        assert(craftable(base,query)==0);
    });
    test("provider refuses unknown server capability",[&] {
        g_query_ok=false;
        assert(craftable(base,query)==0);
    });
    test("10 craft cap supports fewer verified crafts",[&] {
        const Call q{base.recipe,10,nullptr,1};
        g_cap.approved_count=3;
        assert(craftable(q,query)==3);
    });
    test("authorisation can never exceed requested cap",[&] {
        g_cap.approved_count=2;
        assert(craftable(base,query)==0);
    });
    test("original partial result is never lowered",[&] {
        const Call q{base.recipe,10,nullptr,4};
        g_cap.approved_count=2;
        assert(craftable(q,query)==4);
    });
    test("invalid game calls do not hit provider",[&] {
        assert(craftable({0,1,nullptr,0},query)==0);
        assert(craftable({base.recipe,0,nullptr,0},query)==0);
        assert(g_queries==0);
    });
    std::printf("PASS: %u fail-closed client UI tests; the production server-backed provider is not yet installed.\n",n);
}
