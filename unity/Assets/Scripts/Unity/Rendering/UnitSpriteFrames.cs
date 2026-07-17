namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Pure frame-index math for drawRoutine0's soldier/Sectoid body-part
    /// layout (src/Battlescape/UnitSprite.cpp:286-560, used by both XCOM
    /// soldiers and Sectoids - the "_drawingRoutine &lt;= 10" branch). No
    /// UnityEngine dependency, so it is unit-testable without the Editor
    /// (same pattern as IsoProjection.cs); UnitRenderer/BattleController call
    /// into this for the actual frame indices, then resolve/assign Sprites.
    /// </summary>
    public static class UnitSpriteFrames
    {
        // Body-part base frame indices within units-XCOM_0.png/units-SECTOID.png
        // (UnitSprite.cpp:290,295,346-349).
        public const int LegsStandBase = 16;
        public const int LegsWalkBase = 56;
        public const int LeftArmStandBase = 0;
        public const int LeftArmWalkBase = 40;
        public const int RightArmStandBase = 8;
        public const int RightArmWalkBase = 48;
        public const int TorsoBase = 32;

        // Held-item arm poses, within the same units-*.png atlas (UnitSprite.cpp:297-300,346).
        public const int RightArmOneHanded = 232;       // rarm1H
        public const int LeftArmTwoHanded = 240;        // larm2H
        public const int RightArmTwoHandedCarry = 248;  // rarm2H (not aiming)
        public const int RightArmTwoHandedAim = 256;    // rarmShoot (aiming)

        // Death sequence (UnitSprite.cpp:294,377; src/Mod/Armor.cpp:44 default deathFrames=3).
        public const int DieBase = 264;
        public const int DeathFrameCount = 3;

        /// <summary>Standing (walkPhase &lt; 0) or walking (walkPhase 0-7,
        /// taken mod 8) frame index for legs/left-arm/right-arm, matching
        /// UnitSprite.cpp:418-420 (walk: partBase + 24*direction + walkPhase)
        /// vs :438-441 (stand: partBase + direction). walkPhase mod 8 mirrors
        /// BattleUnit::getWalkingPhase (src/Savegame/BattleUnit.cpp:1213-1215).</summary>
        public static int BodyPartFrame(int partStandBase, int partWalkBase, int direction, int walkPhase) =>
            walkPhase < 0
                ? partStandBase + direction
                : partWalkBase + 24 * direction + (walkPhase % 8);

        /// <summary>Torso frame never changes for walk vs stand (UnitSprite.cpp
        /// only adjusts a +-1px Y offset while walking - torsoHandsWeaponY,
        /// intentionally not ported, see this plan's Task 4 notes); it is
        /// always partBase + direction.</summary>
        public static int TorsoFrame(int direction) => TorsoBase + direction;

        /// <summary>Death frame (UnitSprite.cpp:377, selectUnit(coll, die,
        /// fallingPhase) - no direction dependence). phase is 0..DeathFrameCount-1.</summary>
        public static int DeathFrame(int phase) => DieBase + phase;

        /// <summary>Held-item right-arm frame: one-handed weapons always show
        /// the static one-handed carry pose (UnitSprite.cpp:498, no aiming
        /// variant exists for one-handed items); two-handed weapons show the
        /// two-handed carry pose unless isAiming, in which case they show the
        /// two-handed aim pose (UnitSprite.cpp:483-490). Neither pose cycles
        /// with walkPhase - the real engine's item-holding arm frame is
        /// direction-only regardless of walking/standing.</summary>
        public static int HeldRightArmFrame(bool twoHanded, bool isAiming, int direction) =>
            !twoHanded ? RightArmOneHanded + direction
            : isAiming ? RightArmTwoHandedAim + direction
            : RightArmTwoHandedCarry + direction;

        /// <summary>Held-item left-arm frame: only meaningful for two-handed
        /// weapons (UnitSprite.cpp:483, always larm2H+direction, static - like
        /// the right arm, no walk cycle). One-handed weapons leave the left
        /// arm on its normal stand/walk cycle - callers should use
        /// BodyPartFrame(LeftArmStandBase, LeftArmWalkBase, ...) instead of
        /// this method in that case.</summary>
        public static int HeldLeftArmFrame(int direction) => LeftArmTwoHanded + direction;

        /// <summary>Held-item sprite frame within the hand-sprite atlas
        /// (handSprite + direction, UnitSprite.cpp:96-99 selectItem). For a
        /// two-handed weapon while aiming, the item's own direction flips by
        /// a quarter-turn relative to the body (UnitSprite.cpp:~450,
        /// `dir = (unitDir+2)%8`) - a real, cited quirk of the original data,
        /// not a rewrite invention.</summary>
        public static int HeldItemFrame(int handSprite, bool twoHanded, bool isAiming, int direction)
        {
            int itemDir = (twoHanded && isAiming) ? (direction + 2) % 8 : direction;
            return handSprite + itemDir;
        }

        /// <summary>Item pixel-offset while aiming a two-handed weapon only
        /// (UnitSprite.cpp:353-354,450-451), indexed by direction. In original
        /// SDL pixel units - divide by TileRenderer.PixelsPerUnit before
        /// applying as a Unity local-position offset.</summary>
        public static readonly int[] AimOffsetX = { 8, 10, 7, 4, -9, -11, -7, -3 };
        public static readonly int[] AimOffsetY = { -6, -3, 0, 2, 0, -4, -7, -9 };

        /// <summary>HANDOB item art is authored assuming a standard soldier's
        /// standing height (UnitSprite.cpp:290's local `soldierHeight`
        /// constant). Reference height for HeldItemYOffset below.</summary>
        public const int ReferenceStandHeight = 22;

        /// <summary>Held-item Y offset (SDL pixel units, positive = further
        /// down the screen - divide by TileRenderer.PixelsPerUnit and negate
        /// for a Unity local-position offset, matching AimOffsetY's own
        /// convention) for a unit whose actual StandHeight differs from the
        /// soldier-height HANDOB art assumes. Port of UnitSprite.cpp:577-585:
        /// "items are calculated for soldier height (22) - some aliens are
        /// smaller, so item is drawn lower" - applied unconditionally
        /// (both one- and two-handed, standing or walking), added on top of
        /// any aiming offset, using the unit's StandHeight even while
        /// kneeled (the C++ calls getStandHeight() here, not getHeight()).
        /// A soldier-height unit (22) gets 0 - this is a no-op for the
        /// default human squad, only visibly nudging shorter/taller races.</summary>
        public static int HeldItemYOffset(int standHeight) => ReferenceStandHeight - standHeight;
    }
}
