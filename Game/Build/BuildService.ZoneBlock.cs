// Placemaker — zone blocking lots after the build is applied (s19, GPL-3.0).
//
// Playtest: zoned buildings grew over a plaza. CS2 only blocks zone cells under ground nets and PHYSICAL areas
// (Game.Zones.CellBlockJobs, RESEARCH-NOTES-ADDENDUM 67–72); the owner lot overlaps by design and surfaces are
// never physical. Core/Build/ZoneGuard plans ComposedPlan.ZoneBlockers = the footprint minus the zoned cells
// and the existing building lots. They are built here as invisible physical "Placemaker Zone Block" lots, in
// their own transaction once everything else is permanent:
//  * a physical lot reports overlaps with our nets / non-overridable objects / lots; they must not make the
//    main build exclude its own parts, and Apply builds errored temporaries anyway (the tool gate is ours);
//  * it would demolish a spawnable building it overlaps (Warning): such pieces are dropped and the rest
//    re-emitted (≤ 3 rounds); if a warning stays, no blocker is built at all;
//  * the zoning step waits after them, so the Marquee zoning only finds the cells that stay visible.
// Members: Auxiliary / ZoneBlocker, so removing the build removes the blocking (the game unblocks the cells;
// zoning the player painted before comes back, as when a vanilla road is removed; grown shops stay).

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Setting "Keep zoned buildings off builds" (default on); forwarded to Core ZoneGuard too.</summary>
        public static bool BlockZoning
        {
            get => ZoneGuard.Enabled;
            set => ZoneGuard.Enabled = value;
        }

        private const int BlockerMaterializeFrames = 60;
        private const int BlockerValidateFrames = 4;
        private const int BlockerRounds = 3;

        private readonly List<float2[]> _blockerRings = new List<float2[]>();
        private readonly List<Entity> _blockerTemps = new List<Entity>();
        private Entity _blockerPrefab;
        private int _blockerRound;
        private int _blockerPhase;     // 0 emit, 1 wait temps, 2 validated → apply, 3 settle, 4 clear for a retry
        private int _blockerSince;
        private int _blockersBuilt;

        private bool HasBlockers =>
            BlockZoning && !LooseBuild && _groupId != 0 && _request?.Plan?.ZoneBlockers != null && _request.Plan.ZoneBlockers.Count > 0;

        /// <summary>The step after the main Apply settled: blockers, then zoning, then finish.</summary>
        private void GoAfterApply()
        {
            if (HasBlockers)
            {
                _blockerRings.Clear();
                foreach (var r in _request.Plan.ZoneBlockers) if (r != null && r.Length >= 3) _blockerRings.Add(r);
                // (s22) What the blocking keeps free, for the in-game check (cutout cells must never be under a blocker).
                int zonedCells = 0, touched = 0;
                double m2 = 0;
                foreach (var r in _blockerRings) m2 += Poly.Area(r);
                if (_request.Plan.ZoneCells != null)
                    foreach (var c in _request.Plan.ZoneCells)
                    {
                        if (c.State != ZoneCellState.Zoned) continue;
                        zonedCells++;
                        var sq = new[] { c.A, c.B, c.C, c.D };
                        var centre = 0.25f * (c.A + c.B + c.C + c.D);
                        for (int i = 0; i < 4; i++) sq[i] += math.normalizesafe(centre - sq[i]) * 0.03f;
                        foreach (var r in _blockerRings) if (ObstacleCut.Overlaps(sq, r)) { touched++; break; }
                    }
                Mod.Log.Info($"Placemaker zone blocking: {_blockerRings.Count} lot ring(s), {m2:F0} m², keeping {zonedCells} cutout cell(s) free"
                    + (touched > 0 ? $" — {touched} of them touched by a blocker (bug)." : "."));
                _blockerRound = 0;
                _blockerPhase = 0;
                Go(Step.ZoneBlock);
                return;
            }
            GoZoning();
        }

        private void GoZoning() => Go(_work.Zoning.Count > 0 ? Step.ZoningWait : Step.Finish);

        private void ResetZoneBlock()
        {
            _blockerRings.Clear();
            _blockerTemps.Clear();
            _blockerPrefab = Entity.Null;
            _blockerRound = 0;
            _blockerPhase = 0;
            _blockersBuilt = 0;
        }

        private void DoZoneBlock(int frame)
        {
            switch (_blockerPhase)
            {
                case 0:
                {
                    _blockerPrefab = _ownerPrefabs != null ? _ownerPrefabs.ReadyBlockerPrefab : Entity.Null;
                    if (_blockerPrefab == Entity.Null || _blockerRings.Count == 0)
                    {
                        if (_blockerPrefab == Entity.Null) Mod.Log.Warn("Placemaker: the zone block lot prefab is not available; zone cells under this build stay as they are.");
                        GoZoning();
                        return;
                    }
                    _heightsValid = false;
                    foreach (var ring in _blockerRings) EmitBlockerDefinition(ring);
                    _blockerSince = frame;
                    _blockerPhase = 1;
                    return;
                }
                case 1:
                {
                    CollectBlockerTemps();
                    bool all = _blockerTemps.Count >= _blockerRings.Count;
                    if (!all && frame - _blockerSince < BlockerMaterializeFrames) return;
                    if (_blockerTemps.Count == 0)
                    {
                        Mod.Log.Warn("Placemaker: no zone block lot materialised; zone cells under this build stay as they are.");
                        ClearBlockers();
                        return;
                    }
                    _blockerSince = frame;
                    _blockerPhase = 2;
                    return;
                }
                case 2:
                {
                    if (frame - _blockerSince < BlockerValidateFrames) return;
                    CollectBlockerTemps();
                    // A piece that would demolish a building (spawnable buildings are DeleteOverridden): drop it.
                    var lots = WarnedBuildingLots();
                    if (lots.Count > 0)
                    {
                        int before = _blockerRings.Count;
                        _blockerRings.RemoveAll(r => lots.Exists(l => ObstacleCut.Overlaps(r, l)));
                        int dropped = before - _blockerRings.Count;
                        if (_blockerRound++ < BlockerRounds && (dropped > 0 || _blockerRings.Count > 0))
                        {
                            Mod.Log.Info($"Placemaker: {lots.Count} building(s) stood under the zone blocking; left out {dropped} piece(s), retrying.");
                            DestroyOwnDefinitions();
                            _tool.Requested = ApplyMode.Clear;
                            _blockerSince = frame;
                            _blockerPhase = 4;
                            return;
                        }
                        Mod.Log.Warn("Placemaker: zone blocking would remove existing buildings; it is left out for this build.");
                        ClearBlockers();
                        return;
                    }
                    foreach (var t in _blockerTemps)
                        if (EntityManager.Exists(t)) EntityManager.AddComponentData(t, new PlacemakerMember { GroupId = _groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.ZoneBlocker });
                    _tool.Requested = ApplyMode.Apply;
                    _blockerSince = frame;
                    _blockerPhase = 3;
                    return;
                }
                case 3:
                {
                    if (frame - _blockerSince < ApplySettleFrames) return;
                    if (frame - _blockerSince == ApplySettleFrames) DestroyOwnDefinitions();
                    int still = 0;
                    foreach (var t in _blockerTemps) if (EntityManager.Exists(t) && EntityManager.HasComponent<Temp>(t)) still++;
                    if (still > 0 && frame - _blockerSince < ApplyTimeoutFrames) return;
                    _blockersBuilt = 0;
                    foreach (var t in _blockerTemps) if (EntityManager.Exists(t) && !EntityManager.HasComponent<Temp>(t) && !EntityManager.HasComponent<Deleted>(t)) _blockersBuilt++;
                    _registry.MarkDirty();
                    Mod.Log.Info($"Placemaker: {_blockersBuilt} zone block lot(s) keep zoned buildings off group {_groupId}.");
                    GoZoning();
                    return;
                }
                case 4:
                {
                    if (frame - _blockerSince < 2) return;
                    _blockerTemps.Clear();
                    _blockerPhase = 0;
                    return;
                }
            }
        }

        /// <summary>Drops every blocker temporary (Clear) and goes on with the zoning.</summary>
        private void ClearBlockers()
        {
            DestroyOwnDefinitions();
            _tool.Requested = ApplyMode.Clear;
            _blockerTemps.Clear();
            GoZoning();
        }

        private void EmitBlockerDefinition(float2[] ring)
        {
            var def = NewDefinition();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = _blockerPrefab });
            EntityManager.AddComponent<Updated>(def);
            var nodes = EntityManager.AddBuffer<global::Game.Areas.Node>(def);
            nodes.ResizeUninitialized(ring.Length + 1);
            for (int i = 0; i < ring.Length; i++)
                nodes[i] = new global::Game.Areas.Node(new float3(ring[i].x, GroundHeight(ring[i]), ring[i].y), float.MinValue);
            nodes[ring.Length] = nodes[0];
            NoteUsed(_blockerPrefab);
        }

        /// <summary>Our blocker temporaries (while our tool is active every temporary is ours).</summary>
        private void CollectBlockerTemps()
        {
            _blockerTemps.Clear();
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                if (!EntityManager.HasComponent<global::Game.Areas.Area>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                if (!EntityManager.HasComponent<global::Game.Prefabs.PrefabRef>(e)) continue;
                if (EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab != _blockerPrefab) continue;
                _blockerTemps.Add(e);
            }
        }

        /// <summary>Lots (grown 1 m) of live buildings the game marked for removal by our temporaries.</summary>
        private List<float2[]> WarnedBuildingLots()
        {
            var polys = new List<float2[]>();
            if (_warnedBuildingQuery == null || _warnedBuildingQuery.IsEmptyIgnoreFilter) return polys;
            using var warned = _warnedBuildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < warned.Length; i++)
            {
                var e = warned[i];
                if (!BuildingFootprints.IsBuilding(EntityManager, e)) continue;
                BuildingFootprints.Of(EntityManager, e, out var lot, out var footprint);
                if (lot != null) polys.Add(lot);
                if (footprint != null) polys.Add(footprint);
            }
            return ObstacleCut.Grow(polys, 1f);
        }
    }
}
