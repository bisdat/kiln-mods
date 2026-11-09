// SPDX-License-Identifier: MIT
// Runs against the real mod-loader-base crate from installed Kiln commit
// d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63.
// This is diagnostic code, never installed in the game.
use std::{fs, io::{self, Read}, time::{SystemTime, UNIX_EPOCH}};
use camino::Utf8PathBuf;
use mod_loader_base::FileSystem;

#[test]
fn real_eml_reader_loads_nested_module_on_z_drive() -> io::Result<()> {
    let parent = if let Some(home) = std::env::var_os("KILN_EML_TEST_BASE") {
        // Under Wine, this can be a Z:-mapped symlink to an Atomic-style
        // /var/home tree, provided by the Linux workflow, not the test.
        std::path::PathBuf::from(home)
    } else if std::env::var_os("KILN_EML_TEST_USE_CWD").is_some() {
        std::env::current_dir()?
    } else {
        std::env::temp_dir()
    };
    let now = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
    let base = parent.join(format!("kiln-real-reader-{}-{now}", std::process::id()));
    let dir = base.join("library/eml/flame-altar-expanded/0.1.1+hash");
    fs::create_dir_all(dir.join("src"))?;
    fs::write(dir.join("mod.json"), b"{\"id\":\"flame-altar-expanded\"}")?;
    fs::write(dir.join("src/mod.lua"), b"return true")?;
    let root = Utf8PathBuf::from_path_buf(fs::canonicalize(&dir)?).expect("UTF-8 fixture path");
    eprintln!("EML canonical root = {:?}", root);
    eprintln!("raw nested-file metadata = {:?}", fs::metadata(dir.join("src/mod.lua")));
    eprintln!("raw nested-file canonicalize = {:?}", fs::canonicalize(dir.join("src/mod.lua")));

    let mut eml_fs = FileSystem::new_disk(&root)?;
    let mut manifest = String::new();
    eml_fs.read_file("mod.json")?.read_to_string(&mut manifest)?;
    assert!(manifest.contains("flame-altar-expanded"));
    eprintln!("EML mod.json read succeeded");

    let mut code = String::new();
    let outcome = eml_fs.read_file("src/mod.lua");
    match outcome {
        Ok(mut reader) => { reader.read_to_string(&mut code)?; },
        Err(e) => {
            eprintln!("EML src/mod.lua read failed: kind={:?}, os_error={:?}, error={e}", e.kind(), e.raw_os_error());
            return Err(e);
        }
    }
    assert_eq!(code, "return true");
    eprintln!("EML src/mod.lua read succeeded");
    fs::remove_dir_all(base)?;
    Ok(())
}
