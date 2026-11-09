// SPDX-License-Identifier: MIT
// Native selected-recipe shape contract from Kiln.Content's CURRENT
// reflection-validated RecipeInputResource: itemStack and inputItemCategory.
// Detached rows only. This cannot itself choose a category material, prove
// player/base entitlement, read ECS or stage an inventory operation.
#pragma once

#include "selected_costs.h"

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <span>
#include <unordered_set>
#include <vector>

namespace cbpay::recipes {

constexpr size_t kRecipeInputStride = 48;
constexpr size_t kItemOffset = 0x10;
constexpr size_t kItemCountOffset = 0x14;
constexpr size_t kCategoryOffset = 0x28;
constexpr size_t kCategoryCountOffset = 0x2c;

struct NativeInput {
    uint32_t index = 0;
    uint32_t item = 0;
    uint32_t category = 0;
    uint32_t count = 0;
};

enum class Status : uint8_t {
    Ready, InvalidRows, UnresolvedInput, InvalidSelection,
    UnknownCategoryMembership, CountMismatch, DuplicateSelection
};
struct Metadata {
    Status status = Status::InvalidRows;
    std::vector<NativeInput> inputs;
    explicit operator bool() const { return status == Status::Ready; }
};

// The bytes must be copied in the original transaction from a positively
// identified recipe registry entry, using CURRENT REFLECTION-validated
// offsets, before calling this function. No raw pointers are retained.
// A category has its own real amount at +0x2c, NOT itemStack.count at +0x14.
inline Metadata decode(std::span<const uint8_t> bytes) {
    if (bytes.empty() || bytes.size() > 64 * kRecipeInputStride ||
        bytes.size() % kRecipeInputStride != 0)
        return {Status::InvalidRows, {}};
    Metadata out{Status::Ready, {}};
    out.inputs.reserve(bytes.size() / kRecipeInputStride);
    for (size_t i = 0; i < bytes.size() / kRecipeInputStride; ++i) {
        const auto* p = bytes.data() + i * kRecipeInputStride;
        uint32_t item=0, n=0, cat=0, cn=0;
        std::memcpy(&item, p + kItemOffset, 4);
        std::memcpy(&n, p + kItemCountOffset, 4);
        std::memcpy(&cat, p + kCategoryOffset, 4);
        std::memcpy(&cn, p + kCategoryCountOffset, 4);
        // Strictly positive, exactly one kind. No synthetic zero-cost row.
        if ((!item && !cat) || (item && cat) ||
            (item && (!n || cn)) || (cat && (!cn || n)))
            return {Status::UnresolvedInput, {}};
        out.inputs.push_back({
            static_cast<uint32_t>(i), item, cat, item ? n : cn
        });
    }
    return out;
}

// CategoryMembership MUST be the game's original native category registry
// query bound to the SAME chosen item & recipe, not a guess from item names,
// item class, template, or a cached client selection.
// The selection amount must be the actual native-final amount. If an
// independent game balance factor could change the count, the resolver must
// reject (not call this with falsely asserted original count).
template<class CategoryMembership>
Status match_selected(const Metadata& metadata,
                      std::span<const costs::SelectedRow> selected,
                      CategoryMembership&& is_native_category_member) {
    if (!metadata || metadata.inputs.empty()) return Status::InvalidRows;
    if (metadata.inputs.size() != selected.size()) return Status::InvalidSelection;
    std::unordered_set<uint32_t> indices;
    for (const auto& row : selected) {
        if (!indices.insert(row.recipe_input_index).second)
            return Status::DuplicateSelection;
        if (row.recipe_input_index >= metadata.inputs.size() ||
            !row.item || !row.count)
            return Status::InvalidSelection;
        const auto& expected = metadata.inputs[row.recipe_input_index];
        if (row.count != expected.count) return Status::CountMismatch;
        if (expected.item) {
            if (row.kind != costs::IngredientKind::Exact ||
                row.item != expected.item)
                return Status::InvalidSelection;
        } else {
            if (row.kind != costs::IngredientKind::Category ||
                !row.native_selection_verified ||
                !is_native_category_member(expected.category, row.item))
                return Status::UnknownCategoryMembership;
        }
    }
    return Status::Ready;
}

} // namespace cbpay::recipes
