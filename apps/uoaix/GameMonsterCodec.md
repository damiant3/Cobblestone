# Monster checkpoint component

UMC1 accompanies WorldRecords and UCB1. Restore world and combat profiles
first, allocate `gm-new` with the stored slot count, then decode:

- `gmc-size count` gives the component length.
- `gmc-encode monsters world now buffer capacity` writes a snapshot.
- `gmc-decode monsters world now buffer size` validates and restores it.

Both codec operations return Result Integer Text. Count is one through
eight, matching the already allocated controller. Encoding follows a
complete gm-pulse, including its dead-slot cleanup; a stale nonzero monster
serial refuses the snapshot. Persist world, UCB1 and UMC1 atomically.

The header contains eight little-endian qwords: magic UMC1, version1,
count, home x/y/z/radius, reserved zero. Each slot has three qwords: live
mobile serial or zero vacancy, direction0 through7, and remaining raw PIT
ticks. Remaining time is max(0, due-now), bounded by the sixty-second
replacement delay. Decode adds the new boot tick to remaining time; time
while the server was stopped does not advance these timers. It sets the
combat clock to that same boot tick, clears slot visibility and resets
connection identity. UCB1 supplies RNG and NPC profiles.

Decode validates the complete buffer, home bounds, duplicate identities,
clock overflow, deadlines and world/profile associations before mutation.
The encompassing checkpoint supplies integrity checking. Encoding and
decoding allocate no retained state. Row processing is O(slot count);
duplicate validation is O(slot count squared), with the fixed maximum8.

GameMonsterCodecReplay covers home/serial/facing recovery, movement and
replacement deadline rebasing, visibility reset, duplicate/stale refusal
and an invalid final row leaving the destination untouched.
