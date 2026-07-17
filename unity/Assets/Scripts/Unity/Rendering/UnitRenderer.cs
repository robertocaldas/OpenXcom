using System.Collections.Generic;
using OpenXcom.Core.Rules;
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one unit as 5 stacked SpriteRenderer layers (legs, right arm,
    /// torso, left arm, held item), reproducing UnitSprite.cpp drawRoutine0's
    /// per-direction/walk-phase/aiming frame selection (see
    /// UnitSpriteFrames.cs for the exact port citations). Positioned via the
    /// same IsoProjection math TileRenderer uses. Pure display: resolves
    /// frame indices to Sprites and assigns them, makes no gameplay
    /// decisions - BattleController decides direction/walkPhase/isAiming
    /// from BattleState's events and calls SetFrame/SetDeathFrame here.
    ///
    /// [SIMPLIFIED] The held item always draws frontmost (UnitPartRank.Item
    /// is always the highest-ranked, highest-sortingOrder part - see
    /// IsoProjection.UnitSortingOrder), regardless of facing direction. The
    /// real engine varies item-vs-body blit order per direction via an
    /// explicit case-0..7 table (UnitSprite.cpp:607-640). Always-frontmost
    /// is an acceptable cut for the two starter weapons this slice renders,
    /// not a full per-direction z-order table.
    /// </summary>
    public sealed class UnitRenderer : MonoBehaviour
    {
        private SpriteRenderer _legs;
        private SpriteRenderer _rightArm;
        private SpriteRenderer _torso;
        private SpriteRenderer _leftArm;
        private SpriteRenderer _item;
        private BoxCollider _collider;

        private (Texture2D texture, List<Rect> frameRects) _bodyAtlas;
        private (Texture2D texture, List<Rect> frameRects)? _itemAtlas;
        private RuleItem _heldWeapon;

        private void Awake()
        {
            _legs = CreateChild("Legs");
            _rightArm = CreateChild("RightArm");
            _torso = CreateChild("Torso");
            _leftArm = CreateChild("LeftArm");
            _item = CreateChild("Item");

            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = new Vector3(0.6f, 1f, 0.1f);
            // Same bottom-anchor-vs-centered-collider mismatch as TileRenderer:
            // the unit sprite (pivot 0.5,0) only extends upward from this
            // transform's local origin, so the collider must be shifted up by
            // half its height to actually overlap the visible body, not just the
            // ground beneath the unit's feet.
            _collider.center = new Vector3(0f, 0.5f, 0f);
        }

        private SpriteRenderer CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<SpriteRenderer>();
        }

        /// <summary>Positions/sorts this unit and stores the atlases/weapon
        /// used by every later SetFrame call. Does not itself pick a frame -
        /// call SetFrame right after Setup to render the initial pose.</summary>
        public void Setup(int x, int y, int z, int mapWidth, int mapLength,
            (Texture2D texture, List<Rect> frameRects) bodyAtlas,
            (Texture2D texture, List<Rect> frameRects)? itemAtlas,
            RuleItem heldWeapon)
        {
            _bodyAtlas = bodyAtlas;
            _itemAtlas = itemAtlas;
            _heldWeapon = heldWeapon;

            var (worldX, worldY) = IsoProjection.WorldPosition(x, y, z, TileRenderer.PixelsPerUnit);
            float depth = IsoProjection.UnitRaycastDepth(x, y, z, mapWidth, mapLength);
            transform.localPosition = new Vector3(worldX, worldY, depth);

            _legs.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Legs);
            _rightArm.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.RightArm);
            _torso.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Torso);
            _leftArm.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.LeftArm);
            _item.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Item);
        }

        /// <summary>Renders the standing (walkPhase &lt; 0) or walking
        /// (walkPhase 0-7) pose facing direction (0-7). isAiming only affects
        /// a two-handed held weapon's arm/item pose (UnitSpriteFrames docs);
        /// pass false outside of the brief firing-pose window.</summary>
        public void SetFrame(int direction, int walkPhase, bool isAiming)
        {
            _legs.enabled = true;
            _rightArm.enabled = true;
            _torso.enabled = true;
            _leftArm.enabled = true;

            _legs.sprite = FrameSprite(_bodyAtlas,
                UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.LegsStandBase, UnitSpriteFrames.LegsWalkBase, direction, walkPhase));
            _torso.sprite = FrameSprite(_bodyAtlas, UnitSpriteFrames.TorsoFrame(direction));

            if (_heldWeapon != null)
            {
                bool twoHanded = _heldWeapon.TwoHanded;
                _rightArm.sprite = FrameSprite(_bodyAtlas,
                    UnitSpriteFrames.HeldRightArmFrame(twoHanded, isAiming, direction));
                _leftArm.sprite = twoHanded
                    ? FrameSprite(_bodyAtlas, UnitSpriteFrames.HeldLeftArmFrame(direction))
                    : FrameSprite(_bodyAtlas,
                        UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.LeftArmStandBase, UnitSpriteFrames.LeftArmWalkBase, direction, walkPhase));

                if (_itemAtlas.HasValue)
                {
                    _item.enabled = true;
                    _item.sprite = FrameSprite(_itemAtlas.Value,
                        UnitSpriteFrames.HeldItemFrame(_heldWeapon.HandSprite, twoHanded, isAiming, direction));
                    bool offsetItem = twoHanded && isAiming;
                    // AimOffsetX/Y are in SDL pixel space (Y positive = further
                    // down the screen). IsoProjection.WorldPosition negates only
                    // the Y axis when converting SDL screen space to Unity's
                    // Y-up world space (X keeps the same sign), so the X offset
                    // is applied as-is while the Y offset is negated to match.
                    float offX = offsetItem ? UnitSpriteFrames.AimOffsetX[direction] / TileRenderer.PixelsPerUnit : 0f;
                    float offY = offsetItem ? -UnitSpriteFrames.AimOffsetY[direction] / TileRenderer.PixelsPerUnit : 0f;
                    _item.transform.localPosition = new Vector3(offX, offY, 0f);
                }
                else
                {
                    _item.enabled = false;
                }
            }
            else
            {
                _rightArm.sprite = FrameSprite(_bodyAtlas,
                    UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.RightArmStandBase, UnitSpriteFrames.RightArmWalkBase, direction, walkPhase));
                _leftArm.sprite = FrameSprite(_bodyAtlas,
                    UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.LeftArmStandBase, UnitSpriteFrames.LeftArmWalkBase, direction, walkPhase));
                _item.enabled = false;
            }
        }

        /// <summary>Collapsing/death pose - replaces the entire 4-part
        /// composite with one frame (UnitSprite.cpp:374-378: BODYPART_COLLAPSING
        /// is drawn as a single sprite, not the usual per-part composite), so
        /// this disables every other layer and repurposes the legs renderer
        /// as the sole visible sprite. phase is 0..UnitSpriteFrames.DeathFrameCount-1.</summary>
        public void SetDeathFrame(int phase)
        {
            _rightArm.enabled = false;
            _torso.enabled = false;
            _leftArm.enabled = false;
            _item.enabled = false;
            _legs.enabled = true;
            _legs.sprite = FrameSprite(_bodyAtlas, UnitSpriteFrames.DeathFrame(phase));
        }

        /// <summary>Overrides the (only remaining visible, post-SetDeathFrame)
        /// legs layer's sorting order - used to drop a corpse down to
        /// tile-level (Object-rank) order instead of the usual unit-band
        /// order (see BattleController.StartDeathSequence). A corpse now
        /// persists indefinitely (it no longer despawns), so a live unit can
        /// walk onto/through the same tile - IsoProjection.UnitSortingOrder's
        /// own doc comment assumes "no two units ever share a tileIndex,"
        /// which a lingering corpse plus a live unit on the same tile
        /// violates, tying their sort order and leaving draw order
        /// undefined. Object-rank keeps the corpse visible above the tile's
        /// own floor/walls while guaranteeing it renders behind any live
        /// unit on the same tile.</summary>
        public void SetSortingOrder(int order) => _legs.sortingOrder = order;

        private static Sprite FrameSprite((Texture2D texture, List<Rect> frameRects) atlas, int frameIndex) =>
            Sprite.Create(atlas.texture, atlas.frameRects[frameIndex], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
    }
}
