#!/usr/bin/env python3
"""Experimental Kiln native-side raw recipe input probe (not a game mod)."""
from pathlib import Path
import subprocess
import sys

PIN = "eba9a5e1e0e0044a9146fdd53021a751e2957030"
if len(sys.argv) != 2:
    raise SystemExit("usage: native.py <pinned-kiln-checkout>")
root = Path(sys.argv[1]).resolve()
sha = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
if sha != PIN:
    raise SystemExit(f"Refusing unsupported upstream commit {sha}; expected {PIN}")

def replace(path, before, after):
    target = root / path
    text = target.read_text()
    if text.count(before) != 1:
        raise RuntimeError(f"Missing/ambiguous upstream anchor in {path}: {before[:70]}")
    target.write_text(text.replace(before, after, 1))
    print("PATCH", path)

replace("native/core/abi/services/kiln_gameplay.h",
"""typedef struct KilnGameplayStatus {""",
"""/* EXPERIMENT ONLY: the 48-byte RecipeInfo input records as copied VALUES.
 * Not a category schema or permission to authorize crafting. At most 8 records. */
#define KILN_GAMEPLAY_RAW_INPUT_STRIDE 48u
typedef struct KilnGameplayRawRecipe {
    uint32_t size, id, input_count, stride;
    uint8_t inputs[KILN_GAMEPLAY_MAX_OUTPUTS * KILN_GAMEPLAY_RAW_INPUT_STRIDE];
} KilnGameplayRawRecipe;

typedef struct KilnGameplayStatus {""")

replace("native/core/abi/services/kiln_gameplay.h",
"""} KilnGameplayApi;""",
"""    /* Optional v1 table tail. Clients must check header.size before calling.
     * Read-only; server simulation tick-thread. No engine-owned pointers returned. */
    KilnResult(KILN_CALL* recipe_raw_inputs)(uint32_t id, KilnGameplayRawRecipe* out);
} KilnGameplayApi;""")

replace("native/services/gameplay/internal.h",
"""KilnResult recipe(uint32_t id, KilnGameplayRecipe* out);""",
"""KilnResult recipe(uint32_t id, KilnGameplayRecipe* out);
KilnResult recipe_raw_inputs(uint32_t id, KilnGameplayRawRecipe* out);""")

replace("native/services/gameplay/recipes.cpp", """struct ListCtx {""",
"""struct RawCtx {
    uintptr_t reg;
    uint32_t id;
    KilnGameplayRawRecipe* out;
    bool found, sane;
};

void copy_raw_inputs(void* arg) {
    auto* c = static_cast<RawCtx*>(arg);
    const uint8_t* recipe_info = g_find(c->id, c->reg);
    c->found = recipe_info != nullptr;
    c->sane = false;
    if (!c->found) return;
    const uint8_t* field = recipe_info + 0x28;
    int32_t rel = 0;
    uint32_t count = 0;
    std::memcpy(&rel, field, 4);
    std::memcpy(&count, field + 4, 4);
    if (count > 64) return; // same sanity bound as the existing reader
    c->sane = true;
    c->out->id = c->id;
    c->out->stride = KILN_GAMEPLAY_RAW_INPUT_STRIDE;
    c->out->input_count = count < KILN_GAMEPLAY_MAX_OUTPUTS ? count : KILN_GAMEPLAY_MAX_OUTPUTS;
    if (!c->out->input_count) return;
    const uint8_t* src = field + rel;
    for (uint32_t i = 0; i < c->out->input_count; ++i)
        std::memcpy(c->out->inputs + i * KILN_GAMEPLAY_RAW_INPUT_STRIDE,
                    src + i * KILN_GAMEPLAY_RAW_INPUT_STRIDE,
                    KILN_GAMEPLAY_RAW_INPUT_STRIDE);
}

struct ListCtx {""")

replace("native/services/gameplay/recipes.cpp",
"""KilnResult workshop_recipes(uint32_t workshop, uint32_t* out, uint32_t capacity, uint32_t* out_count) {""",
"""KilnResult recipe_raw_inputs(uint32_t id, KilnGameplayRawRecipe* out) {
    if (!out || out->size < sizeof(KilnGameplayRawRecipe) || !id)
        return KILN_ERR_INVALID_ARGUMENT;
    if (!g_ok) return KILN_ERR_UNAVAILABLE;
    const uintptr_t reg = registry();
    if (!reg) return KILN_ERR_UNAVAILABLE;
    KilnGameplayRawRecipe raw{};
    raw.size = sizeof raw;
    RawCtx c{reg, id, &raw, false, false};
    unsigned long code = 0;
    if (!seh_call(&copy_raw_inputs, &c, &code)) return KILN_ERR_INTERNAL;
    if (!c.found) return KILN_ERR_NOT_FOUND;
    if (!c.sane) return KILN_ERR_INTERNAL;
    *out = raw;
    return KILN_OK;
}

KilnResult workshop_recipes(uint32_t workshop, uint32_t* out, uint32_t capacity, uint32_t* out_count) {""")

replace("native/services/gameplay/gameplay.cpp",
"""KilnResult KILN_CALL t_workshop_recipes(uint32_t ws, uint32_t* out, uint32_t cap, uint32_t* n) {""",
"""KilnResult KILN_CALL t_recipe_raw_inputs(uint32_t id, KilnGameplayRawRecipe* out) {
    if (!tick_ok()) return KILN_ERR_WRONG_THREAD;
    return recipe_raw_inputs(id, out);
}
KilnResult KILN_CALL t_workshop_recipes(uint32_t ws, uint32_t* out, uint32_t cap, uint32_t* n) {""")
replace("native/services/gameplay/gameplay.cpp",
"""    &t_spawn_replace_clear,
};""",
"""    &t_spawn_replace_clear,
    &t_recipe_raw_inputs,
};""")
print("PASS native read-only raw-recipe probe installed in disposable Kiln checkout.")
