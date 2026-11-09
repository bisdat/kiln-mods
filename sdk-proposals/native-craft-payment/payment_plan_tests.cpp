// SPDX-License-Identifier: MIT
#include "payment_plan.h"

#include <cassert>
#include <cstdio>
#include <functional>
#include <map>
#include <utility>
#include <vector>

using namespace cbpay;
constexpr uint32_t Player = 1, SameChest = 1412, RemoteChest = 1081, String = 0x12a;
constexpr uint64_t slot(uint32_t inv, uint32_t index) { return uint64_t(index) << 32 | inv; }

Source stock(uint32_t owner, uint32_t inv, uint32_t at, uint32_t item, uint32_t count,
             bool same = false, bool proof = true) {
    return {owner, slot(inv, at), item, count, true, proof, proof, proof, same};
}
Plan planned(uint32_t actor, std::vector<Requirement> needs, std::vector<Source> sources) {
    return make_plan(actor, needs, sources);
}
void expect(Failure e, uint32_t actor, std::vector<Requirement> needs, std::vector<Source> stocks) {
    const auto p = planned(actor, needs, stocks);
    assert(p.failure == e);
    if (e != Failure::None) assert(p.takes.empty());
}

struct MockNativeTx {
    std::map<uint64_t, std::pair<uint32_t, uint32_t>> material;
    std::map<uint64_t, std::pair<uint32_t, uint32_t>> staged;
    bool committed = false;
    int wrong_slot = 0;
    int deny_at = 0;
    int calls = 0;
    explicit MockNativeTx(std::vector<Source> observed) {
        for (const auto& s : observed) material[s.slot] = {s.item, s.units};
        staged = material;
    }
    StageResult stage(const Take& t) {
        ++calls;
        if (deny_at == calls) return {0x15, t.units, {}};
        uint64_t where = wrong_slot ? slot(9999, 0) : t.slot;
        const auto iter = staged.find(t.slot);
        if (iter == staged.end() || iter->second.first != t.item || iter->second.second < t.units)
            return {0, t.units, {}};
        iter->second.second -= t.units;
        return {0, 0, {{where, t.item, t.units, true}}};
    }
    void rollback() { staged = material; }
    void commit() { material = staged; committed = true; }
};

