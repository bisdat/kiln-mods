# One-time client-hosted crafting-stock interface check

**Goal:** locate Enshrouded's own client-hosted recipe stock selector and its
crafting-stock cache. This is the missing evidence needed to extend normal
crafting material availability, rather than forcing a button.

This is a **read-only** inspection of the installed `enshrouded.exe` on disk.
It does **not** launch Enshrouded, read your saves, connect to the game's
servers, change Kiln, install a DLL, access chest inventories or upload the
game binary. It writes only bounded SHA256 / function-address metadata.

## Run once on your Linux Steam/Proton installation

```bash
mkdir -p ~/Downloads/kiln-client-stock
cd ~/Downloads/kiln-client-stock

curl -fL --retry 2 \
  'https://raw.githubusercontent.com/bisdat/kiln-mods/feature/cross-base-storage-kiln/scripts/inspect-client-crafting-stock.py' \
  -o inspect-client-crafting-stock.py

python3 -m venv .venv
.venv/bin/python -m pip install --disable-pip-version-check \
  pefile==2024.8.26 capstone==5.0.6

.venv/bin/python inspect-client-crafting-stock.py \
  "${HOME}/.local/share/Steam/steamapps/common/Enshrouded/enshrouded.exe" \
  --output enshrouded-client-stock-map.json
```

Share only `~/Downloads/kiln-client-stock/enshrouded-client-stock-map.json`
with PR #7 or in this chat. **Never share or commit the actual game EXE.**

The script requires the exact current, Kiln-runtime-verified client binary:
Enshrouded build **26430625**, SHA256
`5d9ef2f140b8ed49e30dbee655841b89d08baec20f799a1817e171a18a850c77`.
On a newer game update it intentionally refuses to infer ABI or reuse stale
addresses. That refusal itself is useful evidence of a version mismatch.

## Output and interpretation

`signatures.stock_selector`, `signatures.original_inventory_payer`,
and `payer_direct_calls_to_selector` establish whether the client-hosted
server uses the same function signature and two direct stock-selector calls
as the separately verified dedicated server.

The `cache_crafting_stock` and `PlayerBasesSystemClient` label locations
and any verified instruction references identify starting points for analysis
of the vanilla client's ingredient availability. Label presence **alone
does not identify the client cache entrypoint**.

All report fields `client_stock_cache_entry_verified`,
`authorised_remote_sources_verified`, and `safe_to_enable_hook` remain
false regardless of matches. A static address scan cannot prove the game's
permissions, ABI, cache behavior, or original transaction atomicity.

The inspector was validated on a disposable copy of the official,
anonymous dedicated-server binary in
[CI #42657043112](https://github.com/bisdat/kiln-mods/actions/runs/42657043112):
it found exactly one original payer, one selector, and the game's two
independent payer-to-selector calls. The client remains unverified until this
same read-only check is run on an installed client EXE.

**No gameplay test or replacement DLL** should be issued until this evidence
has been used to verify and implement both client availability and the
original transaction's remote payment.
