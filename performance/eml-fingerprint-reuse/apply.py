#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Apply an opt-in-by-capability identity+change-stamp fast reuse of CRCs.

Never trust size/mtime alone. A full CRC read is mandatory without a stable
file identity AND independently updated change stamp. A checksum cache that
predates this schema causes a one-time rehash. Applies to pinned upstream Kiln.
"""
from pathlib import Path
import sys
if len(sys.argv) != 2:
    sys.exit("usage: apply.py <pinned kiln checkout>")
root=Path(sys.argv[1])/"gpl/kiln-eml/eml/mod-loader-lua/src"
def change(path,old,new):
    p=root/path
    s=p.read_text()
    if s.count(old)!=1:
        raise SystemExit(f"Unexpected pinned source in {path}: {s.count(old)} matches for {old[:80]!r}")
    p.write_text(s.replace(old,new,1))

change("cache/mod.rs",
    "use std::{collections::HashMap, path::PathBuf};",
    "use std::{collections::HashMap, fs::{File, Metadata}, path::{Path, PathBuf}};")

change("cache/mod.rs",
'''const BUILD_ID: &str = env!("BUILD_ID");
''',
'''const BUILD_ID: &str = env!("BUILD_ID");

// Strong file identity, NOT merely size or last-write time.
// On Windows the ChangeTime and persistent FileId are queried by handle.
// Wine may not support either API; in that case we ALWAYS read and hash.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
struct ChangeStamp {
    volume: u64,
    file_id: u128,
    change_time: i128,
}

// Native Linux: inode+device and ctime detect edits even when mtime is reset.
#[cfg(unix)]
fn change_stamp(file: &File) -> Option<ChangeStamp> {
    use std::os::unix::fs::MetadataExt;
    let m = file.metadata().ok()?;
    Some(ChangeStamp {
        volume: m.dev(),
        file_id: m.ino() as u128,
        change_time: (m.ctime() as i128) * 1_000_000_000 + m.ctime_nsec() as i128,
    })
}

#[cfg(windows)]
fn change_stamp(file: &File) -> Option<ChangeStamp> {
    use std::{ffi::c_void, os::windows::io::AsRawHandle};
    #[repr(C)]
    struct Basic {
        created: i64, accessed: i64, written: i64, changed: i64, attributes: u32,
    }
    #[repr(C)]
    struct IdInfo { volume: u64, file_id: [u8; 16] }
    #[link(name="kernel32")]
    unsafe extern "system" {
        fn GetFileInformationByHandleEx(
            file: *mut c_void, class: i32, info: *mut c_void, size: u32,
        ) -> i32;
    }
    let handle = file.as_raw_handle();
    let mut basic = Basic { created: 0, accessed: 0, written: 0, changed: 0, attributes: 0 };
    let mut id = IdInfo { volume: 0, file_id: [0; 16] };
    let ok_basic = unsafe { GetFileInformationByHandleEx(
        handle, 0, &mut basic as *mut _ as *mut c_void,
        std::mem::size_of::<Basic>() as u32,
    ) } != 0;
    // FileIdInfo = FILE_INFO_BY_HANDLE_CLASS(18).
    let ok_id = unsafe { GetFileInformationByHandleEx(
        handle, 18, &mut id as *mut _ as *mut c_void,
        std::mem::size_of::<IdInfo>() as u32,
    ) } != 0;
    if !ok_basic || !ok_id || basic.changed == 0 { return None; }
    Some(ChangeStamp {
        volume: id.volume,
        file_id: u128::from_le_bytes(id.file_id),
        change_time: basic.changed as i128,
    })
}

#[cfg(not(any(unix, windows)))]
fn change_stamp(_file: &File) -> Option<ChangeStamp> { None }

fn modified_nanos(m: &Metadata) -> std::io::Result<u128> {
    m.modified()?.duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .map_err(std::io::Error::other)
}

// Detect replacement at the same path between the initial open and reuse.
// No stamp => no reuse. A fresh open also checks current access permissions.
fn path_still_same(path: &Path, expected: &ChangeStamp) -> bool {
    let Ok(m) = path.symlink_metadata() else { return false; };
    if !m.is_file() || m.file_type().is_symlink() { return false; }
    let Ok(f) = File::open(path) else { return false; };
    change_stamp(&f).as_ref() == Some(expected)
}
''')

change("cache/mod.rs",
'''    files: HashMap<PathBuf, (u64, u128, u64)>,
}''',
'''    files: HashMap<PathBuf, (u64, u128, u64)>,
    // Serialized with the CRC so next startup can safely reuse it only when
    // identity+change stamp AND size+mtime match. Old caches lack this field.
    #[serde(default)]
    stamps: HashMap<PathBuf, ChangeStamp>,
    // Work-in-progress reference set; never serialized to disk.
    #[serde(skip)]
    reusable: HashMap<PathBuf, ((u64, u128, u64), ChangeStamp)>,
    #[serde(skip)]
    hashed_bytes: u64,
    #[serde(skip)]
    reused_files: u64,
}''')
change("cache/mod.rs",
'''            files: HashMap::new(),
        }
    }
}

impl FileStateCache {

    pub fn configuration''',
'''            files: HashMap::new(),
            stamps: HashMap::new(),
            reusable: HashMap::new(),
            hashed_bytes: 0,
            reused_files: 0,
        }
    }
}

impl FileStateCache {

    // Stamps/CRCs from a previous complete snapshot may be reused by
    // another phase only when the OS guarantees no intervening modification.
    pub fn seed_reuse_from(&mut self, previous: &Self) {
        for (path, stamp) in &previous.stamps {
            if let Some(digest) = previous.files.get(path) {
                self.reusable.insert(path.clone(), (*digest, stamp.clone()));
            }
        }
    }

    pub fn hash_bytes_read(&self) -> u64 { self.hashed_bytes }
    pub fn reused_files(&self) -> u64 { self.reused_files }

    pub fn configuration''')
change("cache/mod.rs",
'''            files: HashMap::new(),
        }
    }

    pub fn track(''',
'''            files: HashMap::new(),
            stamps: HashMap::new(),
            reusable: HashMap::new(),
            hashed_bytes: 0,
            reused_files: 0,
        }
    }

    pub fn track(''')
change("cache/mod.rs",
'''        // kiln-eml: whole-second mtimes miss rapid edits, preserved timestamps and resource/backup changes.
        // Hash bytes as well; unreadable files poison this snapshot rather than producing a cache hit.
        use std::io::Read;
        let fingerprint = (|| -> std::io::Result<_> {
            let m = path.symlink_metadata()?;
            if m.file_type().is_symlink() { return Err(std::io::Error::other("symlink")); }
            let nanos = m.modified()?.duration_since(std::time::UNIX_EPOCH).map_err(std::io::Error::other)?.as_nanos();
            let crc = crc::Crc::<u64>::new(&crc::CRC_64_ECMA_182);
            let mut digest = crc.digest();
            if m.is_file() {
                let mut f = std::fs::File::open(&path)?;
                let mut buf = [0u8; 65536];
                loop { let n = f.read(&mut buf)?; if n == 0 { break; } digest.update(&buf[..n]); }
            }
            Ok((m.len(), nanos, digest.finalize()))
        })();
        let timestamp = match fingerprint {
            Ok(v) => v,
            Err(_) => { self.build_id.clear(); return; }
        };
        self.files.insert(path, timestamp);''',
'''        // Preserve the exact CRC-64 algorithm and fail-closed error behavior.
        // Fast reuse is permitted ONLY on a strong change stamp plus identity.
        use std::io::Read;
        let started = std::time::Instant::now();
        let fingerprint = (|| -> std::io::Result<_> {
            let m = path.symlink_metadata()?;
            if m.file_type().is_symlink() {
                return Err(std::io::Error::other("symlink"));
            }
            let nanos = modified_nanos(&m)?;
            let mut hash_bytes = 0u64;
            let mut reuse = false;
            let mut stamp = None;
            let crc = crc::Crc::<u64>::new(&crc::CRC_64_ECMA_182);
            let mut digest = crc.digest();
            if m.is_file() {
                let mut f = File::open(&path)?;
                let open_m = f.metadata()?;
                if open_m.len() != m.len() || modified_nanos(&open_m)? != nanos {
                    return Err(std::io::Error::other("file changed before fingerprint"));
                }
                let before = change_stamp(&f);
                if let (Some(s), Some((cached, old))) =
                    (before.as_ref(), self.reusable.get(&path)) {
                    if s == old && cached.0 == m.len() && cached.1 == nanos
                        && path_still_same(&path, s) {
                        reuse = true;
                        stamp = Some(s.clone());
                        return Ok((*cached, stamp, hash_bytes, reuse));
                    }
                }

                let mut buf = [0u8; 65536];
                loop {
                    let n = f.read(&mut buf)?;
                    if n == 0 { break; }
                    hash_bytes += n as u64;
                    digest.update(&buf[..n]);
                }
                // A file modified while we hash must NEVER produce a valid
                // fingerprint. This closes a race in the original EML code.
                let end_m = f.metadata()?;
                if end_m.len() != m.len() || modified_nanos(&end_m)? != nanos {
                    return Err(std::io::Error::other("file changed during fingerprint"));
                }
                if let Some(s) = &before {
                    if change_stamp(&f).as_ref() != Some(s)
                        || !path_still_same(&path, s) {
                        return Err(std::io::Error::other("file identity changed while fingerprinting"));
                    }
                    stamp = Some(s.clone());
                }
            }
            Ok(((m.len(), nanos, digest.finalize()), stamp, hash_bytes, reuse))
        })();
        let (timestamp, stamp, hash_bytes, reused) = match fingerprint {
            Ok(v) => v,
            Err(e) => {
                tracing::warn!(
                    target: "eml::lua",
                    event = "fingerprint_invalid",
                    path = %path.display(),
                    error = %e,
                    elapsed_ms = started.elapsed().as_millis(),
                    "File fingerprint invalid; disabling cache hit"
                );
                self.build_id.clear();
                return;
            }
        };
        self.hashed_bytes += hash_bytes;
        if reused { self.reused_files += 1; }
        if let Some(s) = stamp { self.stamps.insert(path.clone(), s); }
        self.files.insert(path, timestamp);''')

change("lib.rs",
'''    let cache_diff = if !args.options.skip_cache {
        let current_cache = FileStateCache::read(env.cache_dir());
        let mut new_cache = FileStateCache::default();
        new_cache.configuration(&cache_key);

        new_cache.track_game_files(env.game_dir());
        track_mods(&mut new_cache, env);

        let cache_diff = new_cache.diff(&current_cache);''',
'''    let (cache_diff, game_snapshot) = if !args.options.skip_cache {
        let current_cache = FileStateCache::read(env.cache_dir());
        let mut new_cache = FileStateCache::default();
        new_cache.seed_reuse_from(&current_cache);
        new_cache.configuration(&cache_key);

        new_cache.track_game_files(env.game_dir());
        track_mods(&mut new_cache, env);
        tracing::info!(
            target: "eml::lua",
            event = "fingerprint_initial_summary",
            hashed_bytes = new_cache.hash_bytes_read(),
            reused_files = new_cache.reused_files(),
            "EML initial game fingerprint summary"
        );
        let cache_diff = new_cache.diff(&current_cache);''')
change("lib.rs",
'''        cache_diff
    } else {
        info!("Skipping cache check");
        CacheDiff::new_dirty()
    };''',
'''        (cache_diff, Some(new_cache))
    } else {
        info!("Skipping cache check");
        (CacheDiff::new_dirty(), None)
    };''')
change("lib.rs",
'''    let mut inputs_before = FileStateCache::default();
    inputs_before.track_game_inputs(env.game_dir().as_std_path(), &file_name, vanilla_containers);''',
'''    let mut inputs_before = FileStateCache::default();
    if let Some(snapshot) = game_snapshot.as_ref() {
        inputs_before.seed_reuse_from(snapshot);
    }
    inputs_before.track_game_inputs(env.game_dir().as_std_path(), &file_name, vanilla_containers);''')
change("lib.rs",
'''        let mut inputs_now = FileStateCache::default();
        inputs_now.track_game_inputs(env.game_dir().as_std_path(), &file_name, vanilla_containers);''',
'''        let mut inputs_now = FileStateCache::default();
        inputs_now.seed_reuse_from(&inputs_before);
        inputs_now.track_game_inputs(env.game_dir().as_std_path(), &file_name, vanilla_containers);''')
# The final persisted snapshot is allowed to reuse hashes only when their
# independently updated change stamp is identical, even after publication.
change("lib.rs",
'''        let mut new_cache = mods_after.clone();
        new_cache.configuration(&cache_key);

        new_cache.track_game_files(env.game_dir());''',
'''        let mut new_cache = mods_after.clone();
        if let Some(snapshot) = game_snapshot.as_ref() {
            new_cache.seed_reuse_from(snapshot);
        }
        new_cache.seed_reuse_from(&inputs_now);
        new_cache.configuration(&cache_key);

        new_cache.track_game_files(env.game_dir());
        tracing::info!(
            target: "eml::lua",
            event = "fingerprint_final_summary",
            hashed_bytes = new_cache.hash_bytes_read(),
            reused_files = new_cache.reused_files(),
            "EML final game fingerprint summary"
        );''')
print("Applied change-stamp guarded CRC reuse with safe full-hash fallback")
