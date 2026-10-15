// In-game test suite: camera framing and PNG capture (docs/teardown/10-ingame-suite.md §4.6).
//   * Result shots (oblique, top): the game's own ScreenCaptureHelper renders the main camera into a render texture with
//     the UI, the overlay and the selection outlines hidden (the save-preview path), read back and saved as PNG.
//   * Preview shot (ghost + Placemaker's overlay visible): the UI is switched off for a few frames and the back buffer is
//     grabbed at the end of the frame, like photo mode does.

using System;
using System.IO;
using Colossal.UI;
using Game;
using Game.Rendering;
using Game.UI;
using Game.UI.Menu;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    internal static class SuiteCapture
    {
        /// <summary>Points the normal game camera at a world point: yaw/pitch in degrees, distance in metres.</summary>
        public static bool Aim(float3 pivot, float yaw, float pitch, float distance)
        {
            try
            {
                var cams = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CameraUpdateSystem>();
                var c = cams?.gamePlayController;
                if (c == null) return false;
                if (!ReferenceEquals(cams.activeCameraController, c))
                {
                    c.TryMatchPosition(cams.activeCameraController);
                    cams.activeCameraController = c;
                }
                c.pivot = new Vector3(pivot.x, pivot.y, pivot.z);
                c.angle = new float2(yaw, pitch);
                c.zoom = distance;
                return true;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker suite: camera aim failed: " + e.Message);
                return false;
            }
        }

        /// <summary>The game camera controller's pivot is within <paramref name="tolerance"/> m of the point (x, z).</summary>
        public static bool PivotNear(float3 p, float tolerance)
        {
            try
            {
                var c = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CameraUpdateSystem>()?.gamePlayController;
                if (c == null) return true;
                var v = c.pivot;
                return math.distance(new float2(v.x, v.z), p.xz) <= tolerance;
            }
            catch (Exception) { return true; }
        }

        /// <summary>The game camera's current zoom (distance to its pivot), 0 when unknown.</summary>
        public static float Zoom()
        {
            try { return World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CameraUpdateSystem>()?.gamePlayController?.zoom ?? 0f; }
            catch (Exception) { return 0f; }
        }

        private static object s_tss;
        private static System.Reflection.PropertyInfo s_left, s_reads;

        /// <summary>
        /// Virtual texturing activity (TextureStreamingSystem, read by reflection: VT materials still to load, async tile
        /// reads). (-1, -1) when unavailable.
        /// </summary>
        public static (long Left, long Reads) Streaming(World world)
        {
            try
            {
                if (s_tss == null)
                {
                    foreach (var s in world.Systems)
                        if (s.GetType().Name == "TextureStreamingSystem") { s_tss = s; break; }
                    if (s_tss == null) return (-1, -1);
                    var t = s_tss.GetType();
                    s_left = t.GetProperty("VTMaterialsLeftToLoadCount");
                    s_reads = t.GetProperty("perSurfaceAsyncTileReadCount");
                }
                long left = s_left != null ? Convert.ToInt64(s_left.GetValue(s_tss)) : -1;
                long reads = s_reads != null ? Convert.ToInt64(s_reads.GetValue(s_tss)) : -1;
                return (left, reads);
            }
            catch (Exception) { return (-1, -1); }
        }

        public static string CameraState()
        {
            try
            {
                var cams = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CameraUpdateSystem>();
                var c = cams?.gamePlayController;
                if (c == null) return "no camera";
                return $"pivot {c.pivot} angle {c.angle} zoom {c.zoom:F0} cam {Camera.main?.transform.position}";
            }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>Renders the main camera (no UI, no overlay) to a PNG. Returns null on success, else the error.</summary>
        public static string RenderPng(string path, int width, int height)
        {
            RenderTexture rt = null;
            Texture2D tex = null;
            var previous = RenderTexture.active;
            try
            {
                var cam = Camera.main;
                if (cam == null) return "no main camera";
                rt = ScreenCaptureHelper.CreateRenderTarget("Placemaker Suite Shot", width, height);
                ScreenCaptureHelper.CaptureScreenshot(cam, rt, new MenuHelpers.SaveGamePreviewSettings());
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);
                RenderTexture.active = previous;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return null;
            }
            catch (Exception e)
            {
                RenderTexture.active = previous;
                return e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            }
        }

        /// <summary>UI off (overlay and ghost stay): call, wait 2 frames + end of frame, then GrabScreen, then UiOn.</summary>
        public static void UiOff()
        {
            try { if (UIManager.defaultUISystem != null) UIManager.defaultUISystem.enabled = false; } catch (Exception) { }
        }

        public static void UiOn()
        {
            try { if (UIManager.defaultUISystem != null) UIManager.defaultUISystem.enabled = true; } catch (Exception) { }
        }

        /// <summary>The main camera rendered (no UI, no overlay) into a new texture; the caller destroys it.</summary>
        public static Texture2D RenderTexture2D(int width, int height, out string error)
        {
            error = null;
            RenderTexture rt = null;
            var previous = RenderTexture.active;
            try
            {
                var cam = Camera.main;
                if (cam == null) { error = "no main camera"; return null; }
                rt = ScreenCaptureHelper.CreateRenderTarget("Placemaker Suite Shot", width, height);
                ScreenCaptureHelper.CaptureScreenshot(cam, rt, new MenuHelpers.SaveGamePreviewSettings());
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);
                return tex;
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            }
        }

        /// <summary>The back buffer (call after WaitForEndOfFrame) scaled to at most maxWidth; the caller destroys it.</summary>
        public static Texture2D GrabTexture2D(int maxWidth, out string error)
        {
            error = null;
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { error = "no screen texture"; return null; }
                if (tex.width <= maxWidth) return tex;
                int w = maxWidth, h = Mathf.RoundToInt(tex.height * (maxWidth / (float)tex.width));
                var rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var scaled = new Texture2D(w, h, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                scaled.Apply(false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(tex);
                return scaled;
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; if (tex != null) UnityEngine.Object.Destroy(tex); return null; }
        }

        /// <summary>Mean absolute difference per colour channel (0-255) between two captures, on a sample grid; -1 if not comparable.</summary>
        public static float MeanDelta(Texture2D a, Texture2D b)
        {
            if (a == null || b == null || a.width != b.width || a.height != b.height) return -1f;
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            long sum = 0, n = 0;
            int step = Math.Max(1, (int)Math.Sqrt(pa.Length / 40000.0));
            for (int y = 0; y < a.height; y += step)
                for (int x = 0; x < a.width; x += step)
                {
                    int i = y * a.width + x;
                    sum += Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b);
                    n += 3;
                }
            return n == 0 ? -1f : sum / (float)n;
        }

        /// <summary>Back buffer to PNG (call after WaitForEndOfFrame). Scaled down to at most maxWidth.</summary>
        public static string GrabScreen(string path, int maxWidth)
        {
            Texture2D tex = null, scaled = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) return "no screen texture";
                var output = tex;
                if (tex.width > maxWidth)
                {
                    int w = maxWidth, h = Mathf.RoundToInt(tex.height * (maxWidth / (float)tex.width));
                    var rt = RenderTexture.GetTemporary(w, h, 0);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    scaled = new Texture2D(w, h, TextureFormat.RGB24, false);
                    scaled.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    scaled.Apply(false);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    output = scaled;
                }
                File.WriteAllBytes(path, output.EncodeToPNG());
                return null;
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
            }
        }
    }
}
