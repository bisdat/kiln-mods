// SPDX-License-Identifier: MIT
// Native SERVER integration at the EXISTING Kiln Rules recipe payer hook.
// This file lives INSIDE the experimental kiln-rules native service; it does
// not add a detour, expose an untrusted plugin ABI, or persist any world data.
#pragma once

#include "recipe_payment_bridge.h"
#include "selected_costs.h"

#include <cstdint>
#include <span>
#include <vector>

namespace cbpay::engine {

enum class Payer : uint32_t { InventoryPay = 1, RecipePayAlt = 2 };
struct Call {
    void* original_tx = nullptr;
    const int64_t* original_costs = nullptr;
    uint32_t owner = 0;
    uint32_t multiplier = 0;
    const void* extra = nullptr;
    uintptr_t return_address = 0;
    Payer payer = Payer::InventoryPay;
};

// ONLY a trusted native resolver in the same kiln-rules game service may
// construct this. The costs must reflect the full SELECTED recipe bill,
// not the managed RecipeInfo projection or an unverified raw 0x18 layout.
// Sources must come from transaction-scoped, write-tracked game storage.
struct Resolved {
    RecipePaymentProof proof{};
    // TWO independent native sources must match the same original payer:
    // selected recipe/category/alternative bill and detached payer cost rows.
    // No missing category row is converted to zero-cost or guessed material.
    costs::Proof cost_proof{};
    std::vector<costs::PayerRow> original_payer_rows;
    std::vector<costs::SelectedRow> selected_ingredients;
    std::vector<Source> eligible_slots;
    NativeRemoveFn original_remove_items = nullptr;
    // Authoritative native game grant validation immediately before staging.
    bool (*grant)(const Call&, const Source&, uint32_t actor) = nullptr;
    // The original RecipeApply payer failure result *and* rollback behaviour
    // must be independently verified; no guessed error code is acceptable.
    uint32_t verified_failure_result = 0;
    bool failure_result_verified = false;
};

using ResolveFn = bool (*)(const Call&, Resolved*);

struct Decision {
    RecipePaymentAction action = RecipePaymentAction::DelegateVanilla;
    uint32_t original_payer_result = 0;
    RecipeBridgeReason reason = RecipeBridgeReason::NoEngineProof;
    uint32_t remote_takes = 0;
};

// This is a genuine executable transaction callback, but DOES NOT run in
// ordinary Kiln because the validated native resolver is not available.
// The host tests inject a synthetic resolver explicitly. No arbitrary
// managed API or user mod can set this callback in the shipped service.
inline Decision dispatch(const Call& call, ResolveFn resolver) {
    if (!resolver || !call.original_tx || !call.original_costs ||
        !call.owner || !call.multiplier ||
        (call.payer != Payer::InventoryPay && call.payer != Payer::RecipePayAlt))
        return {};

    Resolved data{};
    if (!resolver(call, &data) || !data.proof.valid() ||
        !data.original_remove_items || !data.grant ||
        !data.failure_result_verified || !data.verified_failure_result ||
        data.original_payer_rows.empty() || data.original_payer_rows.size() > 64 ||
        data.selected_ingredients.empty() || data.selected_ingredients.size() > 64 ||
        data.eligible_slots.size() > 8192)
        return {};

    // A game-owned payer row alone is NOT proof that the recipe's complete
    // category/alternative/water bill was selected. Reconcile with an
    // independently verified native recipe/selection view before ANY take.
    const auto bill = costs::reconcile(
        data.cost_proof, call.multiplier, data.original_payer_rows,
        data.selected_ingredients);
    if (!bill) return {};

    // The caller's authoritative recipe-specific return address and
    // transaction owner are part of the resolver's REQUIRED proof. Recheck
    // each candidate source immediately before any journalled item take.
    const auto result = try_recipe_payment(
        data.proof, call.owner, bill.requirements, data.eligible_slots,
        [&](const Source& source, uint32_t actor) {
            return data.grant(call, source, actor);
        },
        [&](const Plan& plan) {
            return stage_recipe_native(
                plan, call.owner, call.original_tx, data.original_remove_items);
        });

    if (result.action == RecipePaymentAction::PaidInOriginalTx)
        return {result.action, 0, result.reason, result.planned_remote_takes};
    if (result.action == RecipePaymentAction::AbortOriginalRecipe)
        return {result.action, data.verified_failure_result, result.reason,
                result.planned_remote_takes};
    return {RecipePaymentAction::DelegateVanilla, 0, result.reason, 0};
}

} // namespace cbpay::engine
