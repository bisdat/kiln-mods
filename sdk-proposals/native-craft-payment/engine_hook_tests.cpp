// SPDX-License-Identifier: MIT
// Host simulation of the exact native Kiln Rules hook interface. Verifies
// that BOTH payer variants can use the original recipe transaction when a
// trusted native resolver supplies ALL proofs, and that normal Kiln's null
// resolver does not even invoke a game item removal.
#include "engine_hook.h"

#include <array>
#include <cassert>
#include <cstdio>
#include <cstring>
#include <functional>
#include <map>
#include <vector>

using namespace cbpay;
using namespace cbpay::engine;
constexpr uint32_t Player = 1, Local = 1412, Distant = 1081, String = 0x18feb214;
constexpr uintptr_t RecipePayReturn = 0x55aa55a0;
constexpr uintptr_t AltRecipePayReturn = RecipePayReturn + 7;
constexpr uint64_t Slot(uint32_t inventory, uint32_t idx) {
    return uint64_t(inventory) | (uint64_t(idx) << 32);
}
static Source stock(uint32_t owner, uint32_t inv, uint32_t idx, uint32_t units, bool same=false) {
    return {owner, Slot(inv,idx), String, units, true,true,true,true,same};
}
static RecipePaymentProof proof() { return {true,true,true,true,true,true,true}; }

