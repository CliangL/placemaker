// Entrance connectors (s9, playtest "edge spikes"): a path meets the outline only at an entrance and
// arrives roughly perpendicular to the edge. The old connectors joined each gate to the nearest point
// of the network, which for a spine ending near the edge (or running beside it) was a short leg
// sideways ALONG the outline. Here the connector leaves the gate straight inward along the edge
// normal; it either meets the network on that line (a clean T) or, after a short straight approach,
// turns at most ~50° off the normal to the nearest reachable network point.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class GateApproach
    {
        /// <summary>Largest angle (degrees) between the inward edge normal and the turned leg.</summary>
        public const float MaxTurn = 50f;

        /// <summary>Inward normal of the outline at gate point <paramref name="gate"/>.</summary>
        public static float2 Inward(LayerRegion region, float2 gate)
        {
            region.ClosestOnBoundary(gate, out int seg, out _);
            return region.InwardNormal(seg);
        }

        /// <summary>
        /// Connector polyline from <paramref name="gate"/> (on the outline) to the network <paramref name="spine"/>:
        /// [gate, hit] when the inward normal meets it, else [gate, approach, foot] with a straight approach of
        /// <paramref name="approach"/> m and a leg within MaxTurn of the normal. Null when nothing is reachable
        /// inside the area (or <paramref name="clear"/> rejects every leg). Ends on the network already (within
        /// 1 m) return [gate] only.
        /// </summary>
        public static List<float2> Route(LayerRegion region, float2 gate, IReadOnlyList<(float2 a, float2 b)> spine, float approach,
            Func<float2, float2, bool> clear = null)
        {
            if (region == null || spine == null || spine.Count == 0) return null;
            var n = Inward(region, gate);
            float near = float.MaxValue;
            foreach (var (a, b) in spine) near = math.min(near, math.distance(SegmentIndex.ClosestPoint(gate, a, b), gate));
            if (near <= 1f) return new List<float2> { gate };

            bool Ok(float2 p, float2 q) => !region.CrossesBoundary(p, q, true) && (clear == null || clear(p, q));

            // 0. A network end straight in front of the gate (an axis aimed at it): extend that end to the gate.
            float bestEnd = float.MaxValue; float2 end = default;
            foreach (var (a, b) in spine)
                foreach (var e in new[] { a, b })
                {
                    var v = e - gate;
                    float along = math.dot(v, n), lateral = math.abs(v.x * n.y - v.y * n.x);
                    if (along > 0.5f && lateral < 1f && along < bestEnd && Ok(gate + n * 0.05f, e)) { bestEnd = along; end = e; }
                }

            // 1. Straight in along the normal: first crossing with the network.
            float bestT = float.MaxValue;
            foreach (var (a, b) in spine)
                if (Ray(gate, n, a, b, out float t) && t > 0.5f && t < bestT) bestT = t;
            if (bestEnd < float.MaxValue && bestEnd <= bestT + 0.5f) return new List<float2> { gate, end };
            if (bestT < float.MaxValue)
            {
                var hit = gate + n * bestT;
                if (Ok(gate + n * 0.05f, hit)) return new List<float2> { gate, hit };
            }

            // 2. Short straight approach, then the nearest network point within MaxTurn of the normal.
            float cos = math.cos(math.radians(MaxTurn));
            foreach (float app in new[] { approach, approach * 2f })
            {
                var from = gate + n * app;
                if (!region.Contains(from) || !Ok(gate + n * 0.05f, from)) continue;
                float best = float.MaxValue; float2 foot = default;
                foreach (var (a, b) in spine)
                {
                    float len = math.distance(a, b);
                    int steps = math.max(1, (int)math.ceil(len));
                    for (int i = 0; i <= steps; i++)
                    {
                        var q = math.lerp(a, b, i / (float)steps);
                        var d = q - from;
                        float l = math.length(d);
                        if (l < 0.5f) { best = 0f; foot = q; break; }
                        if (math.dot(d / l, n) < cos || l >= best) continue;
                        if (!Ok(from, q)) continue;
                        best = l; foot = q;
                    }
                }
                if (best < float.MaxValue)
                    return best <= 0.5f ? new List<float2> { gate, foot } : new List<float2> { gate, from, foot };
            }
            return null;
        }

        /// <summary>
        /// Connector from a gate to one destination point: straight when it heads within MaxTurn of the edge
        /// normal, else a straight approach first ([gate, approach, target]). Null when it would leave the area.
        /// </summary>
        public static List<float2> ToPoint(LayerRegion region, float2 gate, float2 target, float approach)
        {
            var n = Inward(region, gate);
            var d = target - gate;
            float len = math.length(d);
            if (len < 0.5f) return null;
            if (math.dot(d / len, n) >= math.cos(math.radians(MaxTurn)))
                return region.CrossesBoundary(gate, target, true) ? null : new List<float2> { gate, target };
            var from = gate + n * approach;
            if (!region.Contains(from) || region.CrossesBoundary(gate, from, true) || region.CrossesBoundary(from, target, false)) return null;
            return new List<float2> { gate, from, target };
        }

        /// <summary>Ray p + t·dir against segment a–b; t is the distance along the (unit) ray.</summary>
        public static bool Ray(float2 p, float2 dir, float2 a, float2 b, out float t)
        {
            t = 0f;
            var s = b - a;
            float den = dir.x * s.y - dir.y * s.x;
            if (math.abs(den) < 1e-6f) return false;
            var w = a - p;
            t = (w.x * s.y - w.y * s.x) / den;
            float u = (w.x * dir.y - w.y * dir.x) / den;
            return t >= 0f && u >= -1e-4f && u <= 1f + 1e-4f;
        }
    }
}
