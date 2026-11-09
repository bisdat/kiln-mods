// SPDX-License-Identifier: MIT
// Full functional native payment coordinator exercised against a synthetic
// ORIGINAL recipe tx. No Enshrouded process/game DLL/save is touched.
#include "recipe_payment_bridge.h"

#include <algorithm>
#include <array>
#include <cassert>
#include <cstdio>
#include <cstring>
#include <functional>
#include <map>
#include <vector>

using namespace cbpay;
constexpr uint32_t Player = 1, Local = 1412, Remote = 1081;
constexpr uint32_t STRING = 0x18feb214, WOOD = 0x2b0cf1d5;
constexpr uint64_t sid(uint32_t inventory, uint32_t index) {
    return uint64_t(inventory) | (uint64_t(index) << 32);
}

static Source source(uint32_t owner, uint32_t inventory, uint32_t idx,
                     uint32_t item, uint32_t units, bool same = false)
{
    return {owner, sid(inventory, idx), item, units, true, true, true, true, same};
}
static RecipePaymentProof verified() { return {true, true, true, true, true, true, true}; }

// Synthetic original transaction -- the only place allowed to commit or
// rollback. Native RemoveItems updates the draft inventory immediately and
// appends an actual-layout take record (52 bytes). Just like the game,
// a failed native call MAY leave earlier staged changes.
struct Game {
    std::array<uint8_t, 0x120> tx{};
    std::vector<std::array<uint8_t, 0x34>> journal;
    std::map<uint64_t, uint32_t> saved;
    std::map<uint64_t, uint32_t> working;
    int calls = 0;
    int deny_at_call = 0;
    bool wrong_slot = false, wrong_item = false, add_instead = false;
    bool committed = false, rolled_back = false, output_staged = false;

    explicit Game(std::vector<Source> sources, uint32_t actor = Player) {
        journal.reserve(64);
        std::memcpy(tx.data() + 0xe0, &actor, sizeof actor);
        for (const auto& s : sources) saved[s.slot] = s.units;
        working = saved;
        update();
    }
    void update() {
        const uint8_t* p = reinterpret_cast<const uint8_t*>(journal.data());
        const uint64_t count = journal.size();
        std::memcpy(tx.data() + 0xf0, &p, sizeof p);
        std::memcpy(tx.data() + 0xf8, &count, sizeof count);
    }
    void append(uint64_t slot, uint32_t item, uint16_t qty, uint8_t kind = 1) {
        std::array<uint8_t, 0x34> op{};
        std::memcpy(op.data(), &slot, sizeof slot);
        std::memcpy(op.data() + 8, &item, sizeof item);
        std::memcpy(op.data() + 0x24, &qty, sizeof qty);
        op[0x26] = kind;
        journal.push_back(op);
        update();
    }
    void rollback() {
        assert(!committed);
        working = saved;
        journal.clear();
        output_staged = false;
        rolled_back = true;
        update();
    }
    void commit() {
        assert(!rolled_back);
        saved = working;
        committed = true;
    }
};

static Game* active = nullptr;
static NativeResult12* CBPAY_CALL game_remove(
    NativeResult12* out, void* tx, uint32_t owner, uint32_t item,
    uint64_t preferred_slot, uint32_t count)
{
    assert(active && out && tx == active->tx.data() && owner && item && count);
    Game& g = *active;
    ++g.calls;
    if (g.deny_at_call == g.calls) {
        out->error = 0x15;
        out->still_missing = count;
        return out;
    }
    auto it = g.working.find(preferred_slot);
    if (it == g.working.end() || it->second < count) {
        out->still_missing = count;
        return out;
    }
    const auto where = g.wrong_slot ? sid(9999, 0) : preferred_slot;
    g.append(where, g.wrong_item ? item + 1 : item,
             static_cast<uint16_t>(count), g.add_instead ? 0 : 1);
    it->second -= count;
    return out;
}
struct Active {
    explicit Active(Game& game) { assert(!active); active = &game; }
    ~Active() { active = nullptr; }
};

