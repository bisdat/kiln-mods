// SPDX-License-Identifier: MIT
// Native recipe *shape* host tests; chosen category rows are synthetic.
// Real engine category membership, scaling and original payer remain unbound.
#include "recipe_metadata.h"
#include <array>
#include <cassert>
#include <cstdio>
#include <functional>
#include <vector>

using namespace cbpay::recipes;
using namespace cbpay::costs;
static constexpr uint32_t STRING=0x18feb214, WOOD=0x543210, CATEGORY=0xaabbccdd;

static std::vector<uint8_t> make(uint32_t item, uint32_t n,
                                 uint32_t category=0,uint32_t catn=0) {
    std::vector<uint8_t> row(48);
    std::memcpy(row.data()+kItemOffset,&item,4);
    std::memcpy(row.data()+kItemCountOffset,&n,4);
    std::memcpy(row.data()+kCategoryOffset,&category,4);
    std::memcpy(row.data()+kCategoryCountOffset,&catn,4);
    return row;
}
static Metadata workbench() {
    auto bytes=make(STRING,3);
    const auto wood=make(0,0,CATEGORY,6);
    bytes.insert(bytes.end(),wood.begin(),wood.end());
    return decode(bytes);
}
static auto member=[](uint32_t cat,uint32_t item) {
    return cat==CATEGORY && item==WOOD; // synthetic game selection oracle
};
int main(){
    int passed=0;
    auto test=[&](const char* name,const std::function<void()>& body) {
        body();++passed;std::printf("PASS %02d %s\n",passed,name);
    };
    test("original Workbench shape decodes two full ingredient amounts",[] {
        auto v=workbench();
        assert(v && v.inputs.size()==2 && v.inputs[0].count==3 &&
               v.inputs[1].item==0 && v.inputs[1].category==CATEGORY &&
               v.inputs[1].count==6);
    });
    test("matching independently selected String and Any Wood",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false},
                                   {1,WOOD,6,IngredientKind::Category,true}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::Ready);
    });
    test("category without game native selection proof fails",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false},
                                   {1,WOOD,6,IngredientKind::Category,false}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::UnknownCategoryMembership);
    });
    test("wrong chosen wood type fails game membership",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false},
                                   {1,STRING,6,IngredientKind::Category,true}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::UnknownCategoryMembership);
    });
    test("category zero count is unresolved and never free",[] {
        assert(decode(make(0,0,CATEGORY,0)).status==cbpay::recipes::Status::UnresolvedInput);
    });
    test("category amount is never itemStack zero",[] {
        auto r=decode(make(0,0,CATEGORY,6));assert(r && r.inputs[0].count==6);
    });
    test("both direct and category inputs are ambiguous",[] {
        assert(decode(make(STRING,3,CATEGORY,6)).status==cbpay::recipes::Status::UnresolvedInput);
    });
    test("unlisted zero ingredient is rejected",[] {
        assert(decode(make(0,0,0,0)).status==cbpay::recipes::Status::UnresolvedInput);
    });
    test("invalid mix category id with nonzero direct count",[] {
        assert(decode(make(0,2,CATEGORY,3)).status==cbpay::recipes::Status::UnresolvedInput);
    });
    test("direct cost with spurious category count rejected",[] {
        assert(decode(make(STRING,3,0,2)).status==cbpay::recipes::Status::UnresolvedInput);
    });
    test("selected category amount mismatch rejects before spending",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false},
                                   {1,WOOD,5,IngredientKind::Category,true}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::CountMismatch);
    });
    test("selected item mismatch rejects before spending",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,WOOD,3,IngredientKind::Exact,false},
                                   {1,WOOD,6,IngredientKind::Category,true}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::InvalidSelection);
    });
    test("missing native category selection never authorises",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::InvalidSelection);
    });
    test("duplicate recipe input selection rejected",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false},
                                   {0,STRING,3,IngredientKind::Exact,false}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::DuplicateSelection);
    });
    test("bad row count and max 64 inputs rejected",[] {
        assert(decode(std::vector<uint8_t>(47)).status==cbpay::recipes::Status::InvalidRows);
        assert(decode(std::vector<uint8_t>(65*48)).status==cbpay::recipes::Status::InvalidRows);
    });
    test("invalid selected category index rejected",[] {
        auto m=workbench();
        std::vector<SelectedRow> s{{0,STRING,3,IngredientKind::Exact,false},
                                   {20,WOOD,6,IngredientKind::Category,true}};
        assert(match_selected(m,s,member)==cbpay::recipes::Status::InvalidSelection);
    });
    std::printf("PASS: %d native recipe shape tests; no live game source entitlement.\n",passed);
}
