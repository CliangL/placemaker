// Placemaker — what the Block paint brush stops at, player-configurable ("Brush rules").
//
// Each obstacle kind can be switched off, the block lock too, and the curb snap distance and edge
// smoothing are sliders. With every stop and the block lock off the brush is plain Paint (no fence,
// Paint's outline pipeline), so the old behaviour is always one click away. Plain Paint mode itself
// never reads these rules.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public enum BrushObstacleKind { Road = 0, Building = 1, Lot = 2, ZonedCell = 3, EmptyZoneCell = 4 }

    /// <summary>A polygon the brush may stop at, tagged with what it is (rules decide whether it counts).</summary>
    public struct BrushObstacle
    {
        public BrushObstacleKind Kind;
        public float2[] Ring;
        public BrushObstacle(BrushObstacleKind kind, float2[] ring) { Kind = kind; Ring = ring; }
    }

    public sealed class BrushRules : IEquatable<BrushRules>
    {
        public const float DefaultSnap = 6f;
        public const float MaxSnap = 20f;
        public const int MaxSmoothing = 6;

        public bool Roads = true, Buildings = true, Lots = true, Zoned = true, EmptyZones = false, Water = true;
        /// <summary>Lock a stroke to the closed block it starts in (off: only the obstacles limit it).</summary>
        public bool StayInBlock = true;
        /// <summary>Gap (m) closed by pulling an edge onto the curb / an obstacle; 0 = off.</summary>
        public float Snap = DefaultSnap;
        /// <summary>Moving-average passes for the painted edge (0–6; Paint uses 6).</summary>
        public int Smoothing = BlockBrush.SmoothPasses;

        public static BrushRules Defaults() => new BrushRules();

        public BrushRules Clone() => (BrushRules)MemberwiseClone();

        /// <summary>Clamps the sliders into range (settings files can hold anything).</summary>
        public BrushRules Normalized()
        {
            var r = Clone();
            r.Snap = float.IsNaN(r.Snap) ? DefaultSnap : math.clamp(r.Snap, 0f, MaxSnap);
            r.Smoothing = math.clamp(r.Smoothing, 0, MaxSmoothing);
            return r;
        }

        public bool Stops(BrushObstacleKind k)
        {
            switch (k)
            {
                case BrushObstacleKind.Road: return Roads;
                case BrushObstacleKind.Building: return Buildings;
                case BrushObstacleKind.Lot: return Lots;
                case BrushObstacleKind.ZonedCell: return Zoned;
                case BrushObstacleKind.EmptyZoneCell: return EmptyZones;
                default: return true;
            }
        }

        /// <summary>Nothing to stop at and no block lock: the brush is plain Paint.</summary>
        public bool AllOff => !Roads && !Buildings && !Lots && !Zoned && !EmptyZones && !Water && !StayInBlock;

        public bool Equals(BrushRules o) => o != null && Roads == o.Roads && Buildings == o.Buildings && Lots == o.Lots && Zoned == o.Zoned
            && EmptyZones == o.EmptyZones && Water == o.Water && StayInBlock == o.StayInBlock && Snap == o.Snap && Smoothing == o.Smoothing;
        public override bool Equals(object obj) => Equals(obj as BrushRules);
        public override int GetHashCode() => (Roads ? 1 : 0) | (Buildings ? 2 : 0) | (Lots ? 4 : 0) | (Zoned ? 8 : 0) | (EmptyZones ? 16 : 0)
            | (Water ? 32 : 0) | (StayInBlock ? 64 : 0) | (Smoothing << 7) ^ Snap.GetHashCode();

        /// <summary>
        /// The brush for a stroke starting at <paramref name="start"/>: fenced to <paramref name="block"/>
        /// (the closed block under the start, or null) when StayInBlock is on and a block exists, else the
        /// open work box around the start (kept on the start's side of the roads when Roads is on);
        /// unconstrained (= Paint) when nothing is left to stop at. Obstacles the rules switch off are
        /// ignored, water only counts when Water is on.
        /// </summary>
        public BlockBrush Build(float2 start, IReadOnlyList<float2> block, IEnumerable<BrushObstacle> obstacles,
            Func<float2, bool> underWater = null, float cellSize = BrushMask.DefaultCellSize)
        {
            var r = Normalized();
            var rings = new List<IReadOnlyList<float2>>();
            if (obstacles != null)
                foreach (var o in obstacles)
                    if (o.Ring != null && r.Stops(o.Kind)) rings.Add(o.Ring);
            var water = r.Water ? underWater : null;
            BlockBrush brush;
            if (r.StayInBlock && block != null && block.Count >= 3) brush = new BlockBrush(block, rings, cellSize, water);
            else if (rings.Count == 0 && water == null) brush = BlockBrush.Free(start, cellSize);
            else brush = BlockBrush.Open(start, rings, cellSize, water, BlockBrush.OpenHalfSize, keepSide: r.Roads);
            brush.SnapDistance = r.Snap;
            brush.Smoothing = r.Smoothing;
            return brush;
        }
    }
}
