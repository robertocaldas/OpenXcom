using System.Collections.Generic;
using OpenXcom.Core.Common;
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Animates a bullet-trail dot sprite along a fired shot's traced voxel
    /// path (Phase 8's TileEngine.CalculateLine output, carried by
    /// ProjectileFiredEvent.Trajectory). [SIMPLIFIED] the real engine draws a
    /// 35-frame fading streak sampled from a dense per-voxel-step trajectory
    /// (src/Battlescape/Projectile.cpp:557-560, Map.cpp:1121-1165) - Trajectory
    /// here is only [origin, hitVoxel] (Phase 8 doesn't record intermediate
    /// steps), so this instead moves ONE dot sprite (the bullet's own base
    /// frame, offset+0) smoothly from origin to hit voxel over a fixed short
    /// duration, rather than reproducing the 35-frame trailing-streak sampling.
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        [SerializeField] private float flightSeconds = 0.4f;

        private (Texture2D texture, List<Rect> frameRects) _atlas;
        private SpriteRenderer _dot;
        private float _elapsed = -1f;
        private Vector3 _from;
        private Vector3 _to;

        /// <summary>True while a bullet-trail animation is in flight - read
        /// by BattleController to sequence AI-turn events one at a time
        /// (don't start the next unit's action until this one's bullet has
        /// finished, not just the firing pose).</summary>
        public bool IsPlaying => _elapsed >= 0f;

        private void Awake()
        {
            var go = new GameObject("Bullet");
            go.transform.SetParent(transform, worldPositionStays: false);
            _dot = go.AddComponent<SpriteRenderer>();
            _dot.sortingOrder = short.MaxValue; // always drawn above tiles/units - a bullet is never occluded mid-flight
            _dot.enabled = false;
        }

        public void Setup((Texture2D texture, List<Rect> frameRects) atlas)
        {
            _atlas = atlas;
        }

        /// <summary>Starts a new bullet-trail animation from originVoxel to
        /// hitVoxel, using bulletSpriteBase (RuleItem.BulletSprite) as the
        /// dot's frame within the bulletsprites atlas.</summary>
        public void Play(Position originVoxel, Position hitVoxel, int bulletSpriteBase)
        {
            var (fx, fy) = IsoProjection.VoxelWorldPosition(originVoxel.X, originVoxel.Y, originVoxel.Z, TileRenderer.PixelsPerUnit);
            var (tx, ty) = IsoProjection.VoxelWorldPosition(hitVoxel.X, hitVoxel.Y, hitVoxel.Z, TileRenderer.PixelsPerUnit);
            _from = new Vector3(fx, fy, -0.01f);
            _to = new Vector3(tx, ty, -0.01f);
            _dot.sprite = Sprite.Create(_atlas.texture, _atlas.frameRects[bulletSpriteBase], new Vector2(0.5f, 0.5f), TileRenderer.PixelsPerUnit);
            _dot.enabled = true;
            _elapsed = 0f;
            _dot.transform.position = _from;
        }

        private void Update()
        {
            if (_elapsed < 0f)
                return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / flightSeconds);
            _dot.transform.position = Vector3.Lerp(_from, _to, t);

            if (t >= 1f)
            {
                _elapsed = -1f;
                _dot.enabled = false;
            }
        }
    }
}
