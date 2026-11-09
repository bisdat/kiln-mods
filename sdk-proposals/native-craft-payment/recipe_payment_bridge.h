// SPDX-License-Identifier: MIT
// Server-side FIRST FUNCTIONAL implementation seam for native recipe payment.
// No managed Items.Take, no free-crafting shortcut and NO separate commit.
//
// IMPORTANT: This does NOT make an unverified game build eligible. Every gate
// is supplied by the authoritative native game integration AT THE PAYER CALL.
// In the production game these proofs are currently unavailable, therefore
// the bridge delegates to vanilla and never stages a remote debit.
//
// Once proven, an authorised exact-item recipe can use stage_native_plan(),
// which calls the GAME InventoryRemoveItems on the ORIGINAL transaction.
// The outer RecipeApply must own both item output AND commit/rollback.
#pragma once

#include "native_take_adapter.h"

#include <cstdint>
#include <span>

namespace cbpay {

struct RecipePaymentProof {
    // Independently verified original InventoryPay/RecipePayAlt return address.
    bool original_recipe_callsite = false;
    // Original tx.operator == actual in-world player (not just an ID from Chat).
    bool transaction_actor = false;
    // Native original RecipeApply rolls back all staged takes AND outputs
    // when any payer error/shortfall occurs. Host tests are not proof.
    bool original_rollback = false;
    // Exact *selected* native bill, after category/alternative multipliers
    // and water requirements, was decoded and reconciled against the game.
    bool selected_costs = false;
    // Transaction-scoped, write-tracked ECS access for all source owners.
    bool transaction_world_access = false;
    // Source grants below were resolved using authoritative game metadata:
    // native Magic Chest property, Flame Altar/base and player permissions.
    bool authoritative_magic_access = false;
    // Another mod's FREE_CRAFTING or creative mode must not preempt the payer.
    bool normal_payment = false;

    bool valid() const {
        return original_recipe_callsite && transaction_actor && original_rollback &&
               selected_costs && transaction_world_access &&
               authoritative_magic_access && normal_payment;
    }
};

enum class RecipePaymentAction : uint8_t {
    DelegateVanilla,     // no new operations; execute original payment
    PaidInOriginalTx,    // exact validated op delta staged; DO NOT call vanilla
    AbortOriginalRecipe, // partial staging possible; original tx MUST rollback
};

enum class RecipeBridgeReason : uint8_t {
    None,
    NoEngineProof,
    UnknownOrInvalidCosts,
    NoEligibleRemoteStock,
    FailedAuthorisation,
    NativeStagingInvalid,
};

struct RecipePaymentResult {
    RecipePaymentAction action = RecipePaymentAction::DelegateVanilla;
    RecipeBridgeReason reason = RecipeBridgeReason::NoEngineProof;
    uint32_t planned_takes = 0;
    uint32_t planned_remote_takes = 0;
};

// Authorise(source, actor) MUST be an engine-native callback querying the
// live original transaction world; never a checked _Magic string, cached
// Chest scan, client-supplied source or stale UI value.
// Stage(plan) MUST use the supplied native InventoryRemoveItems on ORIGINAL tx
// and compare every new operation against real slot,item,count,type.
// Neither callback may commit the tx or grant cross-base eligibility itself.
template <typename Authorise, typename Stage>
RecipePaymentResult try_recipe_payment(
    const RecipePaymentProof& proof,
    uint32_t actor,
    std::span<const Requirement> selected_costs,
    std::span<const Source> live_slots,
    Authorise&& authorise,
    Stage&& stage)
{
    if (!proof.valid() || !actor)
        return {RecipePaymentAction::DelegateVanilla, RecipeBridgeReason::NoEngineProof, 0, 0};

    const Plan plan = make_plan(actor, selected_costs, live_slots);
    if (!plan) return {RecipePaymentAction::DelegateVanilla,
                      RecipeBridgeReason::UnknownOrInvalidCosts, 0, 0};
    uint32_t remote = 0;
    for (const Take& take : plan.takes)
        if (take.owner != actor) ++remote;
    if (!remote)
        return {RecipePaymentAction::DelegateVanilla,
                RecipeBridgeReason::NoEligibleRemoteStock, 0, 0};

    // Never stage one local take then discover a revoked remote grant.
    // Check the complete native source set immediately before ANY write.
    for (const Take& take : plan.takes) {
        const Source* match = nullptr;
        for (const Source& src : live_slots) {
            if (src.owner == take.owner && src.slot == take.slot &&
                src.item == take.item) { match = &src; break; }
        }
        if (!match || !match->loaded || !authorise(*match, actor))
            return {RecipePaymentAction::DelegateVanilla,
                    RecipeBridgeReason::FailedAuthorisation, 0, 0};
        if (take.owner != actor &&
            (!match->magic_verified || !match->permission_verified ||
             !match->base_verified))
            return {RecipePaymentAction::DelegateVanilla,
                    RecipeBridgeReason::FailedAuthorisation, 0, 0};
    }

    // This is the ONLY place a remote payment can enter the tx. A partial
    // result is NOT safe for fallback: the original RecipeApply MUST abort.
    const PaymentOutcome outcome = stage(plan);
    if (outcome == PaymentOutcome::StagedExact)
        return {RecipePaymentAction::PaidInOriginalTx, RecipeBridgeReason::None,
                static_cast<uint32_t>(plan.takes.size()), remote};
    if (outcome == PaymentOutcome::AbortNeedsRollback)
        return {RecipePaymentAction::AbortOriginalRecipe,
                RecipeBridgeReason::NativeStagingInvalid,
                static_cast<uint32_t>(plan.takes.size()), remote};
    return {RecipePaymentAction::DelegateVanilla,
            RecipeBridgeReason::NativeStagingInvalid, 0, 0};
}

// Production integration should bind Stage to this function, called ONLY
// with a correctly scoped original game tx and resolved native function.
// This helper NEVER commits, rolls back, returns a "paid" code or bypasses
// the original payer by itself. These choices belong to the outer hook.
inline PaymentOutcome stage_recipe_native(
    const Plan& plan, uint32_t actor, void* original_recipe_tx,
    NativeRemoveFn original_remove_items)
{
    return stage_native_plan(plan, actor, original_recipe_tx, original_remove_items);
}

} // namespace cbpay
