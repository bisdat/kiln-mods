#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Inject performance and safety regressions into pinned EML cache module."""
from pathlib import Path
import sys
if len(sys.argv)!=2: sys.exit("usage: add_tests.py <pinned kiln checkout>")
p=Path(sys.argv[1])/"gpl/kiln-eml/eml/mod-loader-lua/src/cache/mod.rs"
s=p.read_text()
if "fn strong_change_stamp_reuses_unchanged_crc" in s: sys.exit("already patched")
s += r'''

#[cfg(test)]
mod fingerprint_reuse_tests {
    use super::*;
    use std::{fs, io::{Read, Write}, time::{Duration, Instant}};
    fn temp() -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "eml-reuse-{}", uuid::Uuid::new_v4()
        ));
        fs::create_dir(&dir).unwrap();
        dir
    }
    fn assert_crc_changed_on_preserved_mtime(path: &Path) {
        let initial_time = fs::metadata(path).unwrap().modified().unwrap();
        let mut first = FileStateCache::default();
        first.track(path);
        assert!(first.files.contains_key(path));
        // A real change of bytes with EXACTLY the same file length and mtime.
        // This is the core Kiln integrity requirement.
        std::thread::sleep(Duration::from_millis(40));
        let mut f = File::options().write(true).open(path).unwrap();
        f.write_all(b"CRITICAL-NEW-DATA").unwrap();
        f.set_modified(initial_time).unwrap();
        drop(f);
        let mut second = FileStateCache::default();
        second.seed_reuse_from(&first);
        second.track(path);
        assert!(second.diff(&first).is_some(),
            "A same-size/preserved-mtime edit MUST invalidate CRC cache");
        assert!(second.hash_bytes_read() > 0,
            "Modified bytes MUST be rehashed, never reuse stale CRC");
    }

    #[test]
    fn strong_change_stamp_reuses_unchanged_crc() {
        let dir=temp();
        let path=dir.join("large.dat");
        fs::write(&path, vec![7u8; 16*1024*1024]).unwrap();
        let mut first=FileStateCache::default();
        let cold=Instant::now();
        first.track(&path);
        let cold_ms=cold.elapsed().as_millis();
        assert_eq!(first.hash_bytes_read(), 16*1024*1024);
        let mut warm=FileStateCache::default();
        warm.seed_reuse_from(&first);
        let started=Instant::now();
        warm.track(&path);
        let warm_ms=started.elapsed().as_millis();
        assert!(warm.diff(&first).is_none());
        if first.stamps.contains_key(&path) {
            assert_eq!(warm.hash_bytes_read(), 0,
                "Strong stamp available: unchanged file must avoid CRC scan");
            assert_eq!(warm.reused_files(), 1);
        } else {
            assert_eq!(warm.hash_bytes_read(), 16*1024*1024,
                "Unsupported stamp must fall back to whole-file CRC");
        }
        println!(
            "EML fingerprint synthetic 16 MiB: cold_ms={cold_ms} warm_ms={warm_ms} cold_bytes={} warm_bytes={} reused_files={} strong_stamp={}",
            first.hash_bytes_read(), warm.hash_bytes_read(), warm.reused_files(),
            first.stamps.contains_key(&path)
        );
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn preserved_timestamp_content_edit_requires_new_crc() {
        let dir=temp(); let path=dir.join("data.dat");
        fs::write(&path, b"CRITICAL-OLD-DATA").unwrap();
        assert_crc_changed_on_preserved_mtime(&path);
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn replacing_an_identical_size_file_requires_new_crc() {
        let dir=temp(); let path=dir.join("data.dat");
        let replacement=dir.join("replacement.tmp");
        fs::write(&path, b"CRITICAL-OLD-DATA").unwrap();
        let old_time=fs::metadata(&path).unwrap().modified().unwrap();
        let mut first=FileStateCache::default(); first.track(&path);
        fs::write(&replacement, b"CRITICAL-NEW-DATA").unwrap();
        File::options().write(true).open(&replacement).unwrap().set_modified(old_time).unwrap();
        fs::rename(&replacement, &path).unwrap();
        let mut next=FileStateCache::default(); next.seed_reuse_from(&first);
        next.track(&path);
        assert!(next.diff(&first).is_some(), "Replacement must invalidate CRC");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn deleted_or_unreadable_file_is_never_a_cache_hit() {
        let dir=temp(); let path=dir.join("data.dat");
        fs::write(&path, b"CRITICAL-OLD-DATA").unwrap();
        let mut first=FileStateCache::default(); first.track(&path);
        fs::remove_file(&path).unwrap();
        let mut next=FileStateCache::default(); next.seed_reuse_from(&first);
        next.track(&path);
        next.configuration("options");
        assert!(next.diff(&next).is_some(),
            "A failed metadata/open must poison build ID");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn persisted_crc_stamps_roundtrip_and_old_cache_falls_back() {
        let dir=temp(); let path=dir.join("data.dat");
        fs::write(&path, vec![1u8; 1024*1024]).unwrap();
        let mut before=FileStateCache::default(); before.track(&path);
        let json=serde_json::to_string(&before).unwrap();
        let decoded:FileStateCache=serde_json::from_str(&json).unwrap();
        let mut warm=FileStateCache::default(); warm.seed_reuse_from(&decoded);
        warm.track(&path);
        assert!(before.diff(&warm).is_none());
        let mut old:serde_json::Value=serde_json::from_str(&json).unwrap();
        old.as_object_mut().unwrap().remove("stamps");
        let old:FileStateCache=serde_json::from_value(old).unwrap();
        let mut fallback=FileStateCache::default();
        fallback.seed_reuse_from(&old);
        fallback.track(&path);
        assert_eq!(fallback.hash_bytes_read(), 1024*1024,
            "Old cache without independent change stamp MUST rehash");
        fs::remove_dir_all(dir).unwrap();
    }
}
'''
p.write_text(s)
print("Injected six cache correctness/performance checks")
