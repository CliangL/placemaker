// Placemaker — recognising our temporaries, game validation, adoption into the group (GPL-3.0).
// Error capture adapted from ParkManager's ParkBuildValidation (tomkpunkt, GPL-3.0); error-to-piece
// mapping for auto-exclusion from GridRoadGenerator's TryExcludeErrorSegments (Ti4goc, GPL-3.0).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private const float NetMatchTolerance = 1.0f;
        private const float ObjectMatchTolerance = 0.75f;
        private const float AreaMatchTolerance = 2.0f;
        private const float ErrorMatchTolerance = 2.0f;
        private const int MaxErrorMarkers = 64;

        private readonly List<Entity> _tagged = new List<Entity>();
        private string _errorKey = BuildErrorKeys.Other;
        private object[] _errorArgs = Array.Empty<object>();
        private string _errorTypeName = "";

        private struct CensusResult { public int Expected, Matched; }

        private bool IsOwnCandidate(Entity e, out Temp temp)
        {
            temp = EntityManager.GetComponentData<Temp>(e);
            if (temp.m_Original != Entity.Null || (temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0) return false;
            return true;
        }

        private bool TryMatch(Entity e, out long key, out WorkKind kind)
        {
            key = 0; kind = WorkKind.Net;
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            long pid = PrefabId(prefab);
            if (EntityManager.HasComponent<Edge>(e))
            {
                if (_netIndex == null || !EntityManager.HasComponent<Curve>(e)) return false;
                var mid = MathUtils.Position(EntityManager.GetComponentData<Curve>(e).m_Bezier, 0.5f);
                kind = WorkKind.Net;
                return _netIndex.TryMatch(mid.xz, pid, NetMatchTolerance, out key, out _);
            }
            if (EntityManager.HasComponent<global::Game.Net.Node>(e)) return false;
            if (EntityManager.HasComponent<Owner>(e)) return false;   // sub-elements go with their owner
            if (EntityManager.HasComponent<global::Game.Objects.Object>(e) && EntityManager.HasComponent<global::Game.Objects.Transform>(e))
            {
                if (_objectIndex == null) return false;
                var p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                kind = WorkKind.Object;
                return _objectIndex.TryMatch(p.xz, pid, ObjectMatchTolerance, out key, out _);
            }
            if (EntityManager.HasComponent<global::Game.Areas.Area>(e) && EntityManager.HasBuffer<global::Game.Areas.Node>(e))
            {
                if (_areaIndex == null) return false;
                kind = WorkKind.Area;
                return _areaIndex.TryMatch(AreaCentroid(e), pid, AreaMatchTolerance, out key, out _);
            }
            return false;
        }

        private float2 AreaCentroid(Entity e)
        {
            var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
            int n = nodes.Length;
            // Ignore a closing duplicate of the first node (it would bias the centroid of small rings).
            if (n > 1 && math.distancesq(nodes[0].m_Position.xz, nodes[n - 1].m_Position.xz) < 1e-4f) n--;
            float2 c = 0;
            for (int i = 0; i < n; i++) c += nodes[i].m_Position.xz;
            return n > 0 ? c / n : c;
        }

        private CensusResult Census()
        {
            var matched = new HashSet<long>();
            using (var temps = _tempQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < temps.Length; i++)
                {
                    if (!IsOwnCandidate(temps[i], out _)) continue;
                    if (TryMatch(temps[i], out long key, out _) && key != OwnerKey) matched.Add(key);
                }
            return new CensusResult { Expected = _itemByKey.Count, Matched = matched.Count };
        }

        // ───────────────────────── errors ─────────────────────────

        private void CollectErrors()
        {
            var positions = _status.ErrorPositions ?? (_status.ErrorPositions = new List<float3>());
            int count = 0;
            if (!_errorQuery.IsEmptyIgnoreFilter)
            {
                using var errors = _errorQuery.ToEntityArray(Allocator.Temp);
                count = errors.Length;
                for (int i = 0; i < errors.Length && positions.Count < MaxErrorMarkers; i++)
                    if (TryGetPosition(errors[i], out var p) && !ContainsNear(positions, p)) positions.Add(p);
            }
            // The error TYPE lives on the notification icon whose prefab carries ToolErrorData.
            if (string.IsNullOrEmpty(_errorTypeName) && !_iconQuery.IsEmptyIgnoreFilter)
            {
                using var icons = _iconQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < icons.Length; i++)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(icons[i]).m_Prefab;
                    if (!EntityManager.HasComponent<ToolErrorData>(prefab) || !BelongsToTemporary(icons[i])) continue;
                    _errorTypeName = EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error.ToString();
                    var at = EntityManager.GetComponentData<global::Game.Notifications.Icon>(icons[i]).m_Location;
                    if (positions.Count < MaxErrorMarkers && !ContainsNear(positions, at)) positions.Add(at);
                    break;
                }
            }
            if (count > 0 || !string.IsNullOrEmpty(_errorTypeName))
            {
                _errorKey = BuildErrorKeys.For(_errorTypeName);
                _errorArgs = new object[] { math.max(count, 1), _errorTypeName };
            }
        }

        private static bool ContainsNear(List<float3> list, float3 p)
        {
            foreach (var q in list) if (math.distancesq(q, p) < 1f) return true;
            return false;
        }

        private bool BelongsToTemporary(Entity icon)
        {
            if (EntityManager.HasComponent<Temp>(icon)) return true;
            if (!EntityManager.HasComponent<Owner>(icon)) return false;
            var owner = EntityManager.GetComponentData<Owner>(icon).m_Owner;
            return owner != Entity.Null && EntityManager.Exists(owner) && EntityManager.HasComponent<Temp>(owner);
        }

        private bool TryGetPosition(Entity e, out float3 p)
        {
            p = default;
            if (EntityManager.HasComponent<global::Game.Objects.Transform>(e)) { p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position; return true; }
            if (EntityManager.HasComponent<Curve>(e)) { p = MathUtils.Position(EntityManager.GetComponentData<Curve>(e).m_Bezier, 0.5f); return true; }
            if (EntityManager.HasComponent<global::Game.Net.Node>(e)) { p = EntityManager.GetComponentData<global::Game.Net.Node>(e).m_Position; return true; }
            if (EntityManager.HasBuffer<global::Game.Areas.Node>(e))
            {
                var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
                if (nodes.Length == 0) return false;
                var c = AreaCentroid(e);
                p = new float3(c.x, nodes[0].m_Position.y, c.y);
                return true;
            }
            return false;
        }

        /// <summary>
        /// US8: maps every errored temporary back to plan items and excludes them (reported per kind).
        /// Exact first (our temporary, or the temporary that owns the errored sub-element, matched by
        /// prefab + geometry), then by proximity for errors on foreign temporaries (an existing road
        /// copy that our piece collides with). An error on the owner lot drops only the owner lot.
        /// Returns the number of newly excluded items.
        /// </summary>
        private int ExcludeErroneousItems()
        {
            int newly = 0;
            using var errors = _errorQuery.ToEntityArray(Allocator.Temp);
            var probes = new List<float2>(3);
            for (int i = 0; i < errors.Length; i++)
            {
                var e = errors[i];
                // Climb to the top-level temporary (sub-objects/lanes go with their owner).
                var top = e;
                for (int depth = 0; depth < 4 && EntityManager.HasComponent<Owner>(top); depth++)
                {
                    var o = EntityManager.GetComponentData<Owner>(top).m_Owner;
                    if (o == Entity.Null || !EntityManager.Exists(o) || !EntityManager.HasComponent<Temp>(o)) break;
                    top = o;
                }
                if (EntityManager.HasComponent<Temp>(top) && EntityManager.HasComponent<PrefabRef>(top) && IsOwnCandidate(top, out _)
                    && TryMatch(top, out long exact, out var kind))
                {
                    if (exact == OwnerKey)
                    {
                        if (!_ownerSuppressed) { _ownerSuppressed = true; newly++; Mod.Log.Warn("Placemaker: the game rejected the owner lot; building without it."); }
                        continue;
                    }
                    if (_itemByKey.TryGetValue(exact, out var hitItem)) { newly += Exclude(hitItem); continue; }
                }
                probes.Clear();
                if (EntityManager.HasComponent<Curve>(e))
                {
                    var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    probes.Add(MathUtils.Position(b, 0.25f).xz);
                    probes.Add(MathUtils.Position(b, 0.5f).xz);
                    probes.Add(MathUtils.Position(b, 0.75f).xz);
                }
                else if (TryGetPosition(e, out var p)) probes.Add(p.xz);
                foreach (var probe in probes)
                {
                    long key = 0;
                    bool hit = (_netIndex != null && _netIndex.TryMatch(probe, 0, ErrorMatchTolerance, out key, out _))
                        || (_objectIndex != null && _objectIndex.TryMatch(probe, 0, ErrorMatchTolerance, out key, out _))
                        || (_areaIndex != null && _areaIndex.TryMatch(probe, 0, ErrorMatchTolerance, out key, out _));
                    if (!hit || key == OwnerKey || !_itemByKey.TryGetValue(key, out var w)) continue;
                    newly += Exclude(w);
                }
            }
            return newly;
        }

        private int Exclude(WorkItem w)
        {
            if (!_excluded.Add(PlanWork.Key(w))) return 0;
            string what = w.Kind == WorkKind.Net ? "Net" : w.Kind == WorkKind.Surface ? "Surface" : w.Kind == WorkKind.Area ? "Area" : "Object";
            NoteSkip("Placemaker.Build.Skipped." + what, new float3(w.Anchor.x, Height(w.Anchor), w.Anchor.y));
            return 1;
        }
        // ───────────────────────── adoption ─────────────────────────

        /// <summary>
        /// Tags our temporaries with the group id right before Apply. Apply converts a Temp with no
        /// original into the permanent entity in place, so the tag (and Owner/Attached of parking
        /// decals) survives. Nodes are ours only when every temporary edge on them is ours — a node
        /// splitting an existing road stays the city's.
        /// </summary>
        private int TagTemporaries()
        {
            _tagged.Clear();
            int unmatched = 0, unmatchedEdges = 0;
            var nodeRefs = new Dictionary<Entity, int2>();   // x = all temp edges, y = our edges
            bool carrier = _carrier != Entity.Null && EntityManager.Exists(_carrier);
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                bool isEdge = EntityManager.HasComponent<Edge>(e);
                bool own = IsOwnCandidate(e, out _);
                long key = 0; WorkKind kind = WorkKind.Net;
                bool matched = own && TryMatch(e, out key, out kind);
                if (isEdge)
                {
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    CountNode(nodeRefs, edge.m_Start, matched);
                    CountNode(nodeRefs, edge.m_End, matched);
                }
                if (matched && key == OwnerKey) { AdoptOwnerTemp(e); continue; }
                if (!matched || !_itemByKey.TryGetValue(key, out var w))
                {
                    // Playtest 4b: an object/area of ours the plan index could not place (random
                    // variant prefab, moved by the game, matched elsewhere) was applied UNTAGGED and
                    // survived removal. While our tool is active every top-level temporary without an
                    // original is created from our definitions, so it is ours. Edges stay match-only:
                    // pieces of an existing road split by our junctions are the city's.
                    if (own && !isEdge && IsAdoptable(e, out var adoptKind)) { Tag(e, adoptKind, MemberRole.Generic); unmatched++; }
                    else if (own && isEdge) unmatchedEdges++;
                    continue;
                }
                var role = RoleFor(w);
                var memberKind = kind == WorkKind.Net ? MemberKind.Edge : kind == WorkKind.Area ? MemberKind.Area : MemberKind.Object;
                Tag(e, memberKind, role);
                if (role == MemberRole.ParkingAisle && carrier)
                    EntityManager.GetBuffer<global::Game.Net.SubNet>(_carrier).Add(new global::Game.Net.SubNet(e));
                if (role == MemberRole.ParkingBay && carrier)
                {
                    // Bare carrier owner (PLT): LaneConnectionSystem walks Owner/Attached to find the
                    // aisles in the carrier's SubNet; an area owner would scatter the decals and an
                    // edge owner would cull them (RESEARCH-NOTES 13).
                    EntityManager.AddComponentData(e, new Owner(_carrier));
                    EntityManager.AddComponentData(e, new global::Game.Objects.Attached(_carrier, Entity.Null, 0f));
                }
            }
            foreach (var kv in nodeRefs)
            {
                var node = kv.Key;
                if (kv.Value.y == 0 || kv.Value.x != kv.Value.y) continue;
                if (!EntityManager.Exists(node) || !EntityManager.HasComponent<Temp>(node) || !IsOwnCandidate(node, out _)) continue;
                Tag(node, MemberKind.Node, MemberRole.Generic);
            }
            Mod.Log.Info($"Placemaker tagged {_tagged.Count} temporary parts for group {_groupId}"
                + (unmatched > 0 ? $" ({unmatched} objects/areas adopted without a plan match)" : "")
                + (unmatchedEdges > 0 ? $"; {unmatchedEdges} unmatched new edges left to the city" : "") + ".");
            return _tagged.Count;
        }

        /// <summary>Top-level object or area (sub-elements go with their owner; nodes are decided by their edges).</summary>
        private bool IsAdoptable(Entity e, out MemberKind kind)
        {
            kind = MemberKind.Object;
            if (!EntityManager.HasComponent<PrefabRef>(e) || EntityManager.HasComponent<Owner>(e)) return false;
            if (EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<global::Game.Net.Node>(e)) return false;
            if (EntityManager.HasComponent<global::Game.Objects.Object>(e) && EntityManager.HasComponent<global::Game.Objects.Transform>(e)) return true;
            if (EntityManager.HasComponent<global::Game.Areas.Area>(e)) { kind = MemberKind.Area; return true; }
            return false;
        }

        /// <summary>
        /// Apply frame: tags temporaries that appeared or changed after validation (an optional
        /// object whose collision cleared, a late CourseSplit piece) so nothing is applied untagged.
        /// </summary>
        private int TagLateTemporaries()
        {
            int late = 0;
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                if (EntityManager.HasComponent<PlacemakerMember>(e) || !IsOwnCandidate(e, out _)) continue;
                if (EntityManager.HasComponent<Edge>(e))
                {
                    if (TryMatch(e, out long key, out _) && key != OwnerKey && _itemByKey.ContainsKey(key)) { Tag(e, MemberKind.Edge, MemberRole.Generic); late++; }
                    continue;
                }
                if (IsAdoptable(e, out var kind)) { Tag(e, kind, MemberRole.Generic); late++; }
            }
            if (late > 0) Mod.Log.Info($"Placemaker tagged {late} late temporary parts on the Apply frame.");
            return late;
        }

        // ───────────────────────── strays and verification ─────────────────────────

        private readonly HashSet<Entity> _preexisting = new HashSet<Entity>();
        private EntityQuery _strayQuery;
        private float2[] _strayOutline;

        private EntityQuery StrayQuery()
        {
            if (_strayQuery == default)
                _strayQuery = GetEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                    None = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
                });
            return _strayQuery;
        }

        /// <summary>Remembers every top-level object inside the outline before the build touches anything.</summary>
        private void SnapshotPreexisting()
        {
            _preexisting.Clear();
            _strayOutline = BuildOutline();
            if (_strayOutline == null) return;
            ForObjectsInOutline((e, _) => _preexisting.Add(e));
        }

        private void ForObjectsInOutline(Action<Entity, Entity> visit)
        {
            var outline = _strayOutline;
            if (outline == null) return;
            float2 min = outline[0], max = outline[0];
            foreach (var p in outline) { min = math.min(min, p); max = math.max(max, p); }
            min -= 2f; max += 2f;
            var q = StrayQuery();
            using var entities = q.ToEntityArray(Allocator.Temp);
            using var transforms = q.ToComponentDataArray<global::Game.Objects.Transform>(Allocator.Temp);
            using var prefabs = q.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var p = transforms[i].m_Position.xz;
                if (p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y) continue;
                if (!ReservedSpace.PointInPolygon(p, outline) && TerraformDabs.DistanceToBoundary(outline, p) > 2f) continue;
                visit(entities[i], prefabs[i].m_Prefab);
            }
        }

        /// <summary>
        /// After Apply: a permanent, untagged, top-level object inside the outline that did not exist
        /// before the build and uses one of the prefabs this build placed was created by this build
        /// (e.g. a temporary the game replaced on Apply). Adopt it so removal deletes it too.
        /// </summary>
        private int AdoptStrays()
        {
            if (_strayOutline == null || _usedPrefabs.Count == 0) return 0;
            var strays = new List<Entity>();
            ForObjectsInOutline((e, prefab) =>
            {
                if (_preexisting.Contains(e) || !_usedPrefabs.Contains(prefab) || EntityManager.HasComponent<PlacemakerMember>(e)) return;
                strays.Add(e);
            });
            foreach (var e in strays) Tag(e, MemberKind.Object, MemberRole.Generic);
            if (strays.Count > 0) Mod.Log.Warn($"Placemaker adopted {strays.Count} untagged objects this build created (tag lost on Apply).");
            return strays.Count;
        }

        /// <summary>Counts permanent members per kind and logs mismatches against the plan.</summary>
        private void VerifyMembers()
        {
            try
            {
                var counts = new MemberCounts();
                var members = new List<Entity>();
                _registry.GetMembers(_groupId, members);
                int ownerAreas = 0;
                foreach (var e in members)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e)) continue;
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                    if (tag.GroupId != _groupId) continue;
                    if (tag.Role == MemberRole.OwnerArea) { ownerAreas++; continue; }
                    var kind = tag.Kind;
                    if (EntityManager.HasComponent<Edge>(e)) kind = MemberKind.Edge;
                    else if (EntityManager.HasComponent<global::Game.Net.Node>(e)) kind = MemberKind.Node;
                    counts.Add(kind);
                }
                var planned = new PlannedCounts
                {
                    Objects = _emittedObjects,
                    OptionalDropped = _dropped,
                    Areas = _emittedAreas,
                    NetPieces = _work?.Nets.Count ?? 0,
                };
                var issues = BuildVerification.Compare(planned, counts);
                if (issues.Count == 0) Mod.Log.Info($"Placemaker verified group {_groupId}: {counts}.");
                else Mod.Log.Warn($"Placemaker group {_groupId} member check ({counts}): {string.Join("; ", issues)}.");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker member verification failed: " + e.Message); }
        }

        private static void CountNode(Dictionary<Entity, int2> refs, Entity node, bool own)
        {
            if (node == Entity.Null) return;
            refs.TryGetValue(node, out var v);
            v.x++;
            if (own) v.y++;
            refs[node] = v;
        }

        private MemberRole RoleFor(WorkItem w)
        {
            var layer = _request.Plan.Layers[w.Layer];
            switch (w.Kind)
            {
                case WorkKind.Net:
                    var role = layer.Nets[w.Index].Role;
                    return w.GeneratorId == "parking" && (role == NetRole.DriveAisle || role == NetRole.DriveConnector)
                        ? MemberRole.ParkingAisle : MemberRole.Generic;
                case WorkKind.Object:
                    return layer.Objects[w.Index].Role == ObjectRole.ParkingBay ? MemberRole.ParkingBay : MemberRole.Generic;
                case WorkKind.Surface: return MemberRole.Surface;
                default: return MemberRole.Generic;
            }
        }

        private void Tag(Entity e, MemberKind kind, MemberRole role)
        {
            var tag = new PlacemakerMember { GroupId = _groupId, Kind = kind, Role = role };
            if (EntityManager.HasComponent<PlacemakerMember>(e)) EntityManager.SetComponentData(e, tag);
            else EntityManager.AddComponentData(e, tag);
            _tagged.Add(e);
        }

        private int CountTaggedTemporaries()
        {
            int n = 0;
            foreach (var e in _tagged)
                if (EntityManager.Exists(e) && EntityManager.HasComponent<Temp>(e) && !EntityManager.HasComponent<Deleted>(e)) n++;
            return n;
        }

        // ───────────────────────── record ─────────────────────────

        private void CreateRecord()
        {
            SnapshotPreexisting();
            _groupId = _registry.AllocateId();
            var rec = new GroupRecord
            {
                GroupId = _groupId,
                RecipeId = _request.Recipe?.Id ?? "",
                RecipeNameKey = _request.Recipe?.NameKey ?? "",
                CustomName = _request.CustomName ?? "",
                BuiltAtTicks = DateTime.UtcNow.Ticks,
            };
            _record = _registry.CreateRecord(rec, true);

            // Carrier for parking decals (only when the plan has stalls).
            Entity decal = Entity.Null;
            foreach (var w in _work.Objects)
                if (w.Category == AssetCategories.ParkingDecal && (decal = PrefabOf(w)) != Entity.Null) break;
            if (decal == Entity.Null) return;
            _carrier = EntityManager.CreateEntity();
            // A PrefabRef is required: validation jobs walk Owner chains and read PrefabRef unchecked.
            EntityManager.AddComponentData(_carrier, new PrefabRef { m_Prefab = decal });
            // Owner buffers must exist even when empty (Sub*ReferencesSystem index them unchecked).
            EntityManager.AddBuffer<global::Game.Objects.SubObject>(_carrier);
            EntityManager.AddBuffer<global::Game.Net.SubNet>(_carrier);
            EntityManager.AddBuffer<global::Game.Areas.SubArea>(_carrier);
            EntityManager.AddComponentData(_carrier, new PlacemakerMember { GroupId = _groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.Carrier });
            // Deliberately no Created/Updated: CS2 would try to regenerate the bare carrier (PLT).
            var g = EntityManager.GetComponentData<PlacemakerGroup>(_record);
            g.Carrier = _carrier;
            EntityManager.SetComponentData(_record, g);
        }

        private void ClearCarrierSubNets()
        {
            if (_carrier == Entity.Null || !EntityManager.Exists(_carrier) || !EntityManager.HasBuffer<global::Game.Net.SubNet>(_carrier)) return;
            var buffer = EntityManager.GetBuffer<global::Game.Net.SubNet>(_carrier);
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                var s = buffer[i].m_SubNet;
                if (!EntityManager.Exists(s) || EntityManager.HasComponent<Temp>(s) || EntityManager.HasComponent<Deleted>(s)) buffer.RemoveAt(i);
            }
        }

        private void RemoveUnderConstruction()
        {
            if (_record != Entity.Null && EntityManager.Exists(_record) && EntityManager.HasComponent<PlacemakerUnderConstruction>(_record))
                EntityManager.RemoveComponent<PlacemakerUnderConstruction>(_record);
        }

        private void DeleteRecordAndCarrier()
        {
            if (_carrier != Entity.Null && EntityManager.Exists(_carrier) && !EntityManager.HasComponent<Deleted>(_carrier))
                EntityManager.AddComponent<Deleted>(_carrier);
            if (_record != Entity.Null && EntityManager.Exists(_record) && !EntityManager.HasComponent<Deleted>(_record))
                EntityManager.AddComponent<Deleted>(_record);
            _registry.MarkDirty();
        }

        private void FinishRecord()
        {
            var rec = _registry.GetRecord(_groupId) ?? new GroupRecord { GroupId = _groupId };
            var plan = _request.Plan;
            rec.Stats = plan.MergedStats();
            rec.Facts.Clear();
            foreach (var l in plan.Layers)
                foreach (var kv in l.Facts)
                    rec.Facts[kv.Key] = (rec.Facts.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            var estimate = Pricing.Of(plan, _catalog.InfoMap());
            rec.Cost = estimate.Cost;
            rec.Upkeep = estimate.Upkeep;
            rec.PrefabNames = new List<string>(_usedPrefabNames);
            rec.PrefabNames.Sort(StringComparer.Ordinal);
            if (_work.HasPermanentTerrain || HasPaint()) rec.Flags |= GroupFlags.PermanentTerrain;   // terraform and paint are never undone
            if (_work.HasParking && _carrier != Entity.Null) rec.Flags |= GroupFlags.ParkingEconomy;
            if (_request.Recipe != null) rec.Recipe = _request.Recipe.Clone();
            if (_request.Area != null)
            {
                rec.Area = new AreaShape
                {
                    Ring = (float3[])_request.Area.Ring.Clone(),
                    Source = _request.Area.Source,
                    Attachments = new List<BoundaryAttachment>(_request.Area.Attachments ?? new List<BoundaryAttachment>()),
                };
            }
            if (EntityManager.Exists(_record))
            {
                _registry.WriteRecord(_record, rec);
                var g = EntityManager.GetComponentData<PlacemakerGroup>(_record);
                g.BuiltMembers = _registry.CountMembers(_groupId, false);
                EntityManager.SetComponentData(_record, g);
            }
            RemoveUnderConstruction();
            _registry.MarkDirty();
            try { World.GetOrCreateSystemManaged<Economy.ParkingEconomySystem>().OnGroupBuilt(_groupId); }
            catch (Exception e) { Mod.Log.Warn("Placemaker parking economy setup failed (build kept): " + e.Message); }
        }
    }
}
