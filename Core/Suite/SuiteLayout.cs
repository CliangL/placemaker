// In-game test suite (docs/teardown/10-ingame-suite.md): the fixture's geometry, pure so it is unit-tested offline.
//
// The suite starts a new game on the game's own flat default world (no map: TerrainSystem's default heightmap,
// every texel 8191/65535 of 4096 m ≈ 511.98 m), reshapes a few areas through one heightmap replacement, then lays
// a grid of vanilla roads. Every block of the grid hosts one scenario. Block kinds:
//   Zoned   row 1: residential zoning on the south street side, grown before the scenarios run
//   Survive row 2, column 4: plopped buildings that every build there must leave standing
//   Rough   row 4, columns 0-2: ±4 m hills inside the block (the roads around stay level)
//   Slope   rows 5-6: the ground climbs 6 % to the north (z), roads included
//   Flat    everything else
// Street types vary per grid line (4-lane avenue, 2-lane street, pedestrian street) so blocks face mixed roads.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Suite
{
    public enum SuiteBlockKind { Flat = 0, Zoned = 1, Slope = 2, Rough = 3, Survive = 4 }

    public sealed class SuiteRoadSegment
    {
        public string Prefab = "";
        public float2 A, B;
        /// <summary>True for lines running along z (constant x).</summary>
        public bool Vertical;
        public int Line;
    }

    public sealed class SuiteBlock
    {
        public int I, J;
        public SuiteBlockKind Kind;
        /// <summary>Road centre lines around the block (west, east, south, north).</summary>
        public float X0, X1, Z0, Z1;
        public string West = "", East = "", South = "", North = "";
        public string Id => "B" + I + "-" + J;
        public float2 Center => new float2((X0 + X1) * 0.5f, (Z0 + Z1) * 0.5f);

        /// <summary>The land inside the kerbs, given the half widths of the four roads (+ a margin).</summary>
        public float2[] Interior(Func<string, float> halfWidth, float margin = 0f)
        {
            float w = halfWidth(West) + margin, e = halfWidth(East) + margin, s = halfWidth(South) + margin, n = halfWidth(North) + margin;
            return new[] { new float2(X0 + w, Z0 + s), new float2(X1 - e, Z0 + s), new float2(X1 - e, Z1 - n), new float2(X0 + w, Z1 - n) };
        }
    }

    public sealed class SuiteLayout
    {
        public const string SmallRoad = "Small Road";
        public const string MediumRoad = "Medium Road";
        public const string PedestrianStreet = "Pedestrian Street";

        /// <summary>TerrainSystem's default heightmap value (8191) on the default 4096 m height scale.</summary>
        public const float BaseHeight = 8191f / 65535f * 4096f;
        public const float HeightScale = 4096f;
        public const int HeightmapSize = 4096;
        public const float MapSize = 14336f;
        public const float SlopeGrade = 0.06f;
        public const float RoughAmplitude = 4f;

        public int NX = 9, NZ = 7;
        public float X0 = -700f, Z0 = -500f, PX = 150f, PZ = 140f;

        public float LineX(int i) => X0 + i * PX;
        public float LineZ(int j) => Z0 + j * PZ;

        /// <summary>Street along x = LineX(i).</summary>
        public string VerticalRoad(int i) => i % 4 == 0 ? MediumRoad : i % 4 == 3 ? PedestrianStreet : SmallRoad;
        /// <summary>Street along z = LineZ(j).</summary>
        public string HorizontalRoad(int j) => j % 3 == 0 ? MediumRoad : SmallRoad;

        public SuiteBlockKind Kind(int i, int j)
        {
            if (j >= 5) return SuiteBlockKind.Slope;
            if (j == 4 && i <= 2) return SuiteBlockKind.Rough;
            if (j == 2 && i == 4) return SuiteBlockKind.Survive;
            if (j == 1) return SuiteBlockKind.Zoned;
            return SuiteBlockKind.Flat;
        }

        public List<SuiteBlock> Blocks()
        {
            var r = new List<SuiteBlock>(NX * NZ);
            for (int j = 0; j < NZ; j++)
                for (int i = 0; i < NX; i++)
                    r.Add(new SuiteBlock
                    {
                        I = i, J = j, Kind = Kind(i, j),
                        X0 = LineX(i), X1 = LineX(i + 1), Z0 = LineZ(j), Z1 = LineZ(j + 1),
                        West = VerticalRoad(i), East = VerticalRoad(i + 1), South = HorizontalRoad(j), North = HorizontalRoad(j + 1),
                    });
            return r;
        }

        /// <summary>One segment per block side, junction to junction (shared ends merge into nodes).</summary>
        public List<SuiteRoadSegment> Roads()
        {
            var r = new List<SuiteRoadSegment>();
            for (int i = 0; i <= NX; i++)
                for (int j = 0; j < NZ; j++)
                    r.Add(new SuiteRoadSegment { Prefab = VerticalRoad(i), Vertical = true, Line = i, A = new float2(LineX(i), LineZ(j)), B = new float2(LineX(i), LineZ(j + 1)) });
            for (int j = 0; j <= NZ; j++)
                for (int i = 0; i < NX; i++)
                    r.Add(new SuiteRoadSegment { Prefab = HorizontalRoad(j), Vertical = false, Line = j, A = new float2(LineX(i), LineZ(j)), B = new float2(LineX(i + 1), LineZ(j)) });
            return r;
        }

        // ───────────── terrain ─────────────

        private float SlopeStart => LineZ(5);
        private float SlopeEnd => LineZ(NZ) + 60f;

        public float SlopeRise(float z) => SlopeGrade * math.clamp(z - SlopeStart, 0f, SlopeEnd - SlopeStart);

        /// <summary>Hills inside the rough blocks, fading to zero 14 m inside the road centre lines.</summary>
        public float RoughBump(float x, float z)
        {
            const int j = 4;
            if (z <= LineZ(j) || z >= LineZ(j + 1)) return 0f;
            for (int i = 0; i <= 2; i++)
            {
                if (Kind(i, j) != SuiteBlockKind.Rough) continue;
                float x0 = LineX(i), x1 = LineX(i + 1);
                if (x <= x0 || x >= x1) continue;
                float d = math.min(math.min(x - x0, x1 - x), math.min(z - LineZ(j), LineZ(j + 1) - z));
                float mask = math.smoothstep(14f, 30f, d);
                return RoughAmplitude * math.sin(x / 11f) * math.cos(z / 9f) * mask;
            }
            return 0f;
        }

        public float Height(float x, float z) => BaseHeight + SlopeRise(z) + RoughBump(x, z);

        /// <summary>World position of heightmap texel (px, py): the map spans [-MapSize/2, MapSize/2] on both axes.</summary>
        public static float2 TexelWorld(int px, int py)
        {
            float cell = MapSize / HeightmapSize;
            return new float2(-MapSize * 0.5f + (px + 0.5f) * cell, -MapSize * 0.5f + (py + 0.5f) * cell);
        }

        public static ushort Encode(float height) => (ushort)math.clamp((int)math.round(height / HeightScale * 65535f), 0, 65535);

        /// <summary>The full R16 heightmap (row = z, column = x), default height everywhere outside the shaped areas.</summary>
        public ushort[] Heightmap()
        {
            int n = HeightmapSize;
            var data = new ushort[n * n];
            ushort baseValue = 8191;
            float cell = MapSize / n;
            float roughX0 = LineX(0) - 1f, roughX1 = LineX(3) + 1f, roughZ0 = LineZ(4) - 1f, roughZ1 = LineZ(5) + 1f;
            for (int py = 0; py < n; py++)
            {
                float z = -MapSize * 0.5f + (py + 0.5f) * cell;
                float rise = SlopeRise(z);
                bool roughRow = z > roughZ0 && z < roughZ1;
                ushort rowValue = rise > 0f ? Encode(BaseHeight + rise) : baseValue;
                int o = py * n;
                for (int px = 0; px < n; px++) data[o + px] = rowValue;
                if (!roughRow) continue;
                for (int px = 0; px < n; px++)
                {
                    float x = -MapSize * 0.5f + (px + 0.5f) * cell;
                    if (x < roughX0 || x > roughX1) continue;
                    float b = RoughBump(x, z);
                    if (b != 0f) data[o + px] = Encode(BaseHeight + rise + b);
                }
            }
            return data;
        }
    }
}
