using UnityEngine;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// Shared integer upscale factor for the HUD's Constant-Pixel-Size Canvas
    /// (HudBootstrap) and for CameraController's icon-bar-exclusion band in
    /// HandleEdgeScroll - both must agree on how many real screen pixels the
    /// icon bar's native 56px height actually occupies. OpenXcom itself keeps
    /// its low-res UI pixel-native while still supporting arbitrary display
    /// resolutions by upscaling the whole rendered surface by an integer
    /// factor to fit the window; this is the same idea, sized off the
    /// original's lowest native vertical resolution (320x200).
    /// </summary>
    public static class HudScale
    {
        private const float NativeScreenHeight = 200f;

        public static float ComputeScaleFactor(float screenHeightPixels) =>
            Mathf.Max(1f, Mathf.Floor(screenHeightPixels / NativeScreenHeight));
    }
}
