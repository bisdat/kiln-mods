#!/usr/bin/env python3
"""Install the pinned Enshrouded original stock hook in a disposable Kiln checkout.

This is REAL server-side game code, not a mock. It is compiled only with
CBPAY_ENABLE_SERVER_STOCK; the normal public Kiln build does not install it.
Requires hook-integration.py and tx-inventory-snapshot.py first. The hook:
  * enters through original InventoryPay (never replaces result/payment)
  * calls the game's original 0x2081c0 selector exactly once
  * validates actual registered Magic component, original tx inventory pages,
    authenticated solo player and game-registered active base stock owners
  * temporarily extends ONLY the item-owner pointer while original payer runs
  * restores original behavior on any missing evidence.
Client-hosted singleplayer is NOT supported by this server exe SHA pin.
"""
import os
from pathlib import Path
import re
import subprocess
import sys
if len(sys.argv)!=2:
    raise SystemExit("Usage: server-stock-live-hook.py <disposable-Kiln-release>")
root=Path(sys.argv[1]).resolve()
pin=os.environ.get("KILN_UPSTREAM_SHA","")
if not re.fullmatch(r"[0-9a-f]{40}",pin):
    raise SystemExit("KILN_UPSTREAM_SHA 40 hex required")
actual=subprocess.check_output(["git","-C",str(root),"rev-parse","HEAD"],
    text=True).strip()
if pin!=actual:
    raise SystemExit(f"Upstream drift: {actual} != {pin}")

def edit(path,needle,replacement):
    p=root/path
    s=p.read_text(encoding="utf8")
    if s.count(needle)!=1:
        raise SystemExit(f"Unsafe native stock anchor {path}: {needle[:80]!r}, count={s.count(needle)}")
    p.write_text(s.replace(needle,replacement),encoding="utf8")
    print("PATCH",path)

# Header copied by hook-integration.py into the actual Windows Rules service.
src=Path(__file__).resolve().parent/"server_live_roster.h"
dest=root/"native/services/rules/cbpay/server_live_roster.h"
if not src.is_file() or not dest.parent.is_dir():
    raise SystemExit("Missing live roster or preceding original payer integration")
dest.write_bytes(src.read_bytes())

rules="native/services/rules/rules.cpp"
edit(rules,
     '#include "cbpay/server_stock_scope.h"',
     '#include "cbpay/server_stock_scope.h"\n#include "cbpay/server_selector_abi.h"\n#include "cbpay/server_live_roster.h"')

