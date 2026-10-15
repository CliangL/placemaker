using System;
using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public enum SelectionMode { Draw = 0, Block = 1, Paint = 2, Perimeter = 3, BlockPaint = 4 }

    public static class SelectionModes
    {
        /// <summary>Every mode, in panel order (state.areaModes).</summary>
        public static readonly SelectionMode[] All = { SelectionMode.Draw, SelectionMode.Block, SelectionMode.BlockPaint, SelectionMode.Paint, SelectionMode.Perimeter };

        public static string Id(SelectionMode m)
        {
            switch (m)
            {
                case SelectionMode.Block: return "block";
                case SelectionMode.Paint: return "paint";
                case SelectionMode.Perimeter: return "perimeter";
                case SelectionMode.BlockPaint: return "blockPaint";
                default: return "draw";
            }
        }

        public static bool TryParse(string id, out SelectionMode mode)
        {
            switch (id)
            {
                case "draw": mode = SelectionMode.Draw; return true;
                case "block": mode = SelectionMode.Block; return true;
                case "paint": mode = SelectionMode.Paint; return true;
                case "perimeter": mode = SelectionMode.Perimeter; return true;
                case "blockPaint": mode = SelectionMode.BlockPaint; return true;
                default: mode = SelectionMode.Draw; return false;
            }
        }

        public static AreaSource Source(SelectionMode m)
        {
            switch (m)
            {
                case SelectionMode.Block: return AreaSource.Block;
                case SelectionMode.Paint:
                case SelectionMode.BlockPaint: return AreaSource.Painted;
                case SelectionMode.Perimeter: return AreaSource.Perimeter;
                default: return AreaSource.Drawn;
            }
        }
    }

    /// <summary>Picked blocks: each a CCW loop of road half-edges (ids are packed entities).</summary>
    public sealed class BlockSelectionState
    {
        public List<List<HalfEdge>> Faces = new List<List<HalfEdge>>();
        /// <summary>(s43) Where each face was clicked (parallel to Faces; may be shorter in old snapshots): area walls keep that side.</summary>
        public List<Unity.Mathematics.float2> Seeds = new List<Unity.Mathematics.float2>();

        public BlockSelectionState Clone()
        {
            var c = new BlockSelectionState();
            foreach (var f in Faces) c.Faces.Add(new List<HalfEdge>(f));
            c.Seeds.AddRange(Seeds);
            return c;
        }
    }

    /// <summary>
    /// Block paint: the block the strokes are fenced to (CCW loop of road half-edges; empty = none yet)
    /// and the painted cells. The obstacles are not stored — the game layer gathers them again
    /// for the block (they are world state, not selection state).
    /// </summary>
    public sealed class BlockPaintState
    {
        public List<HalfEdge> Face = new List<HalfEdge>();
        public Placemaker.Core.Geometry.BrushMask.Snapshot Mask;
        /// <summary>Open-land fallback (no closed block under the first stroke): Face is empty, Seed is where it started.</summary>
        public bool Open;
        public Unity.Mathematics.float2 Seed;

        public BlockPaintState Clone() => new BlockPaintState { Face = new List<HalfEdge>(Face), Mask = Mask, Open = Open, Seed = Seed };

        /// <summary>Same block (same set of road edges), independent of where the loop starts.</summary>
        public static bool SameFace(IReadOnlyList<HalfEdge> a, IReadOnlyList<HalfEdge> b)
        {
            if (a == null || b == null || a.Count != b.Count || a.Count == 0) return false;
            var set = new HashSet<HalfEdge>(a);
            foreach (var h in b) if (!set.Contains(h)) return false;
            return true;
        }
    }

    /// <summary>Perimeter mode: clicked road nodes (with the path-filled ones) in order.</summary>
    public sealed class PerimeterSelectionState
    {
        public List<long> Nodes = new List<long>();
        public List<Unity.Mathematics.float3> Positions = new List<Unity.Mathematics.float3>();
        /// <summary>Half-edges between consecutive nodes (Count == Nodes.Count when closed, Nodes.Count-1 otherwise).</summary>
        public List<HalfEdge> Path = new List<HalfEdge>();
        public bool Closed;

        public PerimeterSelectionState Clone() => new PerimeterSelectionState
        {
            Nodes = new List<long>(Nodes),
            Positions = new List<Unity.Mathematics.float3>(Positions),
            Path = new List<HalfEdge>(Path),
            Closed = Closed,
        };
    }

    /// <summary>
    /// One undo step of the whole tool. Treated as immutable once committed: edits build a new
    /// snapshot (sharing unchanged parts by reference), so History can hold dozens cheaply.
    /// AreaShape and Recipe instances referenced here must never be mutated afterwards.
    /// </summary>
    public sealed class ToolSnapshot
    {
        public SelectionMode Mode;
        public PolygonEditor.Snapshot Draw = new PolygonEditor.Snapshot();
        public Placemaker.Core.Geometry.BrushMask.Snapshot Paint;
        public BlockSelectionState Block = new BlockSelectionState();
        public PerimeterSelectionState Perimeter = new PerimeterSelectionState();
        public BlockPaintState BlockPaint = new BlockPaintState();
        /// <summary>The resulting area, or null when there is none.</summary>
        public AreaShape Shape;

        public Recipe Recipe;
        public string RecipeId = "";
        public string Focus = RecipePaths.Root;
        /// <summary>Step paths whose markers the player edited (auto-suggested markers are no longer re-applied there).</summary>
        public HashSet<string> MarkersEdited = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Preset applied to the root step (layer + id; "" = none) — drives design.activePreset.</summary>
        public string PresetLayer = "", PresetId = "";

        public ToolSnapshot With(Action<ToolSnapshot> change)
        {
            var c = (ToolSnapshot)MemberwiseClone();
            change?.Invoke(c);
            return c;
        }
    }
}
