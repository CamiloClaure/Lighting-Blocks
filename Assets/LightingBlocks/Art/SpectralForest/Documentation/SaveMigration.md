# Save version 2

The existing `soul-bridge-v1.json` filename is intentionally retained so previous saves are discovered. The JSON's `version` is now 2. Existing atomic temporary-file replacement and `.bak` recovery remain in place.

Added `forest` fields:

- `seed`: nonnegative deterministic layout seed.
- `layoutVersion`: currently 3 (`ForestConfiguration.LayoutVersion`, `Runtime/ForestConfiguration.cs:23`). It has moved twice since this document was written, each time with its own migration in `ForestWorld.Migrate` (see below).
- `islands`: one entry per configured stable island ID.
- Island `inventory`: integer 0–30.
- Island `slots`: exactly the configured slot population, identified as `island-id:slot-index`.
- Slot `remaining`: finite unpaused simulation seconds until respawn, in 0–30.
- Slot `position`, `generation`, `pending`: added at layout version 2, they track where a collected particle will respawn, how many times it already has, and whether it is currently waiting to.

Version 1 migration preserves Souls, upgrades, chapter completion, player position, block IDs, block positions and placed bridge state. It initializes seed 1907, empty island inventories and available particle slots. Version 2 is written after successful initialization. It never relocates saved loose blocks to new tree anchors.

Layout version 2 (`ForestWorld.Migrate`, save schema version 3) is what added the particle slot's `position`, `generation` and `pending` fields, so a collected particle can respawn elsewhere on its island instead of only at its original spot. Migrating an older, schema-version-2 save only carries over each island's `inventory` and each slot's `remaining`; position, generation and pending come from the freshly generated layout instead, since a version-2 save never had meaningful values for them.

Layout version 3 (save schema version 4, current) corrects particle positions on the Obsidian island (`polygonIndex == 2`) for a since-added `Landscape.Expansion` shift to that island's shore. Migrating a schema-version-3 save first rejects it unless its forest data is already at layout version 2 and every slot's saved position falls inside the pre-expansion bounds (`|x| <= 60`, `-70 <= z <= 120`). It then carries every island's slot `position`, `generation` and `pending` over from the old save as-is, except the Obsidian island's slots each get `Vector3.forward * Landscape.Expansion` added to their position. A repair pass follows: any tree that now overlaps a (possibly shifted) particle is re-suppressed, and any slot whose position is no longer in its island's reachable area falls back to the freshly generated layout's particle position at that slot index.

Incoming forest and block data are validated before active state is mutated. Invalid IDs, duplicate IDs, inventory/cooldown values and future save versions are rejected. A corrupt primary save can still recover from the backup.

Charging is transient. No energy is consumed until a block is successfully spawned and registered; the resulting inventory and block state are saved together. Saving during a pending charge retains the pre-charge inventory and no block. Cooldowns have no offline production. Particle inventories are island-specific, and decorative animal movement is not saved.

Verification writes only under the project's Library folder and disables normal persistence before initialization. The profiling executable likewise disables persistence before GameManager starts.
