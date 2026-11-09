// SPDX-License-Identifier: MIT
// Synthetic, non-game transaction finality evidence tests.
#include "settlement_witness.h"
#include <cassert>
#include <cstdio>
#include <functional>
using namespace cbpay::settlement;
static Receipt receipt(void* tx, uint32_t result=0, uint32_t ops=0,
                       bool journal=true, bool callsite=true,
                       Payer payer=Payer::InventoryPay) {
    return {tx,1,payer,result,ops,journal,callsite};
}
int main() {
    unsigned passed=0;
    auto test=[&](const char* label, const std::function<void()>& fn){
        fn(); ++passed; std::printf("PASS %02u %s\n",passed,label);
    };
    test("valid original payer success and commit",[] {
        Witness w; int tx=0;
        assert(w.payer_returned(receipt(&tx)));
        w.original_finality_returned(&tx,Finality::Commit);
        auto s=w.snapshot();
        assert(s.recorded==1 && s.pending==0 && s.committed_success==1);
    });
    test("failed payer and original rollback",[] {
        Witness w; int tx=0;
        assert(w.payer_returned(receipt(&tx,4)));
        w.original_finality_returned(&tx,Finality::Rollback);
        assert(w.snapshot().rolled_back_failed==1);
        assert(w.snapshot().partial_failed_rollback_call==0);
    });
    test("partial journalled payer failure and rollback call",[] {
        Witness w; int tx=0;
        assert(w.payer_returned(receipt(&tx,4,1)));
        w.original_finality_returned(&tx,Finality::Rollback);
        assert(w.snapshot().partial_failed_rollback_call==1);
    });
    test("failed payer followed by commit is a violation",[] {
        Witness w; int tx=0;
        assert(w.payer_returned(receipt(&tx,4,1)));
        w.original_finality_returned(&tx,Finality::Commit);
        assert(w.snapshot().committed_failed==1);
        assert(w.snapshot().partial_failed_rollback_call==0);
    });
    test("successful payer followed by output failure rollback",[] {
        Witness w; int tx=0;
        assert(w.payer_returned(receipt(&tx,0,2)));
        w.original_finality_returned(&tx,Finality::Rollback);
        assert(w.snapshot().rolled_back_success==1);
    });
    test("different nested original transactions settle independently",[] {
        Witness w; int a=0,b=0;
        assert(w.payer_returned(receipt(&a,4,1)));
        assert(w.payer_returned(receipt(&b,0,2,true,true,Payer::RecipePayAlt)));
        w.original_finality_returned(&b,Finality::Commit);
        w.original_finality_returned(&a,Finality::Rollback);
        auto s=w.snapshot();
        assert(s.recorded==2 && s.pending==0 && s.committed_success==1 &&
               s.rolled_back_failed==1);
    });
    test("duplicate payer in same tx is ambiguous, not proof",[] {
        Witness w; int a=0;
        assert(w.payer_returned(receipt(&a,4,1)));
        assert(!w.payer_returned(receipt(&a,0,0,true,true,Payer::RecipePayAlt)));
        w.original_finality_returned(&a,Finality::Rollback);
        auto s=w.snapshot();
        assert(s.ambiguous==1 && s.pending==0 && s.rolled_back_failed==0);
    });
    test("reject wrong caller and missing actor",[] {
        Witness w; int tx=0;
        assert(!w.payer_returned(receipt(&tx,0,0,true,false)));
        auto r=receipt(&tx);r.actor=0;
        assert(!w.payer_returned(r));
        assert(w.snapshot().refused==2 && w.snapshot().recorded==0);
    });
    test("never accept a partial journal claim without native verification",[] {
        Witness w; int tx=0;
        assert(!w.payer_returned(receipt(&tx,4,2,false)));
        assert(w.snapshot().pending==0);
    });
    test("unknown unrelated transaction finality is ignored",[] {
        Witness w; int tx=0;
        w.original_finality_returned(&tx,Finality::Commit);
        assert(w.snapshot().recorded==0 && w.snapshot().pending==0);
    });
    test("invalid commit argument is fault, not a receipt",[] {
        Witness w; int tx=0;
        w.original_finality_returned(nullptr,Finality::Commit);
        w.original_finality_returned(&tx,static_cast<Finality>(99));
        assert(w.snapshot().finality_faults==2);
    });
    test("a pending borrowed pointer is never reused across world reset",[] {
        Witness w; int tx=0;
        assert(w.payer_returned(receipt(&tx,4,1)));
        w.reset_world();
        auto s=w.snapshot();
        assert(s.pending==0 && s.resets_with_pending==1);
        assert(w.payer_returned(receipt(&tx,0)));
    });
    test("bounded witness capacity cannot evict a pending tx",[] {
        Witness w;
        int tx[130]{};
        for(int i=0;i<128;i++)assert(w.payer_returned(receipt(&tx[i])));
        assert(!w.payer_returned(receipt(&tx[128])));
        auto s=w.snapshot();
        assert(s.recorded==128 && s.pending==128 && s.refused==1);
        w.original_finality_returned(&tx[0],Finality::Commit);
        assert(w.payer_returned(receipt(&tx[129])));
        assert(w.snapshot().pending==128);
    });
    std::printf("PASS: %u bounded settlement receipt tests. Original-game rollback and inventory conservation are NOT proven.\n",passed);
}
