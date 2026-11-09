# EML CRC fingerprint reuse — experimental corrective patch

Related: [performance issue #17](https://github.com/bisdat/kiln-mods/issues/17), [diagnostic PR #15](https://github.com/bisdat/kiln-mods/pull/15), [upstream Kiln issue #4](https://github.com/frostdev-ops/kiln/issues/4). Flame Altar [PR #13](https://github.com/bisdat/kiln-mods/pull/13) remains blocked.

## Scope

The actual Enshrouded install has 55 data containers, approximately 48 GiB. In pinned upstream EML d35ee27c5c4f1f31b1c4a3503636a653d6d3ae63, one CRC-64 file-state snapshot streams all of those bytes, and the patch workflow can fingerprint them multiple times.

The applied source patch targets only mod-loader-lua/src/cache/mod.rs and mod-loader-lua/src/lib.rs in the GPL EML workspace. It preserves CRC-64/ECMA, cache comparisons, file selection, backup and publication barriers. It adds optional file identity and independent change stamps to the on-disk cache schema, using the original CRC tuple unchanged. Old caches cause a complete one-time rehash.

On native Linux, use opened-file device/inode and high-resolution ctime. On Windows, use GetFileInformationByHandleEx FileBasicInfo.ChangeTime and FileIdInfo from the actual file handle. Unavailable, zero or unsupported stamps trigger the original complete byte-for-byte CRC hashing. A fast reuse requires matching identity, change stamp, file size and nanosecond mtime, and an extra path reopening check. A full rehash checks that the file has not changed while being read. Metadata/permissions failure always disables a cache hit.

**Security limitation:** A change stamp is an OS-supplied indication of mutation, not a cryptographic proof of bytes. A sufficiently privileged adversary or unreliable filesystem that can replay the same identity/change stamp could evade the optimized path. The new optimization is experimental until the target Proton/Wine filesystem proves that preserved-mtime changes advance its change stamp. It is NOT verified as a replacement for strict full CRC rereads under every threat model. The test-only DLL must not be installed as a routine production upgrade.

## Tests and benchmark

The independent script add_tests.py injects six tests into pinned EML: 16-MiB cold CRC versus warm reuse, same-size edit with restored modification timestamp, same-size file replacement, deletion/unreadability, cache JSON round-trip, and old-cache fallback. CI checks Windows and Linux, both existing cache correctness tests, and Windows static-CRT release DLL compilation. It prints actual bytes hashed and elapsed times; these are synthetic file benchmarks, not Enshrouded game timings.

Apply first with python3 performance/eml-fingerprint-reuse/apply.py kiln-upstream, then python3 performance/eml-fingerprint-reuse/add_tests.py kiln-upstream, using a clean checkout of the pinned Kiln upstream revision.

This is a separate experimental performance PR. No automatic install; the original Kiln DLL is restored and Flame Altar Expanded remains disabled.
