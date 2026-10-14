// Placemaker — persisted options and rebindable keys (Options → Mods → Placemaker).
//
// Key choices (RESEARCH-NOTES rule 26): Ctrl+P = Find It/Platter, Ctrl+Shift+P = Parking Lot Tool,
// Ctrl+G = Grid Road Generator, M = Move It. Ctrl+Shift+A is unused by the game and those mods.
// All bindings go through CS2's own binding system (SettingsUIKeyboardBinding) so conflicts show
// up in the game's key overview instead of as a silently dead hotkey (PLT learned that the hard way).

using System;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;

namespace Placemaker.Game.Settings
{
    public enum PreviewDetail { Simple = 0, Normal = 1, Detailed = 2 }

    [FileLocation("ModsSettings/Placemaker/Placemaker")]
    [SettingsUIGroupOrder(GroupGeneral, GroupBuild, GroupKeys, GroupMaintenance)]
    [SettingsUIShowGroupName(GroupGeneral, GroupBuild, GroupKeys, GroupMaintenance)]
    [SettingsUIKeyboardAction(ActionToggleTool, ActionType.Button)]
    [SettingsUIKeyboardAction(ActionPrimary, ActionType.Button)]
    [SettingsUIKeyboardAction(ActionUndo, ActionType.Button)]
    [SettingsUIKeyboardAction(ActionRedo, ActionType.Button)]
    [SettingsUIKeyboardAction(ActionRedoAlt, ActionType.Button)]
    public sealed class PlacemakerSettings : ModSetting
    {
        public const string SectionMain = "Main";
        public const string GroupGeneral = "General";
        public const string GroupKeys = "Keys";
        public const string GroupMaintenance = "Maintenance";
        public const string GroupBuild = "Build";

        public const string ActionToggleTool = "ToggleTool";
        public const string ActionPrimary = "Primary";
        public const string ActionUndo = "Undo";
        public const string ActionRedo = "Redo";
        public const string ActionRedoAlt = "RedoAlt";

        /// <summary>Raised by the maintenance buttons; the tool/registration layer acts on them (keeps this class free of system references).</summary>
        public static event Action RemoveEverythingRequested;
        public static event Action ResetDefaultsRequested;

        public PlacemakerSettings(IMod mod) : base(mod) { SetDefaults(); }

        // ───────────── General ─────────────

        [SettingsUISection(SectionMain, GroupGeneral)]
        public bool ShowCursorTooltips { get; set; }

        [SettingsUISection(SectionMain, GroupGeneral)]
        public PreviewDetail PreviewDetail { get; set; }

        /// <summary>Sketch preview: planned surfaces get a faint tint (on) or only their outline (off).</summary>
        [SettingsUISection(SectionMain, GroupGeneral)]
        public bool SketchSurfaceFill { get; set; }

        [SettingsUISection(SectionMain, GroupGeneral)]
        public bool SnapToRoadsWhenDrawing { get; set; }

        /// <summary>Drawn/painted areas are trimmed to the edge of existing roads before planning (AreaClip).</summary>
        [SettingsUISection(SectionMain, GroupGeneral)]
        public bool KeepOffExistingRoads { get; set; }

        /// <summary>Block paint also keeps off visible zone cells nobody zoned yet (zoned cells are always avoided).</summary>
        [SettingsUISection(SectionMain, GroupGeneral)]
        public bool BlockPaintAvoidEmptyZones { get; set; }

        /// <summary>
        /// (FEEDBACK-4) Layout size in percent: scales layout distances (path loops, spacing, plaza and feature
        /// footprints, margins); props and real-dimension layouts keep their size. → SiteContext.LayoutScale.
        /// </summary>
        [SettingsUISlider(min = 25, max = 150, step = 5, scalarMultiplier = 1, unit = global::Game.UI.Unit.kPercentage)]
        [SettingsUISection(SectionMain, GroupGeneral)]
        public int LayoutSize { get; set; }

