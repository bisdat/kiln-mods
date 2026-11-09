#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Add precise read-only progress reporting to pinned EML file-state CRC scans.

Run after apply_instrumentation.py and apply_preload_diagnostics.py.
No cache format, fingerprint bytes, hash algorithm, read decision, or errors change.
"""
from pathlib import Path
import sys

if len(sys.argv) != 2:
    sys.exit("usage: apply_game_file_progress.py <kiln-repository-root>")
base = Path(sys.argv[1]) / "gpl/kiln-eml/eml/mod-loader-lua/src"

def change(path: str, original: str, replacement: str):
    target = base / path
    source = target.read_text()
    if source.count(original) != 1:
        sys.exit(f"expected exactly one pinned source anchor: {target}: {original[:80]!r}, found {source.count(original)}")
    target.write_text(source.replace(original, replacement, 1))

change("cache/track.rs",
'''            if let Some(ext) = extension {
                if matches!(ext, "dat" | "kfc" | "kfc_resources" | "exe" | "bak" | "integrity") || path.file_name().is_some_and(|n| n == "eml.json") {
                    self.track(path);
                }
            }''',
'''            if let Some(ext) = extension {
                if matches!(ext, "dat" | "kfc" | "kfc_resources" | "exe" | "bak" | "integrity") || path.file_name().is_some_and(|n| n == "eml.json") {
                    // kiln-eml diagnostic: track the precise file currently
                    // being read, not just the encompassing game-directory scan.
                    let started = std::time::Instant::now();
                    let file_bytes = std::fs::metadata(&path).ok().map(|m| m.len());
                    tracing::info!(
                        target: "eml::lua",
                        event = "eml_game_file_fingerprint_start",
                        file = %path.display(),
                        bytes = ?file_bytes,
                        "EML game file fingerprint start"
                    );
                    self.track(&path);
                    tracing::info!(
                        target: "eml::lua",
                        event = "eml_game_file_fingerprint_done",
                        file = %path.display(),
                        bytes = ?file_bytes,
                        elapsed_ms = started.elapsed().as_millis(),
                        "EML game file fingerprint completed"
                    );
                }
            }''')
change("cache/mod.rs",
'''            if m.is_file() {
                let mut f = std::fs::File::open(&path)?;
                let mut buf = [0u8; 65536];
                loop { let n = f.read(&mut buf)?; if n == 0 { break; } digest.update(&buf[..n]); }
            }
            Ok((m.len(), nanos, digest.finalize()))''',
'''            if m.is_file() {
                let mut f = std::fs::File::open(&path)?;
                let mut buf = [0u8; 65536];
                // kiln-eml diagnostic: only report extra progress for files
                // >= 128 MiB. The same read() and CRC digest operations remain.
                let show_progress = m.len() >= 128 * 1024 * 1024;
                let mut bytes_read = 0u64;
                let mut next_report = 128 * 1024 * 1024u64;
                let started = std::time::Instant::now();
                loop {
                    let n = f.read(&mut buf)?;
                    if n == 0 { break; }
                    digest.update(&buf[..n]);
                    if show_progress {
                        bytes_read += n as u64;
                        if bytes_read >= next_report {
                            tracing::info!(
                                target: "eml::lua",
                                event = "eml_large_file_hash_progress",
                                file = %path.display(),
                                bytes_read,
                                total_bytes = m.len(),
                                elapsed_ms = started.elapsed().as_millis(),
                                "EML CRC-64 fingerprint progress"
                            );
                            next_report = bytes_read.saturating_add(128 * 1024 * 1024);
                        }
                    }
                }
            }
            Ok((m.len(), nanos, digest.finalize()))''')
print("Pinned EML game-file CRC progress diagnostics applied without changing fingerprint rules")