struct Tx {
    std::array<uint8_t, 0x120> bytes{};
    std::vector<std::array<uint8_t, 0x34>> journal;
    std::map<uint64_t,uint32_t> persisted;
    std::map<uint64_t,uint32_t> current;
    int attempts=0, fail_at=0;
    bool output=false, committed=false, rolled_back=false;
    explicit Tx(std::vector<Source> sources) {
        journal.reserve(32);
        const uint32_t actor=Player;
        std::memcpy(bytes.data()+0xe0,&actor,4);
        for(const auto& x:sources) persisted[x.slot]=x.units;
        current=persisted;
        update();
    }
    void update() {
        const auto* p=reinterpret_cast<const uint8_t*>(journal.data());
        const auto n=uint64_t(journal.size());
        std::memcpy(bytes.data()+0xf0,&p,sizeof p);
        std::memcpy(bytes.data()+0xf8,&n,sizeof n);
    }
    void append(uint64_t slot, uint32_t item, uint32_t units) {
        std::array<uint8_t,0x34> op{};
        const auto count=static_cast<uint16_t>(units);
        std::memcpy(op.data(),&slot,8);
        std::memcpy(op.data()+8,&item,4);
        std::memcpy(op.data()+0x24,&count,2);
        op[0x26]=1;
        journal.push_back(op);
        update();
    }
    void rollback() {
        assert(!committed);
        current=persisted;
        output=false;
        journal.clear();
        rolled_back=true;
        update();
    }
    void commit() {
        assert(!rolled_back);
        persisted=current;
        committed=true;
    }
};
static Tx* tx_active=nullptr;
static NativeResult12* CBPAY_CALL remove_game(
    NativeResult12* out, void* raw, uint32_t owner, uint32_t item,
    uint64_t preferred, uint32_t units) {
    assert(tx_active && raw==tx_active->bytes.data() && owner && item);
    auto& tx=*tx_active;
    ++tx.attempts;
    if(tx.fail_at==tx.attempts) {
        out->error=0x15;out->still_missing=units;
        return out;
    }
    auto it=tx.current.find(preferred);
    if(it==tx.current.end() || it->second<units) {
        out->still_missing=units;
        return out;
    }
    it->second-=units;
    tx.append(preferred,item,units);
    return out;
}
static std::vector<Source> g_sources;
static std::vector<Requirement> g_cost;
static bool g_allow=true,g_invalid_proof=false,g_invalid_failure=false;
static bool g_bad_payer_row=false,g_incomplete_selection=false;
static int g_resolves=0,g_grants=0;
static bool native_grant(const Call&, const Source& source, uint32_t actor) {
    ++g_grants;
    return g_allow && actor==Player && source.loaded;
}
static bool trusted_resolve(const Call& call, Resolved* out) {
    ++g_resolves;
    const auto ret=call.payer==Payer::InventoryPay?RecipePayReturn:AltRecipePayReturn;
    if(call.return_address!=ret || call.owner!=Player || call.multiplier!=1)
        return false;
    out->proof=proof();
    if(g_invalid_proof) out->proof.original_rollback=false;
    out->cost_proof={true,true,true,true,true};
    if (g_incomplete_selection) out->cost_proof.complete_input_enumeration=false;
    out->original_payer_rows.clear();
    out->selected_ingredients.clear();
    for (size_t i=0; i<g_cost.size(); ++i) {
        const auto& cost=g_cost[i];
        out->original_payer_rows.push_back({cost.item,cost.count});
        out->selected_ingredients.push_back({
            static_cast<uint32_t>(i),cost.item,cost.count,
            cost.category?costs::IngredientKind::Category:costs::IngredientKind::Exact,
            false // actual native category selection is NOT proven by this fixture
        });
    }
    if (g_bad_payer_row && !out->original_payer_rows.empty())
        out->original_payer_rows[0].count += 1;
    out->eligible_slots=g_sources;
    out->original_remove_items=&remove_game;
    out->grant=&native_grant;
    out->verified_failure_result=4; // synthetic host return, NOT a game-validated code
    out->failure_result_verified=!g_invalid_failure;
    return true;
}
static void setup(std::vector<Source> sources, std::vector<Requirement> needs) {
    g_sources=std::move(sources);
    g_cost=std::move(needs);
    g_allow=true;
    g_invalid_proof=g_invalid_failure=false;
    g_bad_payer_row=g_incomplete_selection=false;
    g_resolves=g_grants=0;
}
static Decision run(Tx& tx, Payer payer=Payer::InventoryPay,
                    ResolveFn resolver=&trusted_resolve) {
    assert(!tx_active);
    tx_active=&tx;
    const int64_t fake_costs[2]={1,1};
    const Call call{tx.bytes.data(),fake_costs,Player,1,nullptr,
        payer==Payer::InventoryPay?RecipePayReturn:AltRecipePayReturn,payer};
    const Decision result=dispatch(call,resolver);
    tx_active=nullptr;
    return result;
}
int main() {
    unsigned passed=0;
    auto check=[&](const char* name,const std::function<void()>& fn) {
        fn(); ++passed;std::printf("PASS %02u %s\n",passed,name);
    };
    const auto s=std::vector<Source>{stock(Player,200,0,3),
        stock(Local,1421,0,4,true),stock(Distant,1081,0,5)};
    check("normal released Kiln has NO provider, no reads and no payment writes", [&] {
        setup(s,{{String,10}}); Tx tx(s);
        const auto r=run(tx,Payer::InventoryPay,nullptr);
        assert(r.action==RecipePaymentAction::DelegateVanilla);
        assert(tx.attempts==0 && g_resolves==0 && tx.journal.empty());
    });
    check("original InventoryPay hook stages true original transaction slots", [&] {
        setup(s,{{String,10}}); Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::PaidInOriginalTx);
        assert(r.original_payer_result==0 && r.remote_takes==2);
        assert(tx.attempts==3 && tx.journal.size()==3);
        assert(tx.current[Slot(200,0)]==0 && tx.current[Slot(1421,0)]==0 &&
               tx.current[Slot(1081,0)]==2 && !tx.committed);
        tx.output=true;tx.commit();
        assert(tx.committed && tx.persisted[Slot(1081,0)]==2);
    });
    check("original RecipePayAlt hook stages same authorised full recipe", [&] {
        setup(s,{{String,10}}); Tx tx(s);
        const auto r=run(tx,Payer::RecipePayAlt);
        assert(r.action==RecipePaymentAction::PaidInOriginalTx);
        assert(tx.journal.size()==3 && !tx.committed);
        tx.output=true;tx.commit();
        assert(tx.persisted[Slot(1081,0)]==2);
    });
    check("output failure after full staging rolls back all original sources", [&] {
        setup(s,{{String,10}}); Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::PaidInOriginalTx);
        tx.output=true;
        tx.rollback();
        assert(!tx.output && tx.current==tx.persisted && tx.journal.empty());
    });
    check("second source error maps to trusted nonzero outer failure and rollback", [&] {
        setup(s,{{String,10}}); Tx tx(s);tx.fail_at=2;
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::AbortOriginalRecipe &&
               r.original_payer_result==4 && tx.attempts==2 && tx.journal.size()==1);
        tx.rollback();
        assert(tx.current==tx.persisted && !tx.committed);
    });
    check("missing original RecipeApply rollback proof cannot stage", [&] {
        setup(s,{{String,10}});g_invalid_proof=true;Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla);
        assert(!tx.attempts && !g_grants);
    });
    check("unverified failure code cannot stage any debits", [&] {
        setup(s,{{String,10}});g_invalid_failure=true;Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("revoked native remote grant rejects before local take", [&] {
        setup(s,{{String,10}});g_allow=false;Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla);
        assert(!tx.attempts && tx.journal.empty() && g_grants>0);
    });
    check("vanilla local-only crafting remains untouched", [&] {
        setup(s,{{String,2}});Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("full recipe shortage leaves existing vanilla payer untouched", [&] {
        setup(s,{{String,100}});Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("unknown selected category is never free and never staged", [&] {
        setup(s,{{String,3},{0,0,true}});Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("real transaction owner must match selected payer actor", [&] {
        setup(s,{{String,10}});Tx tx(s);
        const uint32_t wrong=999;
        std::memcpy(tx.bytes.data()+0xe0,&wrong,4);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("duplicate native inventory slot fails before any journal writes", [&] {
        auto dupe=s;dupe.push_back(s[2]);
        setup(dupe,{{String,10}});Tx tx(dupe);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("unverified Magic metadata excludes remote sources", [&] {
        auto unverified=s;unverified[2].magic_verified=false;
        setup(unverified,{{String,10}});Tx tx(unverified);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("unverified player/base permissions exclude remote sources", [&] {
        auto unverified=s;unverified[1].permission_verified=false;
        setup(unverified,{{String,10}});Tx tx(unverified);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla && !tx.attempts);
    });
    check("bad original payer return address cannot get native authorisation", [&] {
        setup(s,{{String,10}});Tx tx(s);
        int64_t fake[2]={1,1};
        const Call call{tx.bytes.data(),fake,Player,1,nullptr,1234,Payer::InventoryPay};
        const auto r=dispatch(call,&trusted_resolve);
        assert(r.action==RecipePaymentAction::DelegateVanilla &&
               !tx.attempts && !g_grants);
    });
    check("mismatched independently selected payer cost cannot debit", [&] {
        setup(s,{{String,10}});g_bad_payer_row=true;Tx tx(s);
        const auto r=run(tx);
        assert(r.action==RecipePaymentAction::DelegateVanilla &&
               tx.attempts==0 && tx.journal.empty());
    });
    check("incomplete native selected input enumeration cannot debit", [&] {
        setup(s,{{String,10}});g_incomplete_selection=true;Tx tx(s);
        const auto r=run(tx,Payer::RecipePayAlt);
        assert(r.action==RecipePaymentAction::DelegateVanilla &&
               tx.attempts==0 && tx.journal.empty());
    });
    check("malformed original call data rejects without resolver or native reads", [&] {
        setup(s,{{String,10}});Tx tx(s);
        const Call call{tx.bytes.data(),nullptr,Player,1,nullptr,
            RecipePayReturn,Payer::InventoryPay};
        const auto r=dispatch(call,&trusted_resolve);
        assert(r.action==RecipePaymentAction::DelegateVanilla &&
               !g_resolves && !tx.attempts);
    });
    std::printf("PASS: %u original Kiln native hook interface tests. "
        "Production source provider remains ABSENT: no live debits enabled.\n",passed);
}
