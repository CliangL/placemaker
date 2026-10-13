# Geometry workstream — proposals for lead-owned files

## 1. `AreaShape` should be able to carry holes (Contracts.cs)

Paint mode (`BrushMask.Outlines`) and block picking (`PlanarGraph.PickFace` → `FacePick.Holes`, e.g. a
loop road inside a block) now produce regions with holes. `AreaShape` only has `Ring`, so the game
layer must currently either fill holes or split the area before handing it over.

Proposal (append-only, save-format safe):

```csharp
public sealed class AreaShape
{
    ...
    /// <summary>Clockwise hole rings strictly inside Ring (XZ + terrain height). Empty for most areas.</summary>
    public List<float3[]> Holes = new List<float3[]>();
    public Region ToRegion();   // Core.Geometry.Region (outer CCW + holes CW)
}
```

Generators would plan on the `Region`; every emitted surface still goes through
`SurfaceAcceptance.MakeAcceptable(Region, heightAt)`, which opens holes with seams (CS2 areas cannot have
holes).

## 2. Build: `Stack<T>` / `Queue<T>` are ambiguous in the mod build

Compiling `Placemaker.csproj` against the game's `Managed` folder reports CS0433 for
`System.Collections.Generic.Stack<T>` and `Queue<T>` (defined in both the game's `mscorlib` and the
net48 reference `System.dll`). Core/Geometry uses its own `ListStack<T>`/`ListQueue<T>` (internal,
Core/Geometry/Collections.cs). Either document "don't use Stack/Queue" in ARCHITECTURE.md or fix the
reference set (e.g. reference `System.dll` from `Managed` instead of the reference assemblies, or add an
extern alias).

## 3. Toolchain post-processor needs the .NET 6 runtime

With CSII_TOOLPATH set (Options → Modding installed), `dotnet build Placemaker.csproj ...` compiles, then
`ModPostProcessor.exe` fails: "Framework 'Microsoft.NETCore.App', version '6.0.0' (x64) was not found"
(only 9.x/10.x installed). `-p:UseToolchain=false` builds standalone against the game DLLs (0 warnings).
Installing the .NET 6 runtime (x64) fixes the toolchain path.
