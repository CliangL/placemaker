# Harmony dependency

Unmodified binary dependency `0Harmony.dll`, referenced by `Placemaker.csproj` (Private=true, shipped
next to `Placemaker.dll`; the MIT licence text is deployed as `0Harmony-LICENSE.txt`).

- Package: `Lib.Harmony` 2.2.2 (assembly `0Harmony` 2.2.2.0, target .NET Framework 4.8)
- Copyright (c) 2017 Andreas Pardeike — MIT, full text in `LICENSE`
- Source: https://github.com/pardeike/Harmony — https://www.nuget.org/packages/Lib.Harmony/2.2.2
- SHA-256: `0DE0118C8F1D4408DE389CA33B46D2FF7778F3A8541B430CAE729EC913D899C7`
  (identical to the copy ParkingLotTool ships, so both mods load the same Harmony version)

Used only by `Game/Groups/Patches/*` (raycast lift to the owner area, bulldoze confirmation).
Every patch class has a `[HarmonyPrepare]` guard, and installation is wrapped so a Harmony
problem disables the patches, never the mod.