static RecipePaymentResult execute(
    Game& game, std::vector<Requirement> bill, std::vector<Source> sources,
    RecipePaymentProof proof = verified(), bool grants = true,
    int* authorization_checks = nullptr, int* stage_calls = nullptr)
{
    Active use(game);
    return try_recipe_payment(proof, Player, bill, sources,
        [&](const Source& src, uint32_t actor) {
            assert(actor == Player);
            if (authorization_checks) ++*authorization_checks;
            // In production only native verified ECS permissions can
            // return true; our host harness explicitly simulates the proof.
            return grants && src.loaded;
        },
        [&](const Plan& p) {
            if (stage_calls) ++*stage_calls;
            return stage_recipe_native(p, Player, game.tx.data(), &game_remove);
        });
}

int main() {
    unsigned passed = 0;
    auto test = [&](const char* name, const std::function<void()>& fn) {
        fn(); ++passed;
        std::printf("PASS %02u %s\n", passed, name);
    };
    const auto sources = std::vector<Source>{
        source(Player, 200, 0, STRING, 3),
        source(Local, 1421, 0, STRING, 4, true),
        source(Remote, 1081, 1, STRING, 8)
    };

    test("normal source payment stages player 3 + local Magic 4 + remote Magic 3", [&] {
        Game g(sources);
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::PaidInOriginalTx);
        assert(r.planned_takes == 3 && r.planned_remote_takes == 2);
        assert(g.calls == 3 && g.journal.size() == 3 && !g.committed);
        assert(g.working[sid(200, 0)] == 0 &&
               g.working[sid(1421, 0)] == 0 &&
               g.working[sid(1081, 1)] == 5);
        g.output_staged = true; // original game RecipeApply outputs item
        g.commit();             // original game owns commit, NOT bridge
        assert(g.committed && g.output_staged && g.saved[sid(1081, 1)] == 5);
    });
    test("original recipe output failure rolls back BOTH resource takes and output", [&] {
        Game g(sources);
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::PaidInOriginalTx);
        g.output_staged = true;
        g.rollback();
        assert(g.working == g.saved && g.journal.empty() &&
               !g.output_staged && g.rolled_back && !g.committed);
    });
    test("failure on the second take forbids vanilla fallback and requires outer rollback", [&] {
        Game g(sources); g.deny_at_call = 2;
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::AbortOriginalRecipe &&
               g.calls == 2 && g.journal.size() == 1 && !g.committed);
        assert(g.working != g.saved);
        // Original RecipeApply MUST abort/rollback; never call vanilla payer.
        g.rollback();
        assert(g.working == g.saved && !g.committed);
    });
    test("concurrent stock change after discovery must abort whole original tx", [&] {
        Game g(sources);
        g.working[sid(1081, 1)] = 2; // snapshot of 8 is now stale
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::AbortOriginalRecipe &&
               g.calls == 3 && !g.committed);
        g.rollback();
        assert(g.working == g.saved);
    });
    test("wrong engine selected slot must never be accepted as paid", [&] {
        Game g(sources); g.wrong_slot = true;
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::AbortOriginalRecipe && g.calls == 1);
        g.rollback();
        assert(g.working == g.saved);
    });
    test("wrong item in native journal requires rollback", [&] {
        Game g(sources); g.wrong_item = true;
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::AbortOriginalRecipe);
        g.rollback();
        assert(g.working == g.saved);
    });
    test("put operation instead of take requires rollback", [&] {
        Game g(sources); g.add_instead = true;
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::AbortOriginalRecipe);
        g.rollback();
        assert(g.working == g.saved);
    });
    test("unknown category or invalid zero cost must delegate entirely to vanilla", [&] {
        for (auto bill : {std::vector<Requirement>{{STRING, 3}, {0, 0, true}},
                          std::vector<Requirement>{{STRING, 3}, {WOOD, 0}},
                          std::vector<Requirement>{}}) {
            Game g(sources);
            auto r = execute(g, bill, sources);
            assert(r.action == RecipePaymentAction::DelegateVanilla &&
                   r.reason == RecipeBridgeReason::UnknownOrInvalidCosts &&
                   g.calls == 0);
        }
    });
    test("every required engine proof independently gates all payments", [&] {
        for (int i = 0; i != 7; ++i) {
            RecipePaymentProof proof = verified();
            bool* bits[] = {&proof.original_recipe_callsite, &proof.transaction_actor,
                &proof.original_rollback, &proof.selected_costs,
                &proof.transaction_world_access, &proof.authoritative_magic_access,
                &proof.normal_payment};
            *bits[i] = false;
            Game g(sources);
            int checked = 0, stages = 0;
            auto r = execute(g, {{STRING, 10}}, sources, proof, true, &checked, &stages);
            assert(r.action == RecipePaymentAction::DelegateVanilla &&
                   r.reason == RecipeBridgeReason::NoEngineProof);
            assert(!checked && !stages && !g.calls && g.journal.empty());
        }
    });
    test("no access rights on a single remote slot delegates before touching player stock", [&] {
        Game g(sources);
        int checked = 0, stages = 0;
        auto r = execute(g, {{STRING, 10}}, sources, verified(), false, &checked, &stages);
        assert(r.action == RecipePaymentAction::DelegateVanilla &&
               r.reason == RecipeBridgeReason::FailedAuthorisation);
        assert(checked > 0 && !stages && !g.calls && g.journal.empty());
    });
    test("Magic template alone is not authority to withdraw", [&] {
        auto denied = sources;
        denied[2].permission_verified = false;
        Game g(denied);
        auto r = execute(g, {{STRING, 10}}, denied);
        assert(r.action == RecipePaymentAction::DelegateVanilla && g.calls == 0);
    });
    test("no remote sources needed delegates to original game payer", [&] {
        Game g(sources);
        int stages = 0;
        auto r = execute(g, {{STRING, 2}}, sources, verified(), true, nullptr, &stages);
        assert(r.action == RecipePaymentAction::DelegateVanilla &&
               r.reason == RecipeBridgeReason::NoEligibleRemoteStock &&
               stages == 0 && !g.calls);
    });
    test("full-recipe shortage delegates without trying any native debit", [&] {
        Game g(sources);
        auto r = execute(g, {{STRING, 100}}, sources);
        assert(r.action == RecipePaymentAction::DelegateVanilla &&
               r.reason == RecipeBridgeReason::UnknownOrInvalidCosts &&
               g.calls == 0);
    });
    test("multiple exact ingredients stage together under ONE original transaction", [&] {
        auto list = sources;
        list.push_back(source(Remote, 1082, 0, WOOD, 5));
        Game g(list);
        auto r = execute(g, {{STRING, 10}, {WOOD, 4}}, list);
        assert(r.action == RecipePaymentAction::PaidInOriginalTx &&
               r.planned_takes == 4 && g.calls == 4 && !g.committed);
        g.output_staged = true;
        g.commit();
        assert(g.saved[sid(1082, 0)] == 1 && g.saved[sid(1081, 1)] == 5);
    });
    test("second ingredient failure rolls back prior ingredient takes in original tx", [&] {
        auto list = sources;
        list.push_back(source(Remote, 1082, 0, WOOD, 5));
        Game g(list); g.deny_at_call = 4;
        auto r = execute(g, {{STRING, 10}, {WOOD, 4}}, list);
        assert(r.action == RecipePaymentAction::AbortOriginalRecipe &&
               g.journal.size() == 3);
        g.rollback();
        assert(g.working == g.saved && !g.committed);
    });
    test("tx actor mismatch cannot execute the real native remove path", [&] {
        Game g(sources, 999);
        auto r = execute(g, {{STRING, 10}}, sources);
        assert(r.action == RecipePaymentAction::DelegateVanilla && g.calls == 0);
    });
    test("duplicate linked inventory slot refuses full plan", [&] {
        auto duplicated = sources;
        duplicated.push_back(sources[2]);
        Game g(duplicated);
        auto r = execute(g, {{STRING, 10}}, duplicated);
        assert(r.action == RecipePaymentAction::DelegateVanilla && g.calls == 0);
    });
    test("empty world with no engine proof cannot be a free craft", [&] {
        Game g({});
        auto r = execute(g, {{STRING, 1}}, {}, {});
        assert(r.action == RecipePaymentAction::DelegateVanilla &&
               g.calls == 0 && g.journal.empty());
    });
    std::printf("PASS: %u native payment coordinator + original tx commit/rollback host tests. "
                "Native game integration remains DISABLED pending verified proofs.\n", passed);
}
