# Battlescape tactical spec (distilled from the C++)

Source citations point at `../../src` (the OXCE C++ tree). Where we deliberately
simplify for the clean-slate rewrite, it's marked **[SIMPLIFIED]**.

## Coordinates

- The battlefield is a 3D grid of tiles: `Position { x, y, z }` where `z` is the
  vertical level. Slice 1 uses a single level (`z = 0`).
- 8 compass directions (0 = north, clockwise). Diagonal moves cost more TUs.

## Time Units (TUs) & Energy

Every unit has a TU pool refreshed each turn. Actions cost TUs; some also cost
energy (stamina).

- Base walk cost per orthogonal tile ≈ **4 TU**; diagonal ≈ **6 TU** (×1.5).
  Terrain (`Tile::getTUCost`) can raise it; slice 1 uses flat terrain.
  Source: `src/Battlescape/Pathfinding.cpp` `getTUCost()` (~line 257).
- Kneel/stand and turning also cost TUs in OXCE. **[SIMPLIFIED]** slice 1: turning
  is free, kneel costs a small flat amount.
- Firing cost is a percentage of the unit's *max* TUs, defined per weapon action
  (e.g. snap 25%, aimed 55%, auto 35%). Source: `RuleItem` `getTU*`.

## Firing accuracy

Verified formula. Source: `src/Savegame/BattleUnit.cpp:2468` `getFiringAccuracy()`:

```
Formula = accuracyStat * weaponAccuracy
          * kneelingBonus(1.15 if kneeling & applicable)
          * oneHandedPenalty(0.8 if two-handed weapon fired with other hand full)
          * woundsPenalty(currentHealth / maxHealth)
          * critWoundsPenalty(-10% per fatal head/torso wound)
```

Concretely per action type (`BattleUnit.cpp:2481-2507`):

```
snap  : accuracy = accuracyStat * weapon.accuracySnap%  / 100
aimed : accuracy = accuracyStat * weapon.accuracyAimed% / 100
auto  : accuracy = accuracyStat * weapon.accuracyAuto%  / 100
melee : accuracy = meleeStat    * weapon.accuracyMelee% / 100   (no kneel bonus)
throw : accuracy = throwStat     * weapon.accuracyThrow% / 100   (no kneel bonus)
```

Then:
- if kneeled (and shooting): `accuracy = accuracy * kneelBonus/100` (kneelBonus=115).
- if two-handed and the other hand holds something:
  `accuracy = accuracy * oneHandedPenalty/100` (oneHandedPenalty=80).
- `accuracy = accuracy * accuracyModifier / 100`, where `accuracyModifier`
  (`BattleUnit.cpp:2540` `getAccuracyModifier`) = `healthRatio% - 10%*fatalWounds`.

> In OXCE, `getAccuracyMultiplier(attack)` generalizes the raw `accuracyStat` via
> y-script so mods can rescale it. We drop the scripting layer and use the plain
> stat. **[SIMPLIFIED]**

### Distance drop-off

Source: `src/Battlescape/TileEngine.cpp:2621-2635`.

```
weapon defines upperLimit, lowerLimit, dropoff (per tile, %).
if distance > upperLimit: accuracy -= (distance - upperLimit) * dropoff
if distance < lowerLimit: accuracy -= (lowerLimit - distance) * dropoff
```

### Hit resolution

OXCE fires a projectile along a trajectory whose deviation scales with accuracy
(low accuracy = wider cone), so cover/adjacent tiles can be hit. For slice 1 we
use a **flat roll**: `hit = RNG(0,99) < finalAccuracy`. **[SIMPLIFIED]** — the
trajectory model is a later slice, needed for cover to matter.

## Damage

Source: `src/Battlescape/TileEngine.cpp:3233` `type->getRandomDamage(power)`.

Standard damage type roll is **uniform 0%–200% of weapon power**:

```
rolled = RNG(0, 200) * power / 100
```

Then armor on the hit side is subtracted before applying to health:

```
side   = hit direction → FRONT / LEFT / RIGHT / REAR / UNDER
final  = rolled - armor[side]
if final > 0: health -= final    (after any damage-type resistance)
```

Fatal wounds, stun, morale, and per-body-part damage exist in OXCE but are a
later slice. Slice 1: a unit at `health <= 0` is dead. **[SIMPLIFIED]**

## Line of sight (later this slice)

Source: `src/Battlescape/TileEngine.cpp` `calculateFOV` / `visible()`. Voxel
ray-cast from eye to target; blocked by walls/objects. Slice 1 plan: 2D
supercover line on the single level, blocked by wall tiles; vision range capped
(~20 tiles day). **[SIMPLIFIED]**
