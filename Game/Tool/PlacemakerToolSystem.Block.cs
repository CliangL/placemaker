// Block mode ("Paint bucket"): hover inside a city block → it highlights; click takes it; Shift+click adds or
// removes an adjacent block (the union must stay one simple outline). Face tracing is pure
// (Core/Geometry/FillGraph → PlanarGraph.PickFace, cursor-aware) and starts on the cursor's
// side of the nearest wall, so the exact block under the mouse is picked even at X junctions.
// s43: the player picks the walls (BrushRules.BucketWalls): roads, paths, bike paths and tracks trace the face;
// water, lots & buildings and zoning cut it afterwards, keeping the side that was clicked (the seed).

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
            public AreaShape Area;              // usable land of this block (curbs, area walls cut)
            public HashSet<long> Edges;
            public float2 Seed;
        }

        private BlockPick _hoverFace;
        private float2 _hoverFaceAt;
        private LocText _hoverFaceProblem;

        public BlockPick HoverFace => _hoverFace;
        public LocText HoverFaceProblem => _hoverFaceProblem;
        public BlockSelectionState Blocks => _blocks;

        /// <summary>The Paint bucket's walls (Options / the panel's Walls chips).</summary>
        public FillWall BucketWalls => BrushRules.BucketWalls;

        /// <summary>Real nets the bucket currently traces (overlay); empty outside Block mode.</summary>
        public IReadOnlyDictionary<global::Unity.Entities.Entity, FillWall> BucketGraphEdges => _roads.GraphEdges;
        public int BucketGraphVersion => _roads.GraphVersion;

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
                    _blocks.Seeds.Add(_hoverFace.Seed);
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
                if (idx < next.Seeds.Count) next.Seeds.RemoveAt(idx);
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
                // Seeds stay parallel to faces (old snapshots may have none: pad with the face's own click).
                while (next.Seeds.Count < next.Faces.Count) next.Seeds.Add(face.Seed);
                next.Faces.Add(new List<HalfEdge>(face.Loop));
                next.Seeds.Add(face.Seed);
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

        /// <summary>The block (face of the wall graph) containing <paramref name="p"/>, or null with a reason.</summary>
        private BlockPick PickFace(float2 p, out LocText problem)
        {
            problem = null;
            var loop = new List<HalfEdge>();
            // Block paint fences its strokes to road blocks only (its own stops do the rest).
            var walls = _mode == SelectionMode.Block ? BucketWalls : FillWall.Roads;
            if ((walls & FillWalls.Curves) == 0) { problem = new LocText("Placemaker.Problem.NoLineWalls"); return null; }
            FacePickStatus status;
            try { status = _roads.PickBlock(p, UnityEngine.Time.realtimeSinceStartup, loop, walls); }
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
            var area = FaceArea(loop, p, true);
            if (area == null) { problem = new LocText(_bucketWallsAll ? "Placemaker.Problem.OnlyWallsHere" : "Placemaker.Problem.NoBlockHere"); return null; }
            var edges = new HashSet<long>();
            foreach (var h in loop) edges.Add(h.Edge);
            return new BlockPick { Loop = loop, Area = area, Edges = edges, Seed = p };
        }

        /// <summary>Set when the last FaceArea found nothing but area walls.</summary>
        private bool _bucketWallsAll;

        /// <summary>Usable land of a loop: curb outline, then the area walls (keeping the seed's side).</summary>
        private AreaShape FaceArea(IReadOnlyList<HalfEdge> loop, float2 seed, bool hover)
        {
            _bucketWallsAll = false;
            AreaShape area;
            try
            {
                area = _roads.BuildFaceArea(loop, seed, AreaSource.Block, out bool needsAttachments);
                if (area != null && needsAttachments && !hover) WithFoundAttachments(area);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block outline failed: " + e.Message); return null; }
            return ApplyAreaWalls(area, seed, hover);
        }

        /// <summary>
        /// (s43) Water / lots &amp; buildings / zoning walls: cut out of the block, the piece on the clicked side kept.
        /// Other Placemaker builds count under "Lots &amp; buildings"; the group being edited never counts.
        /// </summary>
        private AreaShape ApplyAreaWalls(AreaShape s, float2 seed, bool hover)
        {
            var walls = _mode == SelectionMode.Block ? BucketWalls : FillWall.None;
            if (s?.Ring == null || s.Ring.Length < 3 || (walls & FillWalls.Areas) == 0) return s;
            try
            {
                var ring = s.Ring2D();
                Poly.Bounds(ring, out var mn, out var mx);
                _bpObstacles ??= new BlockObstacles(EntityManager, World);
                var list = new List<float2[]>();
                if ((walls & (FillWall.Lots | FillWall.Zoned)) != 0)
                    foreach (var o in _bpObstacles.Gather(mn - 1f, mx + 1f, _editingGroup))
                        if (o.Ring != null && FillWalls.Counts(walls, o.Kind)) list.Add(o.Ring);
                if ((walls & FillWall.Water) != 0)
                {
                    var wet = _bpObstacles.Water(mn - 4f, mx + 4f, 4f);
                    if (wet != null) list.AddRange(FillWalls.WaterRects(ring, wet));
                }
                if (list.Count == 0) return s;
                var cut = FillWalls.ClipToSeed(ring, list, seed, out int touched);
                if (cut == null) { _bucketWallsAll = touched > 0; return null; }
                if (ReferenceEquals(cut, ring)) return s;
                var shape = new AreaShape { Ring = Poly.To3D(cut, p => _site.SampleHeight(p)), Source = AreaSource.Painted };
                if (!hover) WithFoundAttachments(shape);
                return shape;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: bucket walls failed: " + e.Message);
                return s;
            }
        }

        /// <summary>(s43) The walls changed: re-trace the picked blocks from where they were clicked.</summary>
        private void RepickBlocks()
        {
            _hoverFace = null;
            _hoverFaceAt = new float2(float.MaxValue);
            if (_blocks.Faces.Count == 0) return;
            var next = new BlockSelectionState();
            for (int i = 0; i < _blocks.Seeds.Count && i < _blocks.Faces.Count; i++)
            {
                var pick = PickFace(_blocks.Seeds[i], out _);
                if (pick == null) continue;
                bool dup = false;
                foreach (var f in next.Faces) if (BlockPaintState.SameFace(f, pick.Loop)) { dup = true; break; }
                if (dup) continue;
                next.Faces.Add(pick.Loop);
                next.Seeds.Add(pick.Seed);
            }
            if (next.Faces.Count > 1 && !RoadGraphAlgorithms.UnionBoundary(next.Faces, new List<HalfEdge>()))
            {
                // The new walls split the selection into blocks that no longer form one outline: keep the last one.
                var last = next.Faces.Count - 1;
                next = new BlockSelectionState { Faces = { next.Faces[last] }, Seeds = { next.Seeds[last] } };
            }
            _blocks = next;
            ShapeChanged();
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
            float2 seed;
            if (_blocks.Seeds.Count > 0) seed = _blocks.Seeds[_blocks.Seeds.Count - 1];
            else
            {
                // Snapshot from before s43: no click recorded, the centre of the centre-line ring stands in.
                var c = Poly.To2D(_roads.CenterRing(loop));
                seed = c.Length >= 3 ? Poly.Centroid(c) : float2.zero;
            }
            return FaceArea(loop, seed, false);
        }
    }
}
