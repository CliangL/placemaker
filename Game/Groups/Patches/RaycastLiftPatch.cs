// Placemaker — lift raycast hits on any member of a build to its owner lot (GPL-3.0).
// Uses Harmony 2.2.2 (MIT, Andreas Pardeike) as a library; see Library/Harmony/LICENSE.
// Adapted from ParkingLotTool's ParkingLotRaycastPatch (KruemelGames, GPL-3.0).
//
// Why a postfix on ToolRaycastSystem.GetRaycastResult: every ToolBaseSystem reads the returned owner
// immediately for hover, selection and bulldozing. A later system could only redirect the info panel.
//
// Guards (all from PLT):
//  * RaycastFlags.SubElements: in that mode vanilla deliberately returns intermediate owners; a lift
//    would rob net/editor tools of the sub-elements they ask for.
//  * Only queries that can select whole things (StaticObjects or Areas in the type mask: default
//    tool, bulldozer). Net tools (upgrades, road building) keep their road hits.
//  * Transport stops stay themselves (line tool asks StaticObjects without SubElements).
//  * Only the query result changes; no world component is touched.

using System;
using Game.Common;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace Placemaker.Game.Groups.Patches
{
    [HarmonyPatch(typeof(ToolRaycastSystem), nameof(ToolRaycastSystem.GetRaycastResult))]
    internal static class RaycastLiftPatch
    {
        /// <summary>Setting: lift clicks on parts to the whole build (default on).</summary>
        public static bool Enabled = true;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(ToolRaycastSystem), nameof(ToolRaycastSystem.GetRaycastResult)) != null) return true;
            Mod.Log.Warn("Placemaker: ToolRaycastSystem.GetRaycastResult no longer exists; builds are selectable per part only.");
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(ToolRaycastSystem __instance, bool __result, ref RaycastResult result)
        {
            try
            {
                if (!Enabled || !__result || result.m_Owner == Entity.Null) return;
                if ((__instance.raycastFlags & RaycastFlags.SubElements) != 0) return;
                if ((__instance.typeMask & (TypeMask.StaticObjects | TypeMask.Areas)) == 0) return;
                var registry = GroupRegistrySystem.Instance;
                if (registry == null || registry.World != __instance.World) return;
                var em = __instance.World.EntityManager;
                var part = result.m_Owner;
                if (!em.Exists(part) || em.HasComponent<Temp>(part)) return;
                if (em.HasComponent<global::Game.Routes.TransportStop>(part)) return;
                if (!registry.TryLiftToOwner(part, out var owner)) return;
                result.m_Owner = owner;
            }
            catch (Exception e)
            {
                // Never break the player's tools: give up lifting for this session.
                Enabled = false;
                Mod.Log.Error(e, "Placemaker raycast lift failed; disabled for this session");
            }
        }
    }
}
