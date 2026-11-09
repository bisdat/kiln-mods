// SPDX-License-Identifier: MIT
//! Read-only EML filesystem diagnostic for frostdev-ops/kiln#4.
//! Compile against upstream Kiln's *pinned* mod-loader-base crate.
//! This executable NEVER modifies a mod, a Library entry, or game data.

use std::{
    env,
    fs,
    io::{self, Read},
    path::{Path, PathBuf},
    process::ExitCode,
    time::Instant,
};
use camino::Utf8PathBuf;
use mod_loader_base::FileSystem;

fn show_metadata(label: &str, path: &Path) {
    match fs::metadata(path) {
        Ok(m) => println!(
            "{label}: metadata OK (file={}, dir={}, bytes={})",
            m.is_file(),
            m.is_dir(),
            m.len()
        ),
        Err(e) => println!("{label}: metadata FAILED ({:?}, os={:?}: {e})", e.kind(), e.raw_os_error()),
    }
    match fs::canonicalize(path) {
        Ok(p) => println!("{label}: canonicalize OK ({})", p.display()),
        Err(e) => println!("{label}: canonicalize FAILED ({:?}, os={:?}: {e})", e.kind(), e.raw_os_error()),
    }
}

fn eml_read(fs: &mut FileSystem, entry: &str) -> io::Result<usize> {
    let mut source = fs.read_file(entry)?;
    let mut bytes = Vec::new();
    source.read_to_end(&mut bytes)?;
    Ok(bytes.len())
}

fn show_read(label: &str, result: io::Result<usize>) -> bool {
    match result {
        Ok(bytes) => {
            println!("{label}: read OK ({bytes} bytes)");
            true
        }
        Err(e) => {
            println!("{label}: read FAILED ({:?}, os={:?}: {e})", e.kind(), e.raw_os_error());
            false
        }
    }
}

fn probe(argument: &Path) -> bool {
    println!("===== EML READ-ONLY PROBE =====");
    println!("input root: {}", argument.display());
    show_metadata("root", argument);
    let manifest = argument.join("mod.json");
    let nested = argument.join("src/mod.lua");
    show_metadata("manifest", &manifest);
    show_metadata("entrypoint", &nested);
    let direct_ok = show_read(
        "direct src/mod.lua",
        fs::read(&nested).map(|data| data.len()),
    );
    // Exactly the path used by the pinned EML reader: canonical root,
    // FileSystem::new_disk and read_file("src/mod.lua").
    let canonical = match fs::canonicalize(argument) {
        Ok(path) => path,
        Err(e) => {
            println!("root canonicalization blocks EML ({:?}, os={:?}: {e})", e.kind(), e.raw_os_error());
            return false;
        }
    };
    let canonical = match Utf8PathBuf::from_path_buf(canonical) {
        Ok(path) => path,
        Err(_) => {
            println!("root is not UTF-8; EML cannot use this path");
            return false;
        }
    };
    let started = Instant::now();
    let mut eml = match FileSystem::new_disk(&canonical) {
        Ok(fs) => fs,
        Err(e) => {
            println!("EML new_disk FAILED ({:?}, os={:?}: {e})", e.kind(), e.raw_os_error());
            return false;
        }
    };
    let manifest_ok = show_read("EML mod.json", eml_read(&mut eml, "mod.json"));
    let nested_ok = show_read("EML src/mod.lua", eml_read(&mut eml, "src/mod.lua"));
    println!("EML read duration: {} ms", started.elapsed().as_millis());
    println!("RESULT: {}", if direct_ok && manifest_ok && nested_ok { "PASS" } else { "FAIL" });
    direct_ok && manifest_ok && nested_ok
}

fn main() -> ExitCode {
    let roots: Vec<PathBuf> = env::args_os().skip(1).map(PathBuf::from).collect();
    if roots.is_empty() {
        eprintln!("Usage: eml-readonly-probe.exe <Windows mod Library directory> [second directory]");
        eprintln!("Example root: Z:\\home\\user\\.local\\share\\kiln\\library\\eml\\flame-altar-expanded\\0.1.1+hash");
        return ExitCode::from(2);
    }
    let mut all_ok = true;
    for root in &roots {
        if !probe(root) {
            all_ok = false;
        }
    }
    ExitCode::from(if all_ok { 0 } else { 1 })
}
