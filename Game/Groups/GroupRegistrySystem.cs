// Placemaker — registry of built groups (GPL-3.0).
// Ownership approach credited to ParkManager (tomkpunkt, GPL-3.0: headless build record +
// ParkPathMember tags) with ParkingLotTool's lessons (KruemelGames, GPL-3.0: carrier SubNet is
// lost on load, orphans after saving without the mod) built in.
//
// Phase: ModificationEnd. Deleted members of this frame are still alive there (CleanUp runs at the
// end of the frame), so dangling carrier SubNet entries are removed before anything reads them
// next frame, and manual bulldozing is noticed the same frame. Runs while paused.

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem : GameSystemBase, IGroupRegistry
    {
        private EntityQuery _recordQuery;
        private EntityQuery _memberQuery;
        private EntityQuery _changedMemberQuery;
        private EntityQuery _changedRecordQuery;
        private EntityQuery _deletedMemberQuery;

        private readonly Dictionary<int, Entity> _records = new Dictionary<int, Entity>();
        private readonly Dictionary<int, GroupRecord> _decoded = new Dictionary<int, GroupRecord>();
        private readonly Dictionary<int, List<Entity>> _members = new Dictionary<int, List<Entity>>();
        private readonly Dictionary<int, List<string>> _missing = new Dictionary<int, List<string>>();
        private readonly List<int> _allIds = new List<int>();
        private bool _dirty = true;
        private bool _postLoadPending;
        private int _missingCheckedCatalogVersion = -1;
        private int _version;

        /// <summary>Bumped whenever groups, names or member counts change (UI refresh hint).</summary>
        public int Version => _version;

        public event Action Changed;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _recordQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerGroup>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _memberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _changedMemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            _deletedMemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            _changedRecordQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerGroup>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>() },
            });
            Services.Groups = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (ReferenceEquals(Services.Groups, this)) Services.Groups = null;
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ClearCaches();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            ClearCaches();
            _postLoadPending = mode.IsGame();
        }

        private void ClearCaches()
        {
            _records.Clear(); _decoded.Clear(); _members.Clear(); _missing.Clear(); _allIds.Clear();
            _dirty = true;
            _missingCheckedCatalogVersion = -1;
            Bump();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                if (_postLoadPending)
                {
                    _postLoadPending = false;
                    PostLoadRepair();
                }
                if (!_changedMemberQuery.IsEmptyIgnoreFilter || !_changedRecordQuery.IsEmptyIgnoreFilter)
                {
                    if (!_deletedMemberQuery.IsEmptyIgnoreFilter) HandleDeletedMembers();
                    _dirty = true;
                    Bump();
                }
                CheckMissingAssets();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker group registry update failed");
            }
        }

        // ───────────────────────── IGroupRegistry ─────────────────────────

        public int GroupOf(Entity e)
        {
            if (e == Entity.Null || !EntityManager.Exists(e) || !EntityManager.HasComponent<PlacemakerMember>(e)) return 0;
            int id = EntityManager.GetComponentData<PlacemakerMember>(e).GroupId;
            EnsureIndex();
            return _records.ContainsKey(id) ? id : 0;
        }

        public GroupInfo Get(int groupId)
        {
            EnsureIndex();
            if (!_records.ContainsKey(groupId)) return null;
            var rec = GetRecord(groupId);
            var info = new GroupInfo { Id = groupId };
            if (rec != null)
            {
                info.RecipeId = rec.RecipeId ?? "";
                info.RecipeNameKey = rec.RecipeNameKey ?? "";
                info.CustomName = rec.CustomName ?? "";
                info.Stats = new List<PlanStat>(rec.Stats);
                info.Upkeep = rec.Upkeep;
            }
            info.MemberCount = CountMembers(groupId, false);
            return info;
        }

        public GroupReceipt GetReceipt(int groupId)
        {
            var rec = GetRecord(groupId);
            if (rec == null || rec.Recipe == null || rec.Area == null) return null;
            var copy = rec.Clone();
            return new GroupReceipt { FormatVersion = ReceiptCodec.CurrentVersion, Recipe = copy.Recipe, Area = copy.Area };
        }

        public IReadOnlyCollection<int> AllGroups
        {
            get { EnsureIndex(); return _allIds; }
        }

        public void GetMembers(int groupId, List<Entity> into)
        {
            if (into == null) return;
            EnsureIndex();
            if (!_members.TryGetValue(groupId, out var list)) return;
            foreach (var e in list)
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e)) into.Add(e);
        }

        public bool RequestRemove(int groupId)
        {
            EnsureIndex();
            if (!_records.TryGetValue(groupId, out var record) || !EntityManager.Exists(record)) return false;
            if (Services.Build is Build.BuildService b && b.IsBuildingGroup(groupId)) return false;
            if (!EntityManager.HasComponent<PlacemakerRemoval>(record))
                EntityManager.AddComponentData(record, new PlacemakerRemoval());
            Mod.Log.Info($"Placemaker: removal of group {groupId} requested ({CountMembers(groupId, true)} members).");
            Bump();
            return true;
        }

        public void Rename(int groupId, string name)
        {
            var rec = GetRecord(groupId);
            if (rec == null || !_records.TryGetValue(groupId, out var entity)) return;
            name = (name ?? "").Trim();
            if (name.Length > 120) name = name.Substring(0, 120);
            rec.CustomName = name;
            WriteRecord(entity, rec);
        }

        // ───────────────────────── internal API (build/removal/economy) ─────────────────────────

        public bool IsRemoving(int groupId) =>
            _records.TryGetValue(groupId, out var e) && EntityManager.Exists(e) && EntityManager.HasComponent<PlacemakerRemoval>(e);

        /// <summary>Prefab names of this group that are not loaded (empty = complete).</summary>
        public IReadOnlyList<string> MissingAssets(int groupId) =>
            _missing.TryGetValue(groupId, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>();

        public bool HasMissingAssets(int groupId) => _missing.TryGetValue(groupId, out var l) && l.Count > 0;

        public int AllocateId()
        {
            EnsureIndex();
            int max = 0;
            foreach (var id in _records.Keys) if (id > max) max = id;
            // Also scan member tags: a record lost to a "saved without the mod" round trip must not
            // have its id reused while stale tags still carry it.
            using (var members = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < members.Length; i++) if (members[i].GroupId > max) max = members[i].GroupId;
            return max + 1;
        }

        public Entity CreateRecord(GroupRecord rec, bool underConstruction)
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new PlacemakerGroup { GroupId = rec.GroupId, Flags = (int)rec.Flags });
            EntityManager.AddBuffer<PlacemakerGroupBlob>(e);
            if (underConstruction) EntityManager.AddComponent<PlacemakerUnderConstruction>(e);
            EntityManager.AddComponent<Created>(e);
            WriteRecord(e, rec);
            _records[rec.GroupId] = e;
            if (!_allIds.Contains(rec.GroupId)) _allIds.Add(rec.GroupId);
            return e;
        }

        public void WriteRecord(Entity e, GroupRecord rec)
        {
            if (!EntityManager.Exists(e) || !EntityManager.HasComponent<PlacemakerGroup>(e)) return;
            var bytes = ReceiptCodec.Encode(rec);
            var buffer = EntityManager.GetBuffer<PlacemakerGroupBlob>(e);
            buffer.ResizeUninitialized(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) buffer[i] = new PlacemakerGroupBlob { Value = bytes[i] };
            var g = EntityManager.GetComponentData<PlacemakerGroup>(e);
            g.Flags = (int)rec.Flags;
            EntityManager.SetComponentData(e, g);
            _decoded[rec.GroupId] = rec;
            Bump();
        }

        public bool TryGetRecordEntity(int groupId, out Entity e)
        {
            EnsureIndex();
            return _records.TryGetValue(groupId, out e) && EntityManager.Exists(e);
        }

        /// <summary>Decoded record (cached; callers that modify it must call WriteRecord).</summary>
        public GroupRecord GetRecord(int groupId)
        {
            EnsureIndex();
            if (_decoded.TryGetValue(groupId, out var rec)) return rec;
            if (!_records.TryGetValue(groupId, out var e) || !EntityManager.HasBuffer<PlacemakerGroupBlob>(e)) return null;
            var buffer = EntityManager.GetBuffer<PlacemakerGroupBlob>(e, true);
            var bytes = new byte[buffer.Length];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = buffer[i].Value;
            rec = ReceiptCodec.Decode(bytes);
            if (rec == null)
            {
                // Unreadable blob: keep the group manageable (remove still works) with a neutral name.
                Mod.Log.Warn($"Placemaker: receipt of group {groupId} is unreadable; the group can be removed but not edited.");
                rec = new GroupRecord { GroupId = groupId, RecipeNameKey = "Placemaker.Group.Unknown" };
            }
            rec.GroupId = groupId;
            _decoded[groupId] = rec;
            return rec;
        }

        public int CountMembers(int groupId, bool includeAuxiliary)
        {
            EnsureIndex();
            if (!_members.TryGetValue(groupId, out var list)) return 0;
            int n = 0;
            foreach (var e in list)
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                if (!includeAuxiliary && EntityManager.GetComponentData<PlacemakerMember>(e).Kind == MemberKind.Auxiliary) continue;
                n++;
            }
            return n;
        }

        public void MarkDirty() { _dirty = true; Bump(); }

        private void Bump()
        {
            _version++;
            try { Changed?.Invoke(); } catch (Exception e) { Mod.Log.Warn("Placemaker group listener failed: " + e.Message); }
        }

        // ───────────────────────── index ─────────────────────────

        private void EnsureIndex()
        {
            if (!_dirty) return;
            _dirty = false;
            _records.Clear();
            _members.Clear();
            _allIds.Clear();
            using (var records = _recordQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < records.Length; i++)
                {
                    var g = EntityManager.GetComponentData<PlacemakerGroup>(records[i]);
                    if (g.GroupId <= 0) continue;
                    if (_records.ContainsKey(g.GroupId))
                    {
                        Mod.Log.Warn($"Placemaker: duplicate group id {g.GroupId}; keeping the first record.");
                        continue;
                    }
                    _records[g.GroupId] = records[i];
                    _allIds.Add(g.GroupId);
                }
            _allIds.Sort();
            foreach (var id in new List<int>(_decoded.Keys)) if (!_records.ContainsKey(id)) _decoded.Remove(id);
            using (var entities = _memberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!_members.TryGetValue(tags[i].GroupId, out var list)) _members[tags[i].GroupId] = list = new List<Entity>();
                    list.Add(entities[i]);
                }
        }

        // ───────────────────────── maintenance ─────────────────────────

        /// <summary>
        /// Members deleted this frame by anyone (player bulldozer, other tools, our own removal).
        /// Carrier SubNet entries of deleted parking aisles are removed now: the carrier has no
        /// Owner-based bookkeeping, and a dangling SubNet entry is read by lane connection jobs.
        /// </summary>
        private void HandleDeletedMembers()
        {
            using var deleted = _deletedMemberQuery.ToEntityArray(Allocator.Temp);
            var touchedGroups = new HashSet<int>();
            for (int i = 0; i < deleted.Length; i++)
            {
                var e = deleted[i];
                var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                touchedGroups.Add(tag.GroupId);
                if (tag.Kind != MemberKind.Edge) continue;
                if (!_records.TryGetValue(tag.GroupId, out var rec) && !TryGetRecordEntity(tag.GroupId, out rec)) continue;
                if (!EntityManager.Exists(rec)) continue;
                var g = EntityManager.GetComponentData<PlacemakerGroup>(rec);
                if (g.Carrier == Entity.Null || !EntityManager.Exists(g.Carrier) || !EntityManager.HasBuffer<global::Game.Net.SubNet>(g.Carrier)) continue;
                var subNets = EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier);
                for (int k = subNets.Length - 1; k >= 0; k--) if (subNets[k].m_SubNet == e) subNets.RemoveAt(k);
            }
            foreach (var id in touchedGroups)
            {
                if (!TryGetRecordEntity(id, out var rec)) continue;
                if (EntityManager.HasComponent<PlacemakerRemoval>(rec) || EntityManager.HasComponent<PlacemakerUnderConstruction>(rec)) continue;
                var r = GetRecord(id);
                if (r != null && (r.Flags & GroupFlags.Modified) == 0)
                {
                    r.Flags |= GroupFlags.Modified;
                    WriteRecord(rec, r);
                }
            }
            // Empty groups (everything bulldozed by hand) clean up their record and auxiliaries.
            _dirty = true;
            EnsureIndex();
            foreach (var id in touchedGroups)
            {
                if (!_records.TryGetValue(id, out var rec)) continue;
                if (EntityManager.HasComponent<PlacemakerRemoval>(rec) || EntityManager.HasComponent<PlacemakerUnderConstruction>(rec)) continue;
                if (CountLiveNonAuxiliaryExcluding(id) == 0)
                {
                    EntityManager.AddComponentData(rec, new PlacemakerRemoval());
                    Mod.Log.Info($"Placemaker: group {id} lost its last element; removing its record.");
                }
            }
        }

        private int CountLiveNonAuxiliaryExcluding(int groupId)
        {
            if (!_members.TryGetValue(groupId, out var list)) return 0;
            int n = 0;
            foreach (var e in list)
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e)
                    && EntityManager.GetComponentData<PlacemakerMember>(e).Kind != MemberKind.Auxiliary) n++;
            return n;
        }

        /// <summary>
        /// After a load: (1) tags whose record is gone (save edited without the mod, or a record
        /// lost to a crash) are removed so the entities degrade to plain vanilla objects; (2) the
        /// carrier SubNet that CS2 does not persist for owner-less edges is rebuilt (PLT measured
        /// 109 → 0 entries after load); (3) records are decoded once.
        /// </summary>
        private void PostLoadRepair()
        {
            _dirty = true;
            EnsureIndex();
            int orphans = 0;
            var orphanEntities = new List<Entity>();
            foreach (var kv in _members)
                if (!_records.ContainsKey(kv.Key)) orphanEntities.AddRange(kv.Value);
            foreach (var e in orphanEntities)
            {
                if (!EntityManager.Exists(e)) continue;
                EntityManager.RemoveComponent<PlacemakerMember>(e);
                orphans++;
            }
            if (orphans > 0)
                Mod.Log.Warn($"Placemaker: {orphans} built elements reference groups that no longer exist (saved without Placemaker?). They stay as normal city objects.");

            int restored = 0;
            foreach (var kv in _records)
            {
                var rec = kv.Value;
                var g = EntityManager.GetComponentData<PlacemakerGroup>(rec);
                if (g.Carrier != Entity.Null && !EntityManager.Exists(g.Carrier)) { g.Carrier = Entity.Null; EntityManager.SetComponentData(rec, g); }
                if (g.Companion != Entity.Null && !EntityManager.Exists(g.Companion)) { g.Companion = Entity.Null; EntityManager.SetComponentData(rec, g); }
                if (g.Carrier == Entity.Null || !_members.TryGetValue(kv.Key, out var list)) continue;
                if (!EntityManager.HasBuffer<global::Game.Net.SubNet>(g.Carrier)) EntityManager.AddBuffer<global::Game.Net.SubNet>(g.Carrier);
                if (!EntityManager.HasBuffer<global::Game.Objects.SubObject>(g.Carrier)) EntityManager.AddBuffer<global::Game.Objects.SubObject>(g.Carrier);
                var subNets = EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier);
                var present = new HashSet<Entity>();
                for (int i = subNets.Length - 1; i >= 0; i--)
                {
                    var s = subNets[i].m_SubNet;
                    if (!EntityManager.Exists(s) || EntityManager.HasComponent<Deleted>(s)) subNets.RemoveAt(i);
                    else present.Add(s);
                }
                foreach (var m in list)
                {
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(m);
                    if (tag.Role != MemberRole.ParkingAisle || tag.Kind != MemberKind.Edge || present.Contains(m)) continue;
                    subNets = EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier);
                    subNets.Add(new global::Game.Net.SubNet(m));
                    restored++;
                }
            }
            if (restored > 0) Mod.Log.Info($"Placemaker restored {restored} parking aisle links after load.");
            foreach (var id in _records.Keys) GetRecord(id);
            Mod.Log.Info($"Placemaker loaded {_records.Count} groups.");
            Bump();
        }

        private void CheckMissingAssets()
        {
            var catalog = Services.Assets as Assets.AssetCatalogSystem;
            if (catalog == null || !catalog.Ready || catalog.Version == _missingCheckedCatalogVersion) return;
            _missingCheckedCatalogVersion = catalog.Version;
            EnsureIndex();
            _missing.Clear();
            foreach (var kv in _records)
            {
                var rec = GetRecord(kv.Key);
                if (rec == null) continue;
                var missing = new List<string>();
                foreach (var name in rec.PrefabNames)
                    if (!string.IsNullOrEmpty(name) && !catalog.PrefabExists(name)) missing.Add(name);
                bool was = (rec.Flags & GroupFlags.MissingAssets) != 0;
                if (missing.Count > 0) _missing[kv.Key] = missing;
                if (was != (missing.Count > 0))
                {
                    if (missing.Count > 0) rec.Flags |= GroupFlags.MissingAssets; else rec.Flags &= ~GroupFlags.MissingAssets;
                    WriteRecord(kv.Value, rec);
                }
                if (missing.Count > 0)
                    Mod.Log.Warn($"Placemaker group {kv.Key} uses {missing.Count} assets that are not loaded (e.g. '{missing[0]}'); offer repair via rebuild.");
            }
        }
    }
}