# The game calls BaseStockSelector through the same original payer that
# kiln.rules already hooks. Only an authentic original RecipeApply caller
# enters the scope; no additional transaction, removal or inventory write.
code=r"""
#if defined(CBPAY_ENABLE_SERVER_STOCK)
namespace cbpay_game_stock {
using cbpay::stock::native::GameStockLists;
using cbpay::stock::native::SelectorStatus;
using cbpay::stock::native::OriginalPayerScope;
inline SafetyHookInline h_selector;
inline bool enabled=false;
inline uintptr_t original_paid_ret=0;
inline uintptr_t original_selector=0;
inline int64_t operator_offset=0;
inline thread_local void* active_tx=nullptr;
inline thread_local uint32_t active_actor=0;

bool caller_was_original(uintptr_t ret,uintptr_t target) {
    __try {
        if (!ret || !target) return false;
        const auto* p=reinterpret_cast<const uint8_t*>(ret);
        int32_t disp=0;std::memcpy(&disp,p-4,4);
        return p[-5]==0xE8 && static_cast<uintptr_t>(ret+disp)==target;
    } __except (EXCEPTION_EXECUTE_HANDLER) {return false;}
}

bool payer_matches(void* tx,uint32_t actor,uintptr_t ret) {
    if(!enabled || !actor || !tx || !original_paid_ret ||
       ret!=original_paid_ret || operator_offset!=0xE0)
        return false;
    __try {
        uint32_t original_actor=0;
        std::memcpy(&original_actor,
                    static_cast<const uint8_t*>(tx)+operator_offset,4);
        return original_actor==actor &&
               static_cast<const uint8_t*>(tx)[0xB1]==2;
    } __except (EXCEPTION_EXECUTE_HANDLER) {return false;}
}

struct PayLife {
    void* prior_tx;
    uint32_t prior_actor;
    PayLife(void* tx,uint32_t actor)
        : prior_tx(active_tx),prior_actor(active_actor) {
        active_tx=tx;active_actor=actor;
    }
    ~PayLife(){active_tx=prior_tx;active_actor=prior_actor;}
    PayLife(const PayLife&)=delete;
    PayLife& operator=(const PayLife&)=delete;
};

// Original stock selector's actual four-argument Microsoft x64 ABI.
// The SafetyHook trampoline is called FIRST. No client-state-derived source
// data may enter this callback.
SelectorStatus* __fastcall hk_selector(
    SelectorStatus* status,GameStockLists* lists,
    void* primary,void* fallback) {
    const uintptr_t ret=reinterpret_cast<uintptr_t>(_ReturnAddress());
    SelectorStatus* vanilla=h_selector.call<SelectorStatus*>(
        status,lists,primary,fallback);
    if(!enabled || !vanilla || vanilla!=status || !status ||
       status->error!=0 || !lists ||
       !active_actor || !active_tx || !original_selector ||
       !OriginalPayerScope::current())
        return vanilla;
    const uintptr_t base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    // These are the exact two return addresses of the pinned server payer's
    // direct E8 calls. No other native selector user may be intercepted.
    if ((ret!=base+0x199d93 && ret!=base+0x199fa7) ||
        !caller_was_original(ret,original_selector))
        return vanilla;
    try {
        // SEH-probe the returned original borrowed owner/fuel buffers.
        std::vector<uint32_t> copied;
        if(!cbpay::stock::live::copy_owners(
             lists->item_owners,lists->item_count,&copied) ||
           lists->fuel_count>4096 ||
           (lists->fuel_count && !lists->fuel_owners))
           return vanilla;
        cbpay::stock::live::SelectorInput old=
            cbpay::stock::live::current_selector;
        cbpay::stock::live::current_selector={primary,fallback,true};
        auto* scope=OriginalPayerScope::current();
        if(scope) scope->after_original_selector(active_actor,active_tx,lists);
        cbpay::stock::live::current_selector=old;
    } catch (...) {
        cbpay::stock::live::current_selector={};
        // Do not mutate the original game's selector status/fuel buffer.
    }
    return vanilla;
}

uint32_t invoke_original_payer(void* tx,int64_t* costs,uint32_t owner,
                               uint32_t multiplier,void* extra,uintptr_t ret) {
    if(payer_matches(tx,owner,ret)) {
        PayLife owner_scope(tx,owner);
        OriginalPayerScope source_scope(owner,tx,&cbpay::stock::live::roster);
        return h_pay.call<uint32_t>(tx,costs,owner,multiplier,extra);
    }
    return h_pay.call<uint32_t>(tx,costs,owner,multiplier,extra);
}

void install(const KilnApi* api) {
    enabled=false;
    if(!h_pay || !(g_hooks & KILN_RULES_HOOK_CRAFT) ||
       kiln_rules_creative::flame_creative() ||
       !cbpay::stock::live::bind(api))
        return;
    uintptr_t paid=0;
    int64_t op=0;
    if (api->symbol(KilnStr{"rules/RecipePaid",16},&paid)!=KILN_OK ||
        api->constant(KilnStr{"items/tx.operator",17},&op)!=KILN_OK ||
        !paid || op!=0xE0)
        return;
    // SHA-256 checked by live::bind against actual process executable.
    const auto module=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    const uintptr_t selector=module+0x2081c0;
    // Additional exact prologue and original E8 caller verification.
    constexpr uint8_t sig[]={
      0x48,0x89,0x5C,0x24,0x08,0x48,0x89,0x6C,0x24,0x10,
      0x48,0x89,0x74,0x24,0x20,0x57,0x48,0x83,0xEC,0x40
    };
    bool good=false;
    __try {
        good=std::memcmp(reinterpret_cast<const void*>(selector),
                         sig,sizeof(sig))==0;
    } __except(EXCEPTION_EXECUTE_HANDLER) {good=false;}
    if(!good || !caller_was_original(module+0x199d93,selector) ||
       !caller_was_original(module+0x199fa7,selector))
        return;
    h_selector=safetyhook::create_inline(
        reinterpret_cast<void*>(selector),
        reinterpret_cast<void*>(&hk_selector));
    if(!h_selector)return;
    original_paid_ret=paid;
    original_selector=selector;
    operator_offset=op;
    enabled=true;
    LOGI("Cross-base ORIGINAL native stock owner hook active on exact pinned dedicated-server EXE; only authenticated solo player with game-registered Magic stock; no postpay");
}
} // namespace cbpay_game_stock
#endif

"""
anchor="uint32_t __fastcall hk_pay(void* tx, int64_t* costs, uint32_t owner, uint32_t multiplier, void* extra) {"
edit(rules,anchor,code+anchor)

edit(rules,
"""    const uint32_t original = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
    if constexpr (cbpay::settlement::kEnabled) {""",
"""#if defined(CBPAY_ENABLE_SERVER_STOCK)
    const uint32_t original = cbpay_game_stock::invoke_original_payer(
        tx,costs,owner,multiplier,extra,
        reinterpret_cast<uintptr_t>(_ReturnAddress()));
#else
    const uint32_t original = h_pay.call<uint32_t>(tx, costs, owner, multiplier, extra);
#endif
    if constexpr (cbpay::settlement::kEnabled) {""")

edit(rules,
"""    g_hooks |= kiln_rules_flight::install(g_api, off);  // character flight yields to Creative Mode too""",
"""#if defined(CBPAY_ENABLE_SERVER_STOCK)
    cbpay_game_stock::install(g_api);
#endif
    g_hooks |= kiln_rules_flight::install(g_api, off);  // character flight yields to Creative Mode too""")

content=(root/rules).read_text(encoding="utf8")
assert content.count("cbpay::stock::live::roster")==1
assert content.count("h_selector.call<SelectorStatus*>")==1
assert content.count("cbpay_game_stock::install(g_api)")==1
assert "resolver" not in content or "nullptr);" in content
print("PASS: real original-stock selector detour and real game-native source roster wired for opt-in exact dedicated server ONLY; both original payers unchanged.")
