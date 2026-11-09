#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Add read-only stage timing to the pinned EML Lua run before require().

Apply AFTER apply_instrumentation.py to upstream revision d35ee27.
The patch inserts tracing only; no cache, game-data or transaction semantics change.
"""
from pathlib import Path
import sys

if len(sys.argv) != 2:
    sys.exit("usage: apply_preload_diagnostics.py <kiln-repository-root>")
target = Path(sys.argv[1]) / "gpl/kiln-eml/eml/mod-loader-lua/src/lib.rs"
s = target.read_text()

def sub(old: str, new: str):
    global s
    count = s.count(old)
    if count != 1:
        sys.exit(f"Unexpected pinned EML source in {target}: {count} matches for {old[:60]!r}")
    s = s.replace(old, new, 1)

sub(
'''    info!("Running lua with options: {:?}", args);

    let cache_key''',
'''    info!("Running lua with options: {:?}", args);

    // Diagnostic only: the original stall occurs *before* "Running mods...".
    // Identify exactly which synchronous preparation stage was last reached.
    let preload_start = std::time::Instant::now();
    macro_rules! preload_checkpoint {
        ($stage:literal) => {
            tracing::info!(
                target: "eml::lua",
                event = "eml_preload_checkpoint",
                stage = $stage,
                pid = std::process::id(),
                elapsed_ms = preload_start.elapsed().as_millis(),
                "EML preparation checkpoint"
            );
        };
    }
    let epoch_ms = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default().as_millis();
    tracing::info!(
        target: "eml::lua",
        event = "eml_run_identity",
        pid = std::process::id(),
        epoch_ms,
        mod_path_count = env.mod_paths().len(),
        "EML run identity"
    );

    let cache_key'''
)
sub('''    track_mods(&mut mods_before, env);

    // check cache if files have changed''',
'''    preload_checkpoint!("track_initial_mods_start");
    track_mods(&mut mods_before, env);
    preload_checkpoint!("track_initial_mods_done");

    // check cache if files have changed''')
sub('''        let current_cache = FileStateCache::read(env.cache_dir());
        let mut new_cache''',
'''        preload_checkpoint!("read_previous_cache_start");
        let current_cache = FileStateCache::read(env.cache_dir());
        preload_checkpoint!("read_previous_cache_done");
        let mut new_cache''')
sub('''        new_cache.track_game_files(env.game_dir());
        track_mods(&mut new_cache, env);

        let cache_diff''',
'''        preload_checkpoint!("track_game_files_start");
        new_cache.track_game_files(env.game_dir());
        preload_checkpoint!("track_game_files_done");
        preload_checkpoint!("track_current_mods_start");
        track_mods(&mut new_cache, env);
        preload_checkpoint!("track_current_mods_done");

        let cache_diff''')
sub('''    // create a new app state

    let app_state = match AppState::new(''',
'''    // create a new app state

    preload_checkpoint!("app_state_new_start");
    let app_state = match AppState::new(''')
sub('''    let vanilla_containers = app_state.kfc_file().containers().len();
    let mut inputs_before''',
'''    preload_checkpoint!("app_state_new_done");
    let vanilla_containers = app_state.kfc_file().containers().len();
    let mut inputs_before''')
sub('''    inputs_before.track_game_inputs(env.game_dir().as_std_path(), &file_name, vanilla_containers);

    // setup and run all mods''',
'''    preload_checkpoint!("track_game_inputs_start");
    inputs_before.track_game_inputs(env.game_dir().as_std_path(), &file_name, vanilla_containers);
    preload_checkpoint!("track_game_inputs_done");

    // setup and run all mods''')
sub('''    let runner = LuaModRunner::new(app_state)?;

    match runner.setup(env.mod_registry()) {''',
'''    preload_checkpoint!("lua_runner_new_start");
    let runner = LuaModRunner::new(app_state)?;
    preload_checkpoint!("lua_runner_new_done");

    preload_checkpoint!("lua_runner_setup_start");
    match runner.setup(env.mod_registry()) {''')
sub('''    info!("Running mods...");

    // TODO: move this somewhere else, maybe to the GameContext?''',
'''    preload_checkpoint!("lua_runner_setup_done");
    info!("Running mods...");

    // TODO: move this somewhere else, maybe to the GameContext?''')
sub('''    runner.run()?;

    let app_state = runner.lua.app_data_ref::<AppState>().unwrap();''',
'''    preload_checkpoint!("lua_modules_start");
    runner.run()?;
    preload_checkpoint!("lua_modules_done");

    let app_state = runner.lua.app_data_ref::<AppState>().unwrap();''')
target.write_text(s)
print("Inserted EML pre-Lua-stage checkpoints in pinned mod-loader-lua/src/lib.rs")
