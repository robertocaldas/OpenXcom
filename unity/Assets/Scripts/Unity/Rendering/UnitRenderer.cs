using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one unit as its 4 standing body-part layers (legs, right arm,
    /// torso, left arm) stacked at the same position, reproducing
    /// UnitSprite.cpp drawRoutine0's direction-4 (south-facing) standing
    /// pose - a unit .PCK frame is a single body-part layer, not a
    /// complete sprite, so one raw frame alone renders as a stray limb.
    /// Positioned via the same IsoProjection math TileRenderer uses. Pure
    /// display: takes already-resolved Sprites and a tile position, makes
    /// no gameplay decisions. [SIMPLIFIED] one static frame per part - no
    /// direction/walk-phase animation and no held-item sprite (HANDOB.PCK
    /// not converted) this slice (parent spec §5 names animation as later
    /// work).
    /// </summary>
    public sealed class UnitRenderer : MonoBehaviour
    {
        private SpriteRenderer _legs;
        private SpriteRenderer _rightArm;
        private SpriteRenderer _torso;
        private SpriteRenderer _leftArm;
        private BoxCollider _collider;

        private void Awake()
        {
            _legs = CreateChild("Legs");
            _rightArm = CreateChild("RightArm");
            _torso = CreateChild("Torso");
            _leftArm = CreateChild("LeftArm");

            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = new Vector3(0.6f, 1f, 0.1f);
        }

        private SpriteRenderer CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<SpriteRenderer>();
        }

        public void Setup(int x, int y, int z, int mapWidth, int mapLength,
            Sprite legsSprite, Sprite rightArmSprite, Sprite torsoSprite, Sprite leftArmSprite)
        {
            var (screenX, screenY) = IsoProjection.MapToScreen(x, y, z);
            transform.localPosition = new Vector3(screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, 0f);

            _legs.sprite = legsSprite;
            _legs.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Legs);

            _rightArm.sprite = rightArmSprite;
            _rightArm.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.RightArm);

            _torso.sprite = torsoSprite;
            _torso.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Torso);

            _leftArm.sprite = leftArmSprite;
            _leftArm.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.LeftArm);
        }
    }
}
