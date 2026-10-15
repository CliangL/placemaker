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
