// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/ParkingGeometry.Types.cs
// (BayKind, BayRole, Zufahrtsart, ParkingLayout), reduced to what the parking layer needs and
// translated. All coordinates are in the local row frame (x along the rows) unless noted.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal enum BayKind { Perimeter = 0, Inner = 1, InnerPerimeter = 2 }

    internal enum BayRole { Normal = 0, Disabled = 1, Electric = 2 }

    /// <summary>Entrance kinds (marker kinds "entrance", "entry", "exit").</summary>
    internal enum EntranceKind { TwoWay = 0, Entry = 1, Exit = 2 }

    internal enum LineKind { Ring = 0, Aisle = 1, Cross = 2, Connector = 3, Footpath = 4 }

    internal sealed class Bay
    {
        /// <summary>[0],[1] front edge (aisle side), [2],[3] back edge — PLT's quad convention.</summary>
        public double2[] Quad;
        public double2 Center;
        /// <summary>Unit vector from the aisle toward the back of the stall (driving-in direction).</summary>
        public double2 Axis;
        /// <summary>Unit vector along the row (direction of increasing Order).</summary>
        public double2 RowDir;
        public double Width, Length;
        public BayKind Kind;
        public BayRole Role;
        public int Row;
        public int Order;
        /// <summary>Drive line serving this stall.</summary>
        public int Line;
        public bool Removed;
    }

    internal sealed class DriveLine
    {
        public int Id;
        public double2 A, B;
        public LineKind Kind;
        public double Width;
        /// <summary>One-way in direction A→B.</summary>
        public bool OneWay;
        /// <summary>Index into Layout.Entrances for connectors/footpaths, else -1.</summary>
        public int Entrance = -1;
        public bool Removed;
        public double Length => PGeo.Len(B - A);
    }

    internal sealed class EntrancePlan
    {
        public EntranceKind Kind;
        /// <summary>Point on the lot outline.</summary>
        public double2 Point;
        /// <summary>Unit inward normal of the outline edge at Point.</summary>
        public double2 Inward;
        public int Edge;
        public double Along;
        /// <summary>Where the connector meets the lot's drive network.</summary>
        public double2 Junction;
        public int ConnectorLine = -1;
        public double Width;
        public bool Connected;
    }

    internal sealed class Charger
    {
        public double2 Position;
        /// <summary>Facing (unit), same as the paired stalls' axis reversed (toward the aisle).</summary>
        public double2 Facing;
        public int BayA, BayB;
    }

    internal sealed class Lantern
    {
        public double2 Position;
        public double2 Facing;
        public bool Double;
    }

    internal sealed class Plant
    {
        public double2 Position;
        public bool Tree;
        public int Species;
        public uint Seed;
    }

    internal sealed class SurfacePoly
    {
        public List<double2> Ring;
        public bool Asphalt;
        /// <summary>The convex cells merged into Ring — the fallback when CS2 would reject Ring.</summary>
        public List<List<double2>> Cells;
    }

    internal struct LayoutIssue
    {
        public IssueSeverity Severity;
        public string Key;
        public object[] Args;
        public double2? At;
        public LayoutIssue(IssueSeverity s, string key, double2? at = null, params object[] args)
        { Severity = s; Key = key; At = at; Args = args ?? System.Array.Empty<object>(); }
    }

    /// <summary>Everything the layout engine produced, in the local frame.</summary>
    internal sealed class Layout
    {
        public Frame Frame;
        public List<double2> Lot = new List<double2>();
        public List<Bay> Bays = new List<Bay>();
        public List<DriveLine> Lines = new List<DriveLine>();
        public List<EntrancePlan> Entrances = new List<EntrancePlan>();
        public List<Charger> Chargers = new List<Charger>();
        /// <summary>Bay index pairs (into Bays) sharing one charger.</summary>
        public List<int2> ElectricPairs = new List<int2>();
        public List<Lantern> Lanterns = new List<Lantern>();
        public List<Plant> Plants = new List<Plant>();
        public List<SurfacePoly> Surfaces = new List<SurfacePoly>();
        public List<LayoutIssue> Issues = new List<LayoutIssue>();
        public bool PerimeterRoad;
        public double RowAngleDeg;
        public double LotArea;
        public double AisleLength;
        /// <summary>Lot area not covered by an accepted surface (m²), set when the plan is emitted.</summary>
        public double UncoveredArea;

        public IEnumerable<Bay> LiveBays { get { foreach (var b in Bays) if (!b.Removed) yield return b; } }
        public IEnumerable<DriveLine> LiveLines { get { foreach (var l in Lines) if (!l.Removed) yield return l; } }

        public int StallCount
        {
            get { int n = 0; foreach (var b in Bays) if (!b.Removed) n++; return n; }
        }

        public int CountRole(BayRole r)
        {
            int n = 0; foreach (var b in Bays) if (!b.Removed && b.Role == r) n++; return n;
        }
    }

    /// <summary>Row frame: local x runs along the parking rows.</summary>
    internal struct Frame
    {
        public double2 Origin, X, Y;
        public Frame(double2 origin, double2 xAxis) { Origin = origin; X = PGeo.Norm(xAxis); Y = PGeo.Perp(X); }
        public double2 ToLocal(double2 w) { var d = w - Origin; return new double2(PGeo.Dot(d, X), PGeo.Dot(d, Y)); }
        public double2 ToWorld(double2 l) => Origin + X * l.x + Y * l.y;
        public double2 DirToWorld(double2 d) => X * d.x + Y * d.y;
        public double2 DirToLocal(double2 d) => new double2(PGeo.Dot(d, X), PGeo.Dot(d, Y));
    }
}
