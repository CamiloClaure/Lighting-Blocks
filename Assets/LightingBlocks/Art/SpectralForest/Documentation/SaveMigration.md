# Save version 2

The existing `soul-bridge-v1.json` filename is intentionally retained so previous saves are discovered. The JSON's `version` is now 2. Existing atomic temporary-file replacement and `.bak` recovery remain in place.

Added `forest` fields:

- `seed`: nonnegative deterministic layout seed.
- `layoutVersion`: currently 1.
- `islands`: one entry per configured stable island ID.
- Island `inventory`: integer 0–30.
- Island `slots`: exactly the configured slot population, identified as `island-id:slot-index`.
- Slot `remaining`: finite unpaused simulation seconds until respawn, in 0–30.

Version 1 migration preserves Souls, upgrades, chapter completion, player position, block IDs, block positions and placed bridge state. It initializes seed 1907, empty island inventories and available particle slots. Version 2 is written after successful initialization. It never relocates saved loose blocks to new tree anchors.

Incoming forest and block data are validated before active state is mutated. Invalid IDs, duplicate IDs, inventory/cooldown values and future save versions are rejected. A corrupt primary save can still recover from the backup.

Charging is transient. No energy is consumed until a block is successfully spawned and registered; the resulting inventory and block state are saved together. Saving during a pending charge retains the pre-charge inventory and no block. Cooldowns have no offline production. Particle inventories are island-specific, and decorative animal movement is not saved.

Verification writes only under the project's Library folder and disables normal persistence before initialization. The profiling executable likewise disables persistence before GameManager starts.
