// SPDX-License-Identifier: MIT
// Host-only harness for the current Kiln native InventoryTx and
// InventoryRemoveItems ABI. This is NOT the game and does NOT commit anything.
#include "native_take_adapter.h"
#include <algorithm>
#include <array>
#include <cassert>
#include <cstdio>
#include <map>
#include <vector>
using namespace cbpay;

constexpr uint32_t P = 1, A = 1412, B = 1081, STRING = 0x12a;
constexpr uint64_t sid(uint32_t entity, uint32_t idx) { return entity | (uint64_t(idx) << 32); }

Source source(uint32_t owner, uint32_t inventory, uint32_t idx, uint32_t units) {
    return {owner, sid(inventory, idx), STRING, units, true, true, true, true, owner == A};
}
struct MockTx {
    std::array<uint8_t, 0x120> bytes{};
    std::vector<std::array<uint8_t, 0x34>> journal;
    std::map<uint64_t, uint32_t> stock;
    bool committed = false;
    bool wrong_slot = false, wrong_item = false, put_instead = false, append_extra = false, short_take = false;
    int error_on_call = 0, calls = 0;
    MockTx(uint32_t actor, const std::vector<Source>& sources) {
        journal.reserve(32);
        std::memcpy(bytes.data() + 0xe0, &actor, 4);
        for (const Source& s : sources) stock[s.slot] = s.units;
        refresh();
    }
    void refresh() {
        const uint8_t* ptr = reinterpret_cast<const uint8_t*>(journal.data());
        const uint64_t count = journal.size();
        std::memcpy(bytes.data() + 0xf0, &ptr, sizeof ptr);
        std::memcpy(bytes.data() + 0xf8, &count, sizeof count);
    }
    void append(uint64_t slot, uint32_t item, uint16_t amount, uint8_t kind = 1) {
        std::array<uint8_t, 0x34> op{};
        std::memcpy(op.data(), &slot, sizeof slot);
        std::memcpy(op.data() + 0x08, &item, sizeof item);
        std::memcpy(op.data() + 0x24, &amount, sizeof amount);
        op[0x26] = kind;
        journal.push_back(op);
        refresh();
    }
};
MockTx* g = nullptr;
NativeResult12* CBPAY_CALL mock_remove(NativeResult12* out, void* tx, uint32_t owner,
                                        uint32_t item, uint64_t preferred, uint32_t count) {
    assert(out && g && tx == g->bytes.data() && owner && item && preferred && count);
    g->calls++;
    if (g->error_on_call == g->calls) {
        out->error = 0x15;
        out->still_missing = count;
        return out;
    }
    auto it = g->stock.find(preferred);
    if (it == g->stock.end()) {
        out->still_missing = count;
        return out;
    }
    uint32_t take = std::min(it->second, count);
    if (g->short_take && take > 0) take--;
    out->still_missing = count - take;
    if (take) {
        const uint64_t actual_slot = g->wrong_slot ? sid(9999, 0) : preferred;
        const uint32_t actual_item = g->wrong_item ? item + 1 : item;
        g->append(actual_slot, actual_item, static_cast<uint16_t>(take), g->put_instead ? 0 : 1);
        it->second -= take;
        if (g->append_extra) g->append(sid(10001, 0), item, 1);
    }
    return out;
}
struct Guard {
    explicit Guard(MockTx& tx) { g = &tx; }
    ~Guard() { g = nullptr; }
};
Plan plan(const std::vector<Source>& sources, uint32_t units) {
    return make_plan(P, std::vector<Requirement>{{STRING, units}}, sources);
}
int main() {
    unsigned passed = 0;
    auto run = [&](const char* name, auto&& test) {
        test(); ++passed; std::printf("PASS %02u %s\n", passed, name);
    };
    run("real ABI staging: two engine removals, exact remote slots, NO commit", [&] {
        auto sources = std::vector<Source>{source(P, 200, 0, 2), source(B, 1081, 1, 5)};
        MockTx tx(P, sources); Guard use(tx);
        auto p = plan(sources, 6);
        assert(p && p.takes.size() == 2);
        auto rc = stage_native_plan(p, P, tx.bytes.data(), &mock_remove);
        assert(rc == PaymentOutcome::StagedExact && tx.calls == 2);
        assert(tx.journal.size() == 2 && !tx.committed);
        assert(tx.stock[sid(200, 0)] == 0 && tx.stock[sid(1081, 1)] == 1);
    });
    run("local-only preflight must delegate to original payer", [&] {
        auto s = std::vector<Source>{source(P, 200, 0, 10)};
        MockTx tx(P, s); Guard use(tx);
        auto rc = stage_native_plan(plan(s, 2), P, tx.bytes.data(), &mock_remove);
        assert(rc == PaymentOutcome::Declined && tx.calls == 0 && tx.journal.empty());
    });
    run("wrong transaction operator fails before native call", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(999, s); Guard use(tx);
        assert(stage_native_plan(plan(s, 2), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::Declined);
        assert(tx.calls == 0);
    });
    run("an off-slot native take requires outer rollback", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx); tx.wrong_slot = true;
        assert(stage_native_plan(plan(s, 2), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::AbortNeedsRollback);
        assert(tx.calls == 1 && !tx.committed);
    });
    run("wrong item in journal requires outer rollback", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx); tx.wrong_item = true;
        assert(stage_native_plan(plan(s, 2), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::AbortNeedsRollback);
    });
    run("put operation must never count as paid", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx); tx.put_instead = true;
        assert(stage_native_plan(plan(s, 2), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::AbortNeedsRollback);
    });
    run("unexpected second operation requires outer rollback", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx); tx.append_extra = true;
        assert(stage_native_plan(plan(s, 2), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::AbortNeedsRollback);
    });
    run("short take cannot be accepted even when partially staged", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx); tx.short_take = true;
        assert(stage_native_plan(plan(s, 5), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::AbortNeedsRollback);
        assert(!tx.journal.empty() && !tx.committed);
    });
    run("failure after first staged take must abort entire original recipe", [&] {
        auto s = std::vector<Source>{source(P, 200, 0, 2), source(B, 1081, 0, 5)};
        MockTx tx(P, s); Guard use(tx); tx.error_on_call = 2;
        assert(stage_native_plan(plan(s, 6), P, tx.bytes.data(), &mock_remove) == PaymentOutcome::AbortNeedsRollback);
        assert(tx.calls == 2 && tx.journal.size() == 1 && !tx.committed);
        // An actual engine rollback must be verified before this path can be enabled!
    });
    run("null native function and null tx refuse staging", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx);
        auto p = plan(s, 3);
        assert(stage_native_plan(p, P, tx.bytes.data(), nullptr) == PaymentOutcome::Declined);
        assert(stage_native_plan(p, P, nullptr, &mock_remove) == PaymentOutcome::Declined);
    });
    run("invalid plan never calls native removal", [&] {
        auto s = std::vector<Source>{source(B, 1081, 0, 10)};
        MockTx tx(P, s); Guard use(tx);
        auto p = plan(s, 100);
        assert(!p && stage_native_plan(p, P, tx.bytes.data(), &mock_remove) == PaymentOutcome::Declined);
        assert(tx.calls == 0);
    });
    std::printf("PASS: %u current-layout native removal/journal tests; SIMULATED tx, NO game touched.\n", passed);
}
