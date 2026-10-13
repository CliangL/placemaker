using Unity.Entities;

namespace Placemaker.Game.Selection
{
    /// <summary>
    /// Entity ↔ the opaque long ids used by Core (BoundaryAttachment.ExternalId, NearbyNet.ExternalId,
    /// road-graph half-edges). Index in the low 32 bits, version in the high 32 bits, so a stale id
    /// (entity deleted and slot reused) never resolves to the new occupant.
    /// </summary>
    public static class EntityIds
    {
        public static long Pack(Entity e) => ((long)(uint)e.Version << 32) | (uint)e.Index;

        public static Entity Unpack(long id) => new Entity { Index = (int)(uint)id, Version = (int)(uint)(id >> 32) };

        /// <summary>Resolves an id back to a live entity (Entity.Null when it no longer exists).</summary>
        public static Entity Resolve(EntityManager em, long id)
        {
            if (id <= 0) return Entity.Null;
            var e = Unpack(id);
            return e.Index > 0 && em.Exists(e) ? e : Entity.Null;
        }
    }
}
