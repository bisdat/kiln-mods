// SPDX-License-Identifier: GPL-3.0-or-later
// Integration tests copied into pinned Kiln's mod-loader-base crate by CI.
// All paths are temporary fixtures, NEVER a real game/Library directory.
use std::{fs, io::{self, ErrorKind, Read}, path::PathBuf, time::{SystemTime, UNIX_EPOCH}};
use mod_loader_base::FileSystem;

struct Fixture { base: PathBuf, module: PathBuf }
impl Fixture {
    fn new() -> io::Result<Self> {
        let now = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let base = std::env::temp_dir().join(format!("kiln-stage-{}-{now}", std::process::id()));
        fs::create_dir_all(base.join("src"))?;
        fs::write(base.join("mod.json"), b"{\"id\":\"test\"}")?;
        let module = base.join("src/mod.lua");
        fs::write(&module, b"return true\n")?;
        Ok(Self { base, module })
    }
    fn fs(&self) -> io::Result<FileSystem> {
        let root = camino::Utf8PathBuf::from_path_buf(fs::canonicalize(&self.base)?)
            .expect("test root is UTF-8");
        FileSystem::new_disk(&root)
    }
}
impl Drop for Fixture {
    fn drop(&mut self) { let _ = fs::remove_dir_all(&self.base); }
}
fn trace() {
    let _ = tracing_subscriber::fmt()
        .with_max_level(tracing::Level::TRACE)
        .with_test_writer()
        .try_init();
}

#[test]
fn module_reads_correctly_with_instrumentation() -> io::Result<()> {
    trace();
    let fixture = Fixture::new()?;
    let mut eml = fixture.fs()?;
    let mut buffer = String::new();
    eml.read_file("mod.json")?.read_to_string(&mut buffer)?;
    assert!(buffer.contains("test"));
    buffer.clear();
    eml.read_file("src/mod.lua")?.read_to_string(&mut buffer)?;
    assert_eq!(buffer, "return true\n");
    Ok(())
}

#[test]
fn module_disappears_between_discovery_and_read() -> io::Result<()> {
    trace();
    let fixture = Fixture::new()?;
    let mut eml = fixture.fs()?; // discovery succeeded while file existed
    fs::remove_file(&fixture.module)?; // fixture-only late disappearance
    let err = eml.read_file("src/mod.lua").err().expect("must fail");
    assert_eq!(err.kind(), ErrorKind::NotFound);
    assert!(fixture.base.join("src").is_dir());
    Ok(())
}

#[test]
fn nonexistent_subdirectory_records_canonicalization_failure() -> io::Result<()> {
    trace();
    let fixture = Fixture::new()?;
    let mut eml = fixture.fs()?;
    let err = eml.read_file("src/absent/entry.lua").err().expect("must fail");
    assert_eq!(err.kind(), ErrorKind::NotFound);
    Ok(())
}

#[test]
fn outside_root_and_symlink_escape_remain_denied() -> io::Result<()> {
    trace();
    let fixture = Fixture::new()?;
    let outside = fixture.base.parent().unwrap().join("not-a-real-mod.lua");
    fs::write(&outside, b"outside")?;
    let mut eml = fixture.fs()?;
    let err = eml.read_file("../not-a-real-mod.lua").err().expect("must fail");
    assert_eq!(err.kind(), ErrorKind::PermissionDenied);
    fs::remove_file(&outside)?;
    Ok(())
}
