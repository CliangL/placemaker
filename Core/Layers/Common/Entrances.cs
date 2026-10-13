// Entrance / access-point suggestion and road attachment shared by park, plaza and campground.
//
// ParkManager made the player place every entrance before anything happened. Here the layer
// suggests entrances where the outline meets roads (block-pick attachments or nearby nets),
// so a fresh selection is immediately buildable; markers the player places always win.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public struct Gate
    {
        public float2 Position;
        public int Segment;
        public float T;
        /// <summary>Existing net to connect to (BoundaryAttachment/NearbyNet ExternalId), −1 if none.</summary>
        public long AttachId;
        public float AttachT;
        public bool Suggested;
    }

    public static class Entrances
    {
        /// <summary>Max distance from the outline to a road edge for it to count as frontage.</summary>
        public const float FrontageReach = 6f;

        /// <summary>Player markers of <paramref name="kind"/>, else suggestions; never empty for a valid region.</summary>
        public static List<Gate> Resolve(AreaInput input, LayerRegion region, string kind, int wanted)
        {
            var gates = new List<Gate>();
            foreach (var m in input.Markers)
            {
                if (m.Kind != kind) continue;
                var p = region.ClosestOnBoundary(m.Position.xz, out int seg, out float t);
                gates.Add(new Gate { Position = p, Segment = seg, T = t, AttachId = -1 });
            }
            if (gates.Count == 0)
                foreach (var m in Suggest(input, region, kind, wanted))
                    gates.Add(new Gate { Position = m.Position.xz, Segment = m.RingSegment, T = m.RingT, AttachId = -1, Suggested = true });
            for (int i = 0; i < gates.Count; i++)
            {
                var g = gates[i];
                Attach(input, ref g);
                gates[i] = g;
            }
            return gates;
        }

        /// <summary>Suggested count by area: pocket parks one entrance, large parks up to five.</summary>
        public static int WantedByArea(float area)
        {
            if (area < 2500f) return 1;
            if (area < 15000f) return 2;
            if (area < 40000f) return 3;
            if (area < 100000f) return 4;
            return 5;
        }

        /// <summary>
        /// Picks up to <paramref name="wanted"/> outline points next to roads, spread out with
        /// farthest-point selection. Falls back to the longest edge's midpoint (and the point
        /// farthest from it) when no road touches the area.
        /// </summary>
        public static List<EdgeMarker> Suggest(AreaInput input, LayerRegion region, string kind, int wanted)
        {
            var result = new List<EdgeMarker>();
            if (region == null || wanted <= 0) return result;
            var candidates = new List<float2>();
            var weights = new List<float>();

            // Strong candidates: where a road meets the outline at a node or crossing.
            foreach (var a in input.Shape.Attachments)
                if (a.Kind == AttachmentKind.RoadCrossing || a.Kind == AttachmentKind.RoadNode)
                {
                    candidates.Add(region.ClosestOnBoundary(a.Position.xz, out _, out _));
                    weights.Add(2f);
                }

            // Frontage runs: outline samples within reach of a road (attachment along edge or nearby net).
            var roads = RoadSegments(input);
            float step = math.clamp(region.Perimeter / 400f, 2f, 8f);
            int samples = math.max(8, (int)(region.Perimeter / step));
            var front = new bool[samples];
            for (int i = 0; i < samples; i++)
            {
                var p = region.PointAt(region.Perimeter * i / samples, out _, out _, out _);
                front[i] = roads.Clearance(p, FrontageReach + 0.01f) <= FrontageReach;
            }
            bool all = true; foreach (var f in front) all &= f;
            if (all)
            {
                // Fully surrounded: candidates at the middle of every long edge.
                for (int e = 0; e < region.Ring.Length; e++)
                {
                    var a = region.Ring[e]; var b = region.Ring[(e + 1) % region.Ring.Length];
                    if (math.distance(a, b) < 12f) continue;
                    candidates.Add(math.lerp(a, b, 0.5f)); weights.Add(1f + math.distance(a, b) / 200f);
                }
            }
            else
            {
                int startIdx = 0;
                while (startIdx < samples && front[startIdx]) startIdx++;   // begin at a non-front sample
                int run = -1;
                for (int k = 1; k <= samples; k++)
                {
                    int i = (startIdx + k) % samples;
                    if (front[i] && run < 0) run = k;
                    if ((!front[i] || k == samples) && run >= 0)
                    {
                        int len = k - run;
                        float length = len * region.Perimeter / samples;
                        if (length >= 6f)
                        {
                            int pieces = length > 150f ? 3 : length > 70f ? 2 : 1;
                            for (int q = 0; q < pieces; q++)
                            {
                                float along = (startIdx + run + len * (q + 0.5f) / pieces) * region.Perimeter / samples;
                                candidates.Add(region.PointAt(along, out _, out _, out _));
                                weights.Add(1f + length / 300f);
                            }
                        }
                        run = -1;
                    }
                }
            }

            if (candidates.Count == 0)
            {
                // No road anywhere: midpoint of the longest edge, then farthest points from it.
                int longest = 0; float len = 0;
                for (int e = 0; e < region.Ring.Length; e++)
                {
                    float l = math.distance(region.Ring[e], region.Ring[(e + 1) % region.Ring.Length]);
                    if (l > len) { len = l; longest = e; }
                }
                candidates.Add(math.lerp(region.Ring[longest], region.Ring[(longest + 1) % region.Ring.Length], 0.5f));
                weights.Add(1f);
                int extra = math.max(0, wanted - 1);
                for (int i = 0; i < 16 && extra > 0; i++)
                {
                    candidates.Add(region.PointAt(region.Perimeter * (i + 0.5f) / 16f, out _, out _, out _));
                    weights.Add(0.5f);
                }
            }

            // Farthest-point selection weighted by candidate strength; first = strongest.
            var chosen = new List<int>();
            int first = 0;
            for (int i = 1; i < candidates.Count; i++) if (weights[i] > weights[first]) first = i;
            chosen.Add(first);
            float minSpacing = math.max(20f, region.Perimeter / (wanted * 3f));
            while (chosen.Count < wanted)
            {
                int best = -1; float bestScore = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (chosen.Contains(i)) continue;
                    float d = float.MaxValue;
                    foreach (int c in chosen) d = math.min(d, math.distance(candidates[i], candidates[c]));
                    if (d < minSpacing) continue;
                    float score = d * weights[i];
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                if (best < 0) break;
                chosen.Add(best);
            }

            foreach (int c in chosen)
            {
                var p = region.ClosestOnBoundary(candidates[c], out int seg, out float t);
                float y = input.Site?.HeightAt != null ? input.Site.HeightAt(p) : 0f;
                result.Add(new EdgeMarker { Kind = kind, RingSegment = seg, RingT = t, Position = new float3(p.x, y, p.y) });
            }
            return result;
        }

        /// <summary>Road/path geometry near the area as segments (Béziers sampled), for frontage tests.</summary>
        public static SegmentIndex RoadSegments(AreaInput input)
        {
            var idx = new SegmentIndex(12f);
            foreach (var n in input.Site?.NearbyNets ?? new List<NearbyNet>())
            {
                if (n.Category == NetCategory.Track) continue;
                float hw = math.max(1f, n.Width * 0.5f);
                float3 prev = n.A;
                for (int i = 1; i <= 8; i++)
                {
                    var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f);
                    idx.Add(prev.xz, p.xz, hw, 0);
                    prev = p;
                }
            }
            foreach (var a in input.Shape.Attachments)
            {
                float hw = math.max(1f, a.RoadWidth * 0.5f);
                var dir = math.normalizesafe(a.Direction.xz, new float2(1, 0));
                float reach = a.Kind == AttachmentKind.RoadAlongEdge ? 20f : 4f;
                idx.Add(a.Position.xz - dir * reach, a.Position.xz + dir * reach, hw, 1);
            }
            return idx;
        }

        /// <summary>Links a gate to the closest attachment or nearby net so the game layer can connect the path end.</summary>
        public static void Attach(AreaInput input, ref Gate g)
        {
            float best = 3f;
            foreach (var a in input.Shape.Attachments)
            {
                float d = math.distance(a.Position.xz, g.Position);
                if (d < best) { best = d; g.AttachId = a.ExternalId; g.AttachT = a.ExternalT; }
            }
            if (g.AttachId >= 0) return;
            float bestNet = float.MaxValue;
            foreach (var n in input.Site?.NearbyNets ?? new List<NearbyNet>())
            {
                if (n.Category == NetCategory.Track) continue;
                float reach = n.Width * 0.5f + FrontageReach;
                // Sample the curve finely enough for a few-metre accuracy on typical 30–90 m edges.
                for (int i = 0; i <= 32; i++)
                {
                    float t = i / 32f;
                    var p = Composer.Bezier(n.A, n.B, n.C, n.D, t).xz;
                    float d = math.distance(p, g.Position);
                    if (d <= reach && d < bestNet) { bestNet = d; g.AttachId = n.ExternalId; g.AttachT = t; }
                }
            }
        }
    }
}
