// SPDX-License-Identifier: MIT
// Isolated standard-library reproducer for upstream frostdev-ops/kiln#4.
// This is NOT a mod and has no interaction with Enshrouded or its saves.
use std::{
    fs,
    io::{self, ErrorKind},
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

fn build_native_path(root: &Path, relative: &Path) -> PathBuf {
    // Under \\?\ Windows verbatim paths, a slash inside a pushed string
    // may not be interpreted as a directory separator. Parse while the
    // relative path still has ordinary Windows semantics, then push each
    // segment separately.
    if relative.is_absolute() || relative.has_root() {
        return root.join(relative); // preserve old semantics for rooted input
    }
    let mut candidate = root.to_path_buf();
    for component in relative.components() {
        candidate.push(component.as_os_str());
    }
    candidate
}

fn safe_path(root: &Path, relative: &Path) -> io::Result<PathBuf> {
    let candidate = build_native_path(root, relative);
    let resolved = fs::canonicalize(candidate)?;
    if !resolved.starts_with(root) || resolved.is_symlink() {
        return Err(io::Error::from(ErrorKind::PermissionDenied));
    }
    Ok(resolved)
}

struct Fixture {
    base: PathBuf,
    root: PathBuf,
}

impl Fixture {
    fn create() -> io::Result<Self> {
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be after the epoch")
            .as_nanos();
        let base = std::env::temp_dir()
            .join(format!("kiln-eml-path-{}-{}", std::process::id(), now));
        let raw_root = base.join("eml").join("flame-altar-expanded").join("0.1.1+hash");
        fs::create_dir_all(raw_root.join("src"))?;
        fs::write(raw_root.join("mod.json"), b"{\"id\":\"flame-altar-expanded\"}")?;
        fs::write(raw_root.join("src").join("mod.lua"), b"return { valid = true }\n")?;
        fs::write(base.join("outside.lua"), b"should not be reachable")?;
        let root = fs::canonicalize(raw_root)?;
        Ok(Self { base, root })
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.base);
    }
}

#[test]
fn root_manifest_and_nested_lua_module_both_open() -> io::Result<()> {
    let f = Fixture::create()?;
    #[cfg(windows)]
    assert!(
        f.root.to_string_lossy().starts_with(r"\\?\"),
        "Windows canonicalisation must produce a verbatim path: {:?}",
        f.root
    );

    assert_eq!(
        fs::read(safe_path(&f.root, Path::new("mod.json"))?)?,
        br#"{"id":"flame-altar-expanded"}"#
    );

    let old_candidate = f.root.join("src/mod.lua");
    let new_candidate = build_native_path(&f.root, Path::new("src/mod.lua"));
    eprintln!("canonical root: {:?}", f.root);
    eprintln!("old root.join(src/mod.lua): {:?}", old_candidate);
    eprintln!("component-by-component: {:?}", new_candidate);
    eprintln!("old path read: {:?}", fs::read(&old_candidate).map(|b| b.len()));

    #[cfg(windows)]
    assert!(
        !new_candidate.to_string_lossy().contains('/'),
        "verbatim path must not contain /: {:?}",
        new_candidate
    );

    assert_eq!(
        fs::read(safe_path(&f.root, Path::new("src/mod.lua"))?)?,
        b"return { valid = true }\n"
    );
    Ok(())
}

#[test]
fn parent_escape_remains_blocked() -> io::Result<()> {
    let f = Fixture::create()?;
    let err = safe_path(&f.root, Path::new("../../../outside.lua")).unwrap_err();
    assert!(
        matches!(err.kind(), ErrorKind::PermissionDenied | ErrorKind::NotFound),
        "outside-root path must fail, got {err}"
    );
    Ok(())
}

#[test]
fn absolute_escape_remains_blocked() -> io::Result<()> {
    let f = Fixture::create()?;
    let outside = fs::canonicalize(f.base.join("outside.lua"))?;
    assert_eq!(
        safe_path(&f.root, &outside).unwrap_err().kind(),
        ErrorKind::PermissionDenied
    );
    Ok(())
}

#[cfg(unix)]
#[test]
fn symlink_escape_remains_blocked() -> io::Result<()> {
    let f = Fixture::create()?;
    std::os::unix::fs::symlink(&f.base, f.root.join("escape"))?;
    assert_eq!(
        safe_path(&f.root, Path::new("escape/outside.lua")).unwrap_err().kind(),
        ErrorKind::PermissionDenied
    );
    Ok(())
}
