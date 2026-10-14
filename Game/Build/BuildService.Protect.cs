// Placemaker — last line of defence for existing buildings (s15).
//
// The plan already keeps off building lots (Core/Build/SiteSafety, from SiteCapture's obstacles). But the
// capture is a snapshot: a building may have grown since, or sit outside the captured margin. The game
// tells us exactly which existing building our temporaries would remove: Net/Areas/Objects
// ValidationHelpers give an overlapped spawnable building (Overridable | DeleteOverridden) a
// Game.Tools.Warning, and Apply deletes it. So before Apply, any live building carrying a Warning while
// our build holds the tool is protected: the work items touching it are left out and the build retries
// (the same exclude-and-retry loop as rejected pieces). If nothing can be identified, the build stops
// rather than demolish anything.

using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private EntityQuery _warnedBuildingQuery;
        private int _protectAttempts;
        private int _buildingsProtected;

        private void InitProtect()
        {
            _warnedBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Warning>(), ComponentType.ReadOnly<global::Game.Objects.Transform>() },
                Any = new[] { ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Buildings.Extension>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void ResetProtect()
        {
            _protectAttempts = 0;
            _buildingsProtected = 0;
        }

        /// <summary>
        /// Called in Validate before anything is applied. 0 = no building at risk; 1 = items were left
        /// out (retry); -1 = buildings at risk but nothing of ours could be matched (stop the build).
        /// </summary>
        private int ProtectBuildings()
        {
            if (_warnedBuildingQuery == null || _warnedBuildingQuery.IsEmptyIgnoreFilter || _request?.Plan == null || _work == null) return 0;
            using var warned = _warnedBuildingQuery.ToEntityArray(Allocator.Temp);
            var polys = new List<float2[]>();
            float3? at = null;
            for (int i = 0; i < warned.Length; i++)
            {
                var e = warned[i];
                if (!BuildingFootprints.IsBuilding(EntityManager, e)) continue;
                BuildingFootprints.Of(EntityManager, e, out var lot, out var footprint);
                if (lot != null) polys.Add(lot);
                if (footprint != null) polys.Add(footprint);
                if (!at.HasValue) at = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
            }
            if (polys.Count == 0) return 0;
            Dictionary<string, AssetInfo> assets = null;
            try { assets = Services.Assets?.InfoMap(); } catch (System.Exception) { }
            var hits = SiteSafety.ItemsTouching(_request.Plan, _work, ObstacleCut.Grow(polys, 1f), assets, 1f);
            int newly = 0;
            foreach (var w in hits) newly += Exclude(w);
            if (newly > 0 && _protectAttempts++ < MaxAutoExcludeAttempts)
            {
                _buildingsProtected += warned.Length;
                Mod.Log.Info($"Placemaker: {warned.Length} existing building(s) would have been removed by this build; left out {newly} part(s) touching them and retrying.");
                return 1;
            }
            Mod.Log.Warn($"Placemaker: {warned.Length} existing building(s) are marked for removal by this build and the parts responsible could not be left out; the build stops.");
            NoteSkip("Placemaker.Build.WouldDemolish", at, IssueSeverity.Error, warned.Length);
            return -1;
        }
    }
}
