// Block mode: hover inside a city block → it highlights; click takes it; Shift+click adds or
// removes an adjacent block (the union must stay one simple outline). Face tracing is pure
// (Core/Geometry/PlanarGraph.PickFace, cursor-aware) and starts on the cursor's
// side of the nearest road, so the exact block under the mouse is picked even at X junctions.

using System;
using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {

        public sealed class BlockPick
        {
            public List<HalfEdge> Loop;
            public AreaShape Area;              // curb outline of this block
            public HashSet<long> Edges;
        }

        private BlockPick _hoverFace;
        private float2 _hoverFaceAt;
        private LocText _hoverFaceProblem;

        public BlockPick HoverFace => _hoverFace;
        public LocText HoverFaceProblem => _hoverFaceProblem;
        public BlockSelectionState Blocks => _blocks;

        private void UpdateBlock()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit)
            {
                _cursor = _in.Hit;
                bool stillInside = _hoverFace?.Area != null && Poly.Contains(_hoverFace.Area.Ring2D(), _in.Hit.xz);
                if (!stillInside && math.distance(_in.Hit.xz, _hoverFaceAt) > 1f)
                {
                    _hoverFaceAt = _in.Hit.xz;
                    _hoverFace = PickFace(_in.Hit.xz, out _hoverFaceProblem);
                }
            }
            else _hoverFace = null;

            if (_in.LeftPressed)
            {
                if (_hoverFace == null) { Flash(_hoverFaceProblem ?? new LocText("Placemaker.Problem.NoBlockHere")); return; }
                if (!_in.Shift)
                {
                    _blocks = new BlockSelectionState();
                    _blocks.Faces.Add(new List<HalfEdge>(_hoverFace.Loop));
                    ShapeChanged();
                    Commit("Placemaker.Undo.SelectBlock");
                    if (_stage == ToolStage.Area && _shape != null) SetStage(ToolStage.Design);
                    return;
                }
                ToggleBlock(_hoverFace);
                return;
            }
            if (_in.RightPressed && _hoverFace != null)
            {
                int idx = IndexOfFace(_hoverFace);
                if (idx >= 0) ToggleBlock(_hoverFace);
            }
        }

        private int IndexOfFace(BlockPick face)
        {
            for (int i = 0; i < _blocks.Faces.Count; i++)
            {
                var f = _blocks.Faces[i];
                if (f.Count != face.Edges.Count) continue;
                bool same = true;
                foreach (var h in f) if (!face.Edges.Contains(h.Edge)) { same = false; break; }
                if (same) return i;
            }
            return -1;
        }

        private void ToggleBlock(BlockPick face)
        {
            var next = _blocks.Clone();
            int idx = IndexOfFace(face);
            string label;
            if (idx >= 0)
            {
                next.Faces.RemoveAt(idx);
                label = "Placemaker.Undo.RemoveBlock";
            }
            else
            {
                if (next.Faces.Count > 0)
                {
                    bool adjacent = false;
                    foreach (var f in next.Faces) if (RoadGraphAlgorithms.SharesEdge(f, face.Loop)) { adjacent = true; break; }
                    if (!adjacent) { Flash(new LocText("Placemaker.Problem.NotAdjacent")); return; }
                }
                next.Faces.Add(new List<HalfEdge>(face.Loop));
                label = "Placemaker.Undo.AddBlock";
            }
            if (next.Faces.Count > 1 && !RoadGraphAlgorithms.UnionBoundary(next.Faces, new List<HalfEdge>()))
            {
                Flash(new LocText(idx >= 0 ? "Placemaker.Problem.WouldSplit" : "Placemaker.Problem.WouldEncloseHole"));
                return;
            }
            _blocks = next;
            ShapeChanged();
            Commit(label);
            if (_stage == ToolStage.Area && _shape != null) SetStage(ToolStage.Design);
        }

        /// <summary>The block (face of the road graph) containing <paramref name="p"/>, or null with a reason.</summary>
        private BlockPick PickFace(float2 p, out LocText problem)
        {
            problem = null;
            var loop = new List<HalfEdge>();
            FacePickStatus status;
            try { status = _roads.PickBlock(p, UnityEngine.Time.realtimeSinceStartup, loop); }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: block pick failed: " + e.Message);
                problem = new LocText("Placemaker.Problem.NoBlockHere");
                return null;
            }
            switch (status)
            {
                case FacePickStatus.Ok: break;
                case FacePickStatus.EmptyGraph:
                case FacePickStatus.NoRoadNearby: problem = new LocText("Placemaker.Problem.NoRoadsNearby"); return null;
                case FacePickStatus.TooLarge: problem = new LocText("Placemaker.Problem.BlockTooLarge"); return null;
                case FacePickStatus.OutsideNetwork: problem = new LocText("Placemaker.Problem.NotEnclosed"); return null;
                default: problem = new LocText("Placemaker.Problem.NoBlockHere"); return null;
            }
            var area = _roads.BuildArea(loop, AreaSource.Block);
            if (area == null) { problem = new LocText("Placemaker.Problem.NoBlockHere"); return null; }
            var edges = new HashSet<long>();
            foreach (var h in loop) edges.Add(h.Edge);
            return new BlockPick { Loop = loop, Area = area, Edges = edges };
        }

        /// <summary>Shape for the picked blocks (union of faces).</summary>
        private AreaShape BuildBlockShape()
        {
            if (_blocks.Faces.Count == 0) return null;
            List<HalfEdge> loop;
            if (_blocks.Faces.Count == 1) loop = _blocks.Faces[0];
            else
            {
                loop = new List<HalfEdge>();
                if (!RoadGraphAlgorithms.UnionBoundary(_blocks.Faces, loop)) return null;
            }
            try { return _roads.BuildArea(loop, AreaSource.Block); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block outline failed: " + e.Message); return null; }
        }
    }
}
