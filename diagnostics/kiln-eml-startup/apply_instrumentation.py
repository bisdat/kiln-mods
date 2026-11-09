#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Apply observational EML module-load diagnostics to pinned Kiln source.

Input: checkout of frostdev-ops/kiln d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63.
Only logging, additional read-only metadata on failure, and timing change.
File open, containment, symlink and error-return semantics stay unchanged.
"""
from pathlib import Path
import sys

if len(sys.argv) != 2:
    sys.exit("usage: apply_instrumentation.py <kiln-repository-root>")
base = Path(sys.argv[1]) / "gpl" / "kiln-eml"

def change(path: str, original: str, replacement: str) -> None:
    p = base / path
    content = p.read_text()
    if content.count(original) != 1:
        sys.exit(f"refusing unexpected source layout: {p} (match count {content.count(original)})")
    p.write_text(content.replace(original, replacement, 1))

fs = "eml/mod-loader-base/src/registry/fs.rs"
change(
    fs,
    "use std::{collections::HashMap, fs::File, io::{BufReader, Error, ErrorKind, Read, Result}};",
    "use std::{collections::HashMap, fs::File, io::{BufReader, Error, ErrorKind, Read, Result}, time::{Instant, SystemTime, UNIX_EPOCH}};",
)
change(
    fs,
    """    fn read_file(
        &mut self,
        path: &Path
    ) -> Result<Box<dyn FileRead>> {
        let path = sanitize_path(&self.root, path)?;

        if !path.is_file() {
            return Err(Error::from(ErrorKind::NotFound));
        }

        Ok(Box::new(File::open(path)?))
    }""",
    """    fn read_file(
        &mut self,
        path: &Path
    ) -> Result<Box<dyn FileRead>> {
        // kiln-eml diagnostic: the previous log only said read_file=false.
        // Log stages for Lua modules without changing any access decisions.
        let started = Instant::now();
        let module = path.as_str().ends_with(".lua");
        if module {
            let time_ms = SystemTime::now().duration_since(UNIX_EPOCH)
                .unwrap_or_default().as_millis();
            tracing::info!(
                target: "eml::base",
                event = "lua_file_read_start",
                root = %self.root,
                requested = %path,
                candidate = %self.root.join(path),
                time_ms,
                "EML Lua module read requested"
            );
        }
        let resolved = match sanitize_path(&self.root, path) {
            Ok(p) => p,
            Err(e) => {
                if module {
                    tracing::error!(
                        target: "eml::base",
                        event = "lua_file_read_failed",
                        stage = "sanitize_path",
                        requested = %path,
                        root = %self.root,
                        kind = ?e.kind(),
                        os_error = ?e.raw_os_error(),
                        elapsed_ms = started.elapsed().as_millis(),
                        "EML Lua module path validation failed"
                    );
                }
                return Err(e);
            }
        };

        if !resolved.is_file() {
            if module {
                let metadata = std::fs::metadata(&resolved)
                    .map(|m| format!("file={} size={}", m.is_file(), m.len()))
                    .unwrap_or_else(|e| format!("error={:?} os={:?}", e.kind(), e.raw_os_error()));
                tracing::error!(
                    target: "eml::base",
                    event = "lua_file_read_failed",
                    stage = "is_file",
                    requested = %path,
                    resolved = %resolved,
                    metadata = %metadata,
                    elapsed_ms = started.elapsed().as_millis(),
                    "EML Lua module candidate is not a regular file"
                );
            }
            return Err(Error::from(ErrorKind::NotFound));
        }

        let file = match File::open(&resolved) {
            Ok(file) => file,
            Err(e) => {
                if module {
                    tracing::error!(
                        target: "eml::base",
                        event = "lua_file_read_failed",
                        stage = "open",
                        requested = %path,
                        resolved = %resolved,
                        kind = ?e.kind(),
                        os_error = ?e.raw_os_error(),
                        elapsed_ms = started.elapsed().as_millis(),
                        "EML Lua file open failed"
                    );
                }
                return Err(e);
            }
        };
        if module {
            tracing::info!(
                target: "eml::base",
                event = "lua_file_read_success",
                requested = %path,
                resolved = %resolved,
                elapsed_ms = started.elapsed().as_millis(),
                "EML Lua file opened"
            );
        }
        Ok(Box::new(file))
    }""",
)
change(
    fs,
    """    let path = root.join(path).canonicalize_utf8()?;

    if !path.starts_with(root) {
        return Err(Error::from(ErrorKind::PermissionDenied));
    }

    if path.is_symlink() {
        return Err(Error::from(ErrorKind::PermissionDenied));
    }

    Ok(path)""",
    """    let candidate = root.join(path);
    let path = match candidate.canonicalize_utf8() {
        Ok(p) => p,
        Err(e) => {
            if path.as_str().ends_with(".lua") {
                // Only on failure: record whether the file or its parent
                // vanished after mod discovery or is inaccessible in Wine.
                let probe = |p: &Path| std::fs::metadata(p)
                    .map(|m| format!("file={} dir={} size={}", m.is_file(), m.is_dir(), m.len()))
                    .unwrap_or_else(|e| format!("error={:?} os={:?}", e.kind(), e.raw_os_error()));
                let parent = candidate.parent().map(probe)
                    .unwrap_or_else(|| "no parent".into());
                tracing::error!(
                    target: "eml::base",
                    event = "lua_path_canonicalize_failed",
                    stage = "canonicalize",
                    root = %root,
                    requested = %path,
                    candidate = %candidate,
                    candidate_metadata = %probe(&candidate),
                    parent_metadata = %parent,
                    kind = ?e.kind(),
                    os_error = ?e.raw_os_error(),
                    "EML Lua module path canonicalization failed"
                );
            }
            return Err(e);
        }
    };

    if !path.starts_with(root) {
        if path.as_str().ends_with(".lua") {
            tracing::warn!(
                target: "eml::base", event = "lua_path_denied",
                stage = "outside_root", requested = %path, root = %root,
                "EML module path outside authorized root"
            );
        }
        return Err(Error::from(ErrorKind::PermissionDenied));
    }

    if path.is_symlink() {
        if path.as_str().ends_with(".lua") {
            tracing::warn!(
                target: "eml::base", event = "lua_path_denied",
                stage = "symlink", requested = %path, root = %root,
                "EML module path is a symlink"
            );
        }
        return Err(Error::from(ErrorKind::PermissionDenied));
    }

    Ok(path)""",
)

state = "eml/mod-loader-lua/src/runner/state.rs"
change(
    state,
    """            let mut fs = mod_state.fs();
            let reader = fs.read_file(&path)
                .map_err(|e| LuaError::module_load(&path, e))?;
            let chunk = std::io::read_to_string(reader)
                .map_err(|e| LuaError::module_load(&path, e))?;

            drop(fs);""",
    """            let mut fs = mod_state.fs();
            if path.as_str() == "src/mod.lua" {
                tracing::info!(
                    target: "eml::lua",
                    event = "eml_entrypoint_require",
                    mod_id = %mod_state.info().id,
                    qualified_id = %qualified_id,
                    root = %fs.root(),
                    requested = %path,
                    "EML entrypoint requested after game-data preparation"
                );
            }
            let reader = fs.read_file(&path).map_err(|e| {
                tracing::error!(
                    target: "eml::lua",
                    event = "eml_module_open_error",
                    mod_id = %mod_state.info().id,
                    qualified_id = %qualified_id,
                    requested = %path,
                    kind = ?e.kind(),
                    os_error = ?e.raw_os_error(),
                    "EML module read_file returned an error"
                );
                LuaError::module_load(&path, e)
            })?;
            let chunk = std::io::read_to_string(reader).map_err(|e| {
                tracing::error!(
                    target: "eml::lua",
                    event = "eml_module_content_error",
                    mod_id = %mod_state.info().id,
                    requested = %path,
                    kind = ?e.kind(),
                    os_error = ?e.raw_os_error(),
                    "EML module opened but could not be read as UTF-8"
                );
                LuaError::module_load(&path, e)
            })?;

            if path.as_str() == "src/mod.lua" {
                tracing::info!(
                    target: "eml::lua",
                    event = "eml_entrypoint_read_success",
                    mod_id = %mod_state.info().id,
                    requested = %path,
                    bytes = chunk.len(),
                    "EML Lua module text successfully loaded"
                );
            }
            drop(fs);""",
)
print("Applied EML startup-stage diagnostics to pinned source; no IO semantics changed")
