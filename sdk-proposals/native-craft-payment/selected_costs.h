// SPDX-License-Identifier: MIT
// Fail-closed reconciliation of ORIGINAL recipe payer costs with an
// independently resolved, COMPLETE native recipe/selection bill.
// This proves neither Magic Chest access nor RecipeApply rollback.
// No game reads, writes, detours or allocations to game state.
#pragma once

#include "payment_plan.h"

#include <cstdint>
#include <limits>
#include <span>
#include <unordered_set>
#include <utility>
#include <vector>

namespace cbpay::costs {

enum class IngredientKind : uint8_t { Exact, Category, Alternative, Water };
struct PayerRow {
    uint32_t item = 0;
    uint32_t count = 0;
};
struct SelectedRow {
    uint32_t recipe_input_index = 0;
    uint32_t item = 0;   // actual CHOSEN item, never a category ID
    uint32_t count = 0;  // actual FINAL required units at one craft
    IngredientKind kind = IngredientKind::Exact;
    bool native_selection_verified = false;
};
// These facts must be obtained from the CURRENT original RecipeApply caller
// and independently decoded game recipe/selection metadata. An observed
// 0x18-byte row layout or the managed RecipeInfo alone is NOT a proof.
struct Proof {
    bool payer_rows_from_original_call = false;
    bool selected_recipe_identity = false;
    bool complete_input_enumeration = false;
    bool native_category_scaling = false;
    bool no_water_charges = false;
    bool valid() const {
        return payer_rows_from_original_call && selected_recipe_identity &&
               complete_input_enumeration && native_category_scaling &&
               no_water_charges;
    }
};
enum class Status : uint8_t {
    Ready, MissingProof, UnsupportedMultiplier, InvalidRows,
    DuplicateRecipeInput, UnresolvedSelection, UnsupportedWater,
    QuantityOverflow, PayerMismatch
};
struct Bill {
    Status status = Status::MissingProof;
    std::vector<Requirement> requirements;
    explicit operator bool() const { return status == Status::Ready; }
};

// Both cost sources must agree on every item and total amount, regardless
// of row order or distinct recipe inputs that select the same material.
// The payer's raw 0x18 byte layout is deliberately NOT decoded here:
// native integration must first prove and safely copy its selected rows.
// Multi-craft is disabled until original category/water scaling is proven.
inline Bill reconcile(const Proof& proof, uint32_t multiplier,
                      std::span<const PayerRow> original_payer_rows,
                      std::span<const SelectedRow> selected_recipe_inputs) {
    if (!proof.valid()) return {Status::MissingProof, {}};
    if (multiplier != 1) return {Status::UnsupportedMultiplier, {}};
    if (original_payer_rows.empty() || selected_recipe_inputs.empty() ||
        original_payer_rows.size() > 64 || selected_recipe_inputs.size() > 64)
        return {Status::InvalidRows, {}};

    struct Aggregate { uint32_t item; uint64_t units; };
    auto add = [](std::vector<Aggregate>* dest, uint32_t item, uint32_t count) {
        for (auto& r : *dest) {
            if (r.item == item) {
                r.units += count;
                return r.units <= std::numeric_limits<uint32_t>::max();
            }
        }
        dest->push_back({item, count});
        return true;
    };

    std::vector<Aggregate> from_payer, from_selection;
    std::unordered_set<uint32_t> recipe_indices;
    std::vector<Requirement> needs;
    needs.reserve(selected_recipe_inputs.size());
    for (const auto& row : original_payer_rows) {
        if (!row.item || !row.count) return {Status::InvalidRows, {}};
        if (!add(&from_payer, row.item, row.count))
            return {Status::QuantityOverflow, {}};
    }
    for (const auto& input : selected_recipe_inputs) {
        if (!recipe_indices.insert(input.recipe_input_index).second)
            return {Status::DuplicateRecipeInput, {}};
        if (input.kind == IngredientKind::Water)
            return {Status::UnsupportedWater, {}};
        if (!input.item || !input.count ||
            (input.kind != IngredientKind::Exact &&
             input.kind != IngredientKind::Category &&
             input.kind != IngredientKind::Alternative) ||
            (input.kind != IngredientKind::Exact && !input.native_selection_verified))
            return {Status::UnresolvedSelection, {}};
        if (!add(&from_selection, input.item, input.count))
            return {Status::QuantityOverflow, {}};
        needs.push_back({input.item, input.count, false});
    }
    if (from_payer.size() != from_selection.size())
        return {Status::PayerMismatch, {}};
    for (const auto& p : from_payer) {
        bool match = false;
        for (const auto& s : from_selection)
            if (s.item == p.item && s.units == p.units) match = true;
        if (!match) return {Status::PayerMismatch, {}};
    }
    return {Status::Ready, std::move(needs)};
}

} // namespace cbpay::costs