        /// <summary>(FEEDBACK-4) The one edge margin (m at 100 % layout size) between the outline and the layout. → SiteContext.EdgeMargin.</summary>
        [SettingsUISlider(min = 0f, max = 10f, step = 0.5f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 1)]
        [SettingsUISection(SectionMain, GroupGeneral)]
        public float EdgeMargin { get; set; }

        // ───────────── Keys ─────────────

        [SettingsUIKeyboardBinding(BindingKeyboard.A, ActionToggleTool, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeys)]
        public ProxyBinding ToggleToolBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Enter, ActionPrimary)]
        [SettingsUISection(SectionMain, GroupKeys)]
        public ProxyBinding PrimaryBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Z, ActionUndo, ctrl: true)]
        [SettingsUISection(SectionMain, GroupKeys)]
        public ProxyBinding UndoBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Y, ActionRedo, ctrl: true)]
        [SettingsUISection(SectionMain, GroupKeys)]
        public ProxyBinding RedoBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Z, ActionRedoAlt, ctrl: true, shift: true)]
        [SettingsUISection(SectionMain, GroupKeys)]
        public ProxyBinding RedoAltBinding { get; set; }

        [SettingsUISection(SectionMain, GroupKeys)]
        [SettingsUIButton]
        public bool ResetKeys
        {
            set
            {
                try { ResetKeyBindings(); }
                catch (Exception e) { Mod.Log.Warn("Placemaker: resetting key bindings failed: " + e.Message); }
            }
        }

        // ───────────── Maintenance ─────────────

        [SettingsUISection(SectionMain, GroupMaintenance)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool RemoveEverything
        {
            set
            {
                try { RemoveEverythingRequested?.Invoke(); }
                catch (Exception e) { Mod.Log.Error(e, "Placemaker: remove everything failed"); }
            }
        }

        [SettingsUISection(SectionMain, GroupMaintenance)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool ResetDefaults
        {
            set
            {
                try
                {
                    SetDefaults();
                    ResetDefaultsRequested?.Invoke();
                    ApplyAndSave();
                }
                catch (Exception e) { Mod.Log.Error(e, "Placemaker: reset defaults failed"); }
            }
        }

        // ───────────── Building (forwarded to the build workstream's systems) ─────────────

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool ClearExistingTreesAndProps { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool RecoverRejectedPieces { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool AutoExcludeCollisions { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool OverlayPavedSurfaces { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool ChargeNetConstruction { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool SelectAsOneThing { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool ConfirmBulldoze { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool SurfaceClones { get; set; }

        /// <summary>Widens AreaBatchSystem queries so ghost surfaces render textured (reflection hack; off by default).</summary>
        [SettingsUISection(SectionMain, GroupBuild)]
        public bool TexturedPreviewSurfaces { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool ParkingEconomy { get; set; }

        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(SectionMain, GroupBuild)]
        [SettingsUIDisableByCondition(typeof(PlacemakerSettings), nameof(ParkingEconomyOff))]
        public int DefaultParkingFee { get; set; }

        [SettingsUISection(SectionMain, GroupBuild)]
        public bool ExperimentalParkingCompanion { get; set; }

        [SettingsUISlider(min = 0.5f, max = 3f, step = 0.1f, scalarMultiplier = 1)]
        [SettingsUICustomFormat(fractionDigits = 1)]
        [SettingsUISection(SectionMain, GroupBuild)]
        [SettingsUIDisableByCondition(typeof(PlacemakerSettings), nameof(CompanionOff))]
        public float CompanionUpkeepFactor { get; set; }

        public bool ParkingEconomyOff() => !ParkingEconomy;
        public bool CompanionOff() => !ExperimentalParkingCompanion;

        public override void Apply()
        {
            base.Apply();
            PushToServices();
        }

        /// <summary>The build/economy systems read static switches (docs/proposals/gamebuild.md §6).</summary>
        public void PushToServices()
        {
            try
            {
                global::Placemaker.Game.Build.BuildService.AutoExcludeCollisions = AutoExcludeCollisions;
                global::Placemaker.Game.Build.BuildService.ClearExistingObjects = ClearExistingTreesAndProps;
                global::Placemaker.Game.Build.BuildService.RecoverRejectedPieces = RecoverRejectedPieces;
                global::Placemaker.Game.Build.BuildService.OverlayPavedSurfaces = OverlayPavedSurfaces;
                global::Placemaker.Game.Build.BuildService.ChargeNetConstruction = ChargeNetConstruction;
                global::Placemaker.Game.Groups.OwnerAreaPrefabSystem.FeatureEnabled = SelectAsOneThing;
                global::Placemaker.Game.Groups.Patches.RaycastLiftPatch.Enabled = SelectAsOneThing;
                global::Placemaker.Game.Groups.Patches.BulldozeConfirmationPatch.Enabled = ConfirmBulldoze;
                global::Placemaker.Game.Build.SurfaceCloneSystem.FeatureEnabled = SurfaceClones;
                global::Placemaker.Game.Build.GhostSurfaceSystem.FeatureEnabled = TexturedPreviewSurfaces;
                global::Placemaker.Game.Economy.ParkingEconomySystem.FeatureEnabled = ParkingEconomy;
                global::Placemaker.Game.Economy.ParkingEconomySystem.DefaultFee = DefaultParkingFee;
                global::Placemaker.Game.Economy.ParkingCompanionSystem.CompanionEnabled = ExperimentalParkingCompanion;
                global::Placemaker.Game.Economy.ParkingCompanionUpkeepSystem.UpkeepFactor = CompanionUpkeepFactor;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: build options not applied: " + e.Message); }
        }

        // ───────────── Persisted tool state (not shown) ─────────────

        /// <summary>CS2 resets a tool's selectedSnap to "all" whenever it is created; we restore the player's choice.</summary>
        [SettingsUIHidden] public int SnapMask { get; set; }
        [SettingsUIHidden] public bool SnapMaskSaved { get; set; }
        [SettingsUIHidden] public float BrushSize { get; set; }
        [SettingsUIHidden] public bool BrushSquare { get; set; }
        [SettingsUIHidden] public int LastAreaMode { get; set; }
        [SettingsUIHidden] public string LastRecipe { get; set; }
        [SettingsUIHidden] public bool PanelCollapsed { get; set; }

        public override void SetDefaults()
        {
            ShowCursorTooltips = true;
            PreviewDetail = PreviewDetail.Normal;
            SketchSurfaceFill = true;
            SnapToRoadsWhenDrawing = true;
            KeepOffExistingRoads = true;
            BlockPaintAvoidEmptyZones = false;
            LayoutSize = (int)System.Math.Round(global::Placemaker.Core.SiteContext.DefaultLayoutScale * 100f);
            EdgeMargin = global::Placemaker.Core.SiteContext.DefaultEdgeMargin;
            BrushSize = 150f;
            BrushSquare = false;
            LastAreaMode = 0;
            LastRecipe = "";
            PanelCollapsed = false;
            AutoExcludeCollisions = true;
            ClearExistingTreesAndProps = true;
            RecoverRejectedPieces = true;
            OverlayPavedSurfaces = true;
            ChargeNetConstruction = true;
            SelectAsOneThing = true;
            ConfirmBulldoze = true;
            SurfaceClones = true;
            TexturedPreviewSurfaces = false;
            ParkingEconomy = true;
            DefaultParkingFee = 0;
            ExperimentalParkingCompanion = false;
            CompanionUpkeepFactor = 1f;
            // Snap mask and bindings are intentionally not reset here: the snap mask keeps the
            // player's last choice, bindings have their own reset button.
        }
    }
}
