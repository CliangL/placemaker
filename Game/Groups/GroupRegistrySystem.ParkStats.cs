// Placemaker — keeps the park attractiveness of loaded builds in line with their receipts (GPL-3.0).
// The value lives on the owner lot as a vanilla AttractivenessProvider (BuildService.ParkStats.cs) and is saved
// with it; after a load we only repair: re-add it when a receipt says it was applied but the lot lost it, and
// remove it when the player switched the setting off.

using System;
using Game.Buildings;
using Placemaker.Core.Build;
using Placemaker.Game.Build;
using Unity.Entities;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem
    {
        private void SyncParkStats()
        {
            int added = 0, removed = 0;
            foreach (var kv in _records)
            {
                try
                {
                    var g = EntityManager.GetComponentData<PlacemakerGroup>(kv.Value);
                    var owner = g.Owner;
                    if (owner == Entity.Null || !EntityManager.Exists(owner)) continue;
                    var rec = GetRecord(kv.Key);
                    double value = 0;
                    bool has = rec?.ParkStats != null && rec.ParkStats.Applied.TryGetValue(ParkStatIds.InGameAttractiveness, out value) && value > 0;
                    bool present = EntityManager.HasComponent<AttractivenessProvider>(owner);
                    if (!BuildService.ApplyParkStats && present && has)
                    {
                        EntityManager.RemoveComponent<AttractivenessProvider>(owner);
                        removed++;
                    }
                    else if (BuildService.ApplyParkStats && has && !present && BuildService.ApplyAttractiveness(EntityManager, owner, value, 0) > 0)
                        added++;
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Placemaker: park stats of group {kv.Key} not synced: {e.Message}");
                }
            }
            if (added + removed > 0) Mod.Log.Info($"Placemaker park attractiveness after load: {added} restored, {removed} removed (setting off).");
        }
    }
}
