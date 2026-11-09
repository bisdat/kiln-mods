// SPDX-License-Identifier: MIT
// Pure host tests; all payer rows and native selection proofs are simulated.
// These are NOT proof of actual category resolution or a real game debit.
#include "selected_costs.h"
#include <cassert>
#include <cstdio>
#include <functional>
#include <limits>
#include <vector>

using namespace cbpay::costs;
constexpr uint32_t STRING = 0x18feb214, WOOD = 0xa12345;
static Proof good() { return {true,true,true,true,true}; }
static PayerRow pay(uint32_t item, uint32_t n) { return {item,n}; }
static SelectedRow selected(uint32_t idx,uint32_t item,uint32_t n,
                            IngredientKind kind=IngredientKind::Exact,
                            bool verified=false) {
    return {idx,item,n,kind,verified};
}
int main() {
    int passed=0;
    auto test=[&](const char* title, const std::function<void()>& fn) {
        fn(); ++passed; std::printf("PASS %02d %s\n",passed,title);
    };
    test("exact selected input reconciles",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,STRING,3)});
        assert(r && r.requirements.size()==1 && r.requirements[0].count==3);
    });
    test("native category selection may reconcile only with proof",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(WOOD,2)},
                         std::vector<SelectedRow>{selected(1,WOOD,2,IngredientKind::Category,true)});
        assert(r && r.requirements[0].item==WOOD);
    });
    test("native alternative selection may reconcile",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(WOOD,2)},
                         std::vector<SelectedRow>{selected(1,WOOD,2,IngredientKind::Alternative,true)});
        assert(r);
    });
    test("multiple rows same item reconcile without double counting",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3),pay(STRING,2)},
                         std::vector<SelectedRow>{selected(0,STRING,2),selected(1,STRING,3)});
        assert(r && r.requirements.size()==2);
    });
    test("different item order is irrelevant",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(WOOD,2),pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,STRING,3),selected(1,WOOD,2)});
        assert(r);
    });
    test("missing original payer provenance cannot spend",[] {
        auto p=good();p.payer_rows_from_original_call=false;
        auto r=reconcile(p,1,std::vector<PayerRow>{pay(STRING,3)},
                        std::vector<SelectedRow>{selected(0,STRING,3)});
        assert(!r && r.status==Status::MissingProof && r.requirements.empty());
    });
    test("incomplete selected recipe cannot spend",[] {
        auto p=good();p.complete_input_enumeration=false;
        assert(reconcile(p,1,std::vector<PayerRow>{pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,STRING,3)}).status==Status::MissingProof);
    });
    test("unverified native category cost selection cannot spend",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(WOOD,2)},
                         std::vector<SelectedRow>{selected(1,WOOD,2,IngredientKind::Category)});
        assert(!r && r.status==Status::UnresolvedSelection);
    });
    test("unknown Workbench placeholder is never free",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3),pay(0,0)},
                         std::vector<SelectedRow>{selected(0,STRING,3),selected(1,0,0,IngredientKind::Category)});
        assert(!r && r.requirements.empty());
    });
    test("nonzero water cost fails closed",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,STRING,3),selected(1,WOOD,1,IngredientKind::Water)});
        assert(!r && r.status==Status::UnsupportedWater);
    });
    test("unverified no-water assertion fails",[] {
        auto p=good();p.no_water_charges=false;
        assert(!reconcile(p,1,std::vector<PayerRow>{pay(STRING,3)},
                          std::vector<SelectedRow>{selected(0,STRING,3)}));
    });
    test("mismatched selected item fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,WOOD,3)});
        assert(!r && r.status==Status::PayerMismatch);
    });
    test("mismatched category scaling fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(WOOD,3)},
                         std::vector<SelectedRow>{selected(1,WOOD,2,IngredientKind::Category,true)});
        assert(!r && r.status==Status::PayerMismatch);
    });
    test("missing or extra payer row fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3),pay(WOOD,2)},
                         std::vector<SelectedRow>{selected(0,STRING,3)});
        assert(!r && r.status==Status::PayerMismatch);
    });
    test("multi-craft remains disabled before native scaling proof",[] {
        assert(reconcile(good(),2,std::vector<PayerRow>{pay(STRING,6)},
                         std::vector<SelectedRow>{selected(0,STRING,6)}).status==Status::UnsupportedMultiplier);
    });
    test("duplicate recipe input index fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,STRING,1),selected(0,STRING,2)});
        assert(!r && r.status==Status::DuplicateRecipeInput);
    });
    test("overflow across duplicate payer lines fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,std::numeric_limits<uint32_t>::max()),pay(STRING,1)},
                         std::vector<SelectedRow>{selected(0,STRING,1)});
        assert(!r && r.status==Status::QuantityOverflow);
    });
    test("overflow across distinct category inputs fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(WOOD,1)},
                         std::vector<SelectedRow>{
                           selected(0,WOOD,std::numeric_limits<uint32_t>::max()),
                           selected(1,WOOD,1,IngredientKind::Category,true)});
        assert(!r && r.status==Status::QuantityOverflow);
    });
    test("empty or malformed native cost list fails",[] {
        assert(reconcile(good(),1,std::vector<PayerRow>{},
                         std::vector<SelectedRow>{selected(0,STRING,3)}).status==Status::InvalidRows);
        assert(reconcile(good(),1,std::vector<PayerRow>{pay(STRING,0)},
                         std::vector<SelectedRow>{selected(0,STRING,3)}).status==Status::InvalidRows);
    });
    test("unsupported input kind fails",[] {
        auto r=reconcile(good(),1,std::vector<PayerRow>{pay(STRING,3)},
                         std::vector<SelectedRow>{selected(0,STRING,3,static_cast<IngredientKind>(250),true)});
        assert(!r && r.status==Status::UnresolvedSelection);
    });
    std::printf("PASS: %d independently reconciled selected-cost host cases; real game authority remains unbound.\n",passed);
}