int main() {
    int passed = 0;
    auto run = [&](const char* name, const std::function<void()>& f) {
        f(); ++passed; std::printf("PASS %02d %s\n", passed, name);
    };
    run("local-first, then same-base, then distant", [] {
        const std::vector<Source> s = {stock(RemoteChest, 1081, 0, String, 30),
            stock(Player, 200, 2, String, 3), stock(SameChest, 1412, 3, String, 20, true)};
        auto p = planned(Player, {{String, 25}}, s);
        assert(p && p.takes.size() == 3);
        assert(p.takes[0].owner == Player && p.takes[0].units == 3);
        assert(p.takes[1].owner == SameChest && p.takes[1].units == 20);
        assert(p.takes[2].owner == RemoteChest && p.takes[2].units == 2);
    });
    run("remote with missing verified Magic, rights or base does not count", [] {
        auto s = stock(RemoteChest, 1081, 0, String, 10);
        for (int i = 0; i != 3; ++i) {
            auto t = s;
            if (i == 0) t.magic_verified = false;
            if (i == 1) t.permission_verified = false;
            if (i == 2) t.base_verified = false;
            expect(Failure::InsufficientStock, Player, {{String, 2}}, {t});
        }
    });
    run("unloaded chest does not contribute", [] {
        auto s = stock(RemoteChest, 1081, 0, String, 10); s.loaded = false;
        expect(Failure::InsufficientStock, Player, {{String, 1}}, {s});
    });
    run("duplicates fail closed even when stock appears sufficient", [] {
        auto s = stock(RemoteChest, 1081, 0, String, 10);
        expect(Failure::DuplicateSlot, Player, {{String, 15}}, {s, s});
    });
    run("invalid slots and owners fail closed", [] {
        auto s = stock(RemoteChest, 1081, 0, String, 10); s.slot = slot(1081, 8);
        expect(Failure::InvalidSnapshot, Player, {{String, 2}}, {s});
        s = stock(RemoteChest, 1081, 0, String, 10); s.owner = 0;
        expect(Failure::InvalidSnapshot, Player, {{String, 2}}, {s});
    });
    run("unspecified recipe category refuses payment", [] {
        expect(Failure::UnsupportedCategory, Player, {{0, 3, true}}, {stock(RemoteChest, 1081, 0, String, 10)});
    });
    run("invalid counts and actor are rejected", [] {
        expect(Failure::InvalidActor, 0, {{String, 2}}, {});
        expect(Failure::InvalidRequirement, Player, {{String, 0}}, {});
        expect(Failure::InvalidRequirement, Player, {}, {});
    });
    run("shortfall returns no partial allocation", [] {
        auto p = planned(Player, {{String, 100}}, {stock(RemoteChest, 1081, 0, String, 10)});
        assert(p.failure == Failure::InsufficientStock && p.missing_units == 90 && p.takes.empty());
    });
    run("multiple ingredients require full preflight", [] {
        auto p = planned(Player, {{String, 2}, {600, 9}}, {stock(Player, 200, 0, String, 3)});
        assert(p.failure == Failure::InsufficientStock && p.missing_item == 600 && p.takes.empty());
    });
    run("repeated ingredient uses same slot only once", [] {
        auto p = planned(Player, {{String, 2}, {String, 3}}, {stock(Player, 200, 0, String, 10)});
        assert(p && p.takes.size() == 1 && p.takes[0].units == 5);
    });
    run("deterministic order under shuffled snapshots", [] {
        std::vector<Source> a = {stock(1082, 2100, 1, String, 2), stock(RemoteChest, 1081, 4, String, 2)};
        auto b = a; std::swap(b[0], b[1]);
        auto p = planned(Player, {{String, 3}}, a), q = planned(Player, {{String, 3}}, b);
        assert(p && q && p.takes.size() == q.takes.size());
        for (size_t i = 0; i < p.takes.size(); ++i)
            assert(p.takes[i].slot == q.takes[i].slot && p.takes[i].units == q.takes[i].units);
    });
    run("successful journal exact-match in same mock native transaction", [] {
        auto s = std::vector<Source>{stock(Player, 200, 0, String, 2), stock(RemoteChest, 1081, 1, String, 5)};
        auto p = planned(Player, {{String, 6}}, s);
        MockNativeTx tx(s);
        auto rc = stage_plan(p, [&](const Take& t) { return tx.stage(t); });
        assert(rc == PaymentOutcome::StagedExact && !tx.committed && tx.calls == 2);
        tx.commit();
        assert(tx.material[slot(200, 0)].second == 0);
        assert(tx.material[slot(1081, 1)].second == 1);
    });
    run("native preferred-slot misdirection requires rollback", [] {
        auto s = std::vector<Source>{stock(RemoteChest, 1081, 0, String, 10)};
        auto p = planned(Player, {{String, 4}}, s);
        MockNativeTx tx(s); tx.wrong_slot = 1;
        auto rc = stage_plan(p, [&](const Take& t) { return tx.stage(t); });
        assert(rc == PaymentOutcome::AbortNeedsRollback && !tx.committed);
        tx.rollback();
        assert(tx.staged == tx.material);
    });
    run("staging error requires outer rollback of prior staged take", [] {
        auto s = std::vector<Source>{stock(Player, 200, 0, String, 1), stock(RemoteChest, 1081, 0, String, 3)};
        auto p = planned(Player, {{String, 4}}, s);
        MockNativeTx tx(s); tx.deny_at = 2;
        auto rc = stage_plan(p, [&](const Take& t) { return tx.stage(t); });
        assert(rc == PaymentOutcome::AbortNeedsRollback && tx.calls == 2 && !tx.committed);
        assert(tx.staged != tx.material); tx.rollback(); assert(tx.staged == tx.material);
    });
    run("no plan means no native staging", [] {
        auto p = planned(Player, {{String, 100}}, {stock(RemoteChest, 1081, 0, String, 10)});
        int calls = 0;
        auto rc = stage_plan(p, [&](const Take&) -> StageResult { ++calls; return {}; });
        assert(rc == PaymentOutcome::Declined && !calls);
    });
    run("zero or huge op counts are refused", [] {
        auto s = stock(Player, 200, 0, String, 70000);
        expect(Failure::TooMany, Player, {{String, 70000}}, {s});
        expect(Failure::TooMany, Player, std::vector<Requirement>(65, {String, 1}), {s});
    });
    std::printf("PASS: %d native transaction/payment preflight tests; NO game binary or inventory touched.\n", passed);
}
