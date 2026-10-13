using System;
using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public enum SelectionMode { Draw = 0, Block = 1, Paint = 2, Perimeter = 3 }

    public static class SelectionModes
    {
        public static string Id(SelectionMode m)
        {
            switch (m)
            {
                case SelectionMode.Block: return "block";
                case SelectionMode.Paint: return "paint";
                case SelectionMode.Perimeter: return "perimeter";
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
                default: mode = SelectionMode.Draw; return false;
            }
        }

        public static AreaSource Source(SelectionMode m)
        {
            switch (m)
            {
                case SelectionMode.Block: return AreaSource.Block;
                case SelectionMode.Paint: return AreaSource.Painted;
                case SelectionMode.Perimeter: return AreaSource.Perimeter;
                default: return AreaSource.Drawn;
            }
        }
    }

    /// <summary>Picked blocks: each a CCW loop of road half-edges (ids are packed entities).</summary>
    public sealed class BlockSelectionState
    {
        public List<List<HalfEdge>> Faces = new List<List<HalfEdge>>();

        public BlockSelectionState Clone()
        {
            var c = new BlockSelectionState();
            foreach (var f in Faces) c.Faces.Add(new List<HalfEdge>(f));
            return c;
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
        /// <summary>The resulting area, or null when there is none.</summary>
        public AreaShape Shape;

        public Recipe Recipe;
        public string RecipeId = "";
        public string Focus = RecipePaths.Root;
        /// <summary>Step paths whose markers the player edited (auto-suggested markers are no longer re-applied there).</summary>
        public HashSet<string> MarkersEdited = new HashSet<string>(StringComparer.Ordinal);

        public ToolSnapshot With(Action<ToolSnapshot> change)
        {
            var c = (ToolSnapshot)MemberwiseClone();
            change?.Invoke(c);
            return c;
        }
    }
}
