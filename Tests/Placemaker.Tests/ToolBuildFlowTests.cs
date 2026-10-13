using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    /// <summary>
    /// Playtest 1: "I went to build another area and then it failed… then I'm starting to build
    /// again and it's got these red dots". The failure path must land in Review with a message,
    /// allow fix/retry or going back, and the red markers must vanish on any change.
    /// </summary>
    public class ToolBuildFlowTests
    {
        private static StageFacts Ready() => new StageFacts
        {
            AreaValid = true, HasSelection = true, HasRecipe = true, PlanReady = true, CanAfford = true, BuildServiceAvailable = true,
        };

        private static readonly float3[] Errors = { new float3(10, 0, 20), new float3(30, 0, 40) };

        [Fact]
        public void FailedBuildLandsInReviewWithMessageAndRetryEnabled()
        {
            var f = Ready();
            f.BuildRunning = true;
            Assert.Equal(ToolStage.Building, StageMachine.Normalize(ToolStage.Review, f));
            Assert.Equal(PrimaryKind.CancelBuild, StageMachine.Primary(ToolStage.Building, f).Kind);

            // Build service reports Failed: the tool leaves Building …
            f.BuildRunning = false;
            var stage = StageMachine.Normalize(ToolStage.Building, f);
            Assert.Equal(ToolStage.Review, stage);
            // … and records the failure after the stage change.
            var fb = new BuildFeedback();
            fb.StageChanged();
            fb.Failed("Placemaker.Build.InvalidShape", new object[] { 2 }, Errors);
            Assert.True(fb.HasFailure);
            Assert.Equal("Placemaker.Build.InvalidShape", fb.Failure.Key);
            Assert.Equal(IssueSeverity.Error, fb.Failure.Severity);
            Assert.Equal(2, fb.ErrorPositions.Count);
            Assert.Equal(Errors[0], fb.Failure.At.Value);

            // Retry is possible right away; Esc goes back to Design to fix.
            var p = StageMachine.Primary(stage, f);
            Assert.Equal(PrimaryKind.Build, p.Kind);
            Assert.True(p.Enabled);
            Assert.Equal(EscapeAction.ToDesign, StageMachine.Escape(stage, f));
            Assert.Contains(ToolStage.Design, StageMachine.Reachable(f));
            Assert.Contains(ToolStage.Area, StageMachine.Reachable(f));
        }

        [Fact]
        public void FailedBuildWithBrokenAreaFallsBackToArea()
        {
            var f = new StageFacts { HasRecipe = true, HasSelection = true };
            Assert.Equal(ToolStage.Area, StageMachine.Normalize(ToolStage.Building, f));
        }

        [Fact]
        public void RedMarkersVanishOnAreaOrDesignChange()
        {
            var fb = new BuildFeedback();
            fb.Failed("k", null, Errors);
            int v = fb.Version;
            fb.InputChanged();
            Assert.False(fb.HasFailure);
            Assert.Empty(fb.ErrorPositions);
            Assert.True(fb.Version > v);
        }

        [Fact]
        public void RedMarkersVanishOnStageChangeNewBuildAndSuccess()
        {
            var fb = new BuildFeedback();
            fb.Failed("k", null, Errors);
            fb.StageChanged();
            Assert.Empty(fb.ErrorPositions);

            fb.Failed("k", null, Errors);
            fb.Started();
            Assert.Empty(fb.ErrorPositions);
            Assert.False(fb.HasFailure);

            fb.Failed("k", null, Errors);
            fb.Succeeded();
            Assert.Empty(fb.ErrorPositions);
            Assert.False(fb.HasFailure);
        }

        [Fact]
        public void RefusedStartUsesFallbackKeyAndNoPositions()
        {
            var fb = new BuildFeedback();
            fb.Failed(null, null, null, "Placemaker.Issue.BuildRefused");
            Assert.Equal("Placemaker.Issue.BuildRefused", fb.Failure.Key);
            Assert.Empty(fb.ErrorPositions);
            Assert.Null(fb.Failure.At);
            Assert.NotNull(fb.Failure.Args);
        }

        [Fact]
        public void ClearWithoutFailureDoesNotBumpVersion()
        {
            var fb = new BuildFeedback();
            fb.InputChanged();
            Assert.Equal(0, fb.Version);
        }

        [Fact]
        public void FailureShowsInSummaryIssues()
        {
            var fb = new BuildFeedback();
            fb.Failed("Placemaker.Issue.BuildFailed", null, Errors);
            var input = new SummaryInput();
            input.ExtraIssues.Add(fb.Failure);
            var j = JsonValue.Parse(UiModels.Summary(input).ToString());
            var issue = j["issues"].Items[0];
            Assert.Equal("error", issue["severity"].AsString());
            Assert.Equal(3, issue["at"].Items.Count);
        }

        // ───────────── permanent acknowledgement ─────────────

        [Fact]
        public void PermanentEffectsBlockBuildUntilAcknowledged()
        {
            var f = Ready();
            f.PermanentPending = true;
            var p = StageMachine.Primary(ToolStage.Review, f);
            Assert.False(p.Enabled);
            Assert.Equal("Placemaker.Reason.AcknowledgePermanent", p.Reason.Key);
            f.PermanentPending = false;
            Assert.True(StageMachine.Primary(ToolStage.Review, f).Enabled);
        }

        [Fact]
        public void PermanentFingerprintChangesWithEffects()
        {
            var plan = new ComposedPlan();
            var layer = new AreaPlan();
            layer.Terraform.Add(new PlannedTerraform { Ring = ToolTestData.Square(10), Permanent = true, WaterLevel = 5f });
            plan.Layers.Add(layer);
            var a = UiModels.PermanentFingerprint(UiModels.Permanent(plan));
            Assert.NotEqual("", a);
            layer.Terraform[0].Ring = ToolTestData.Square(20);
            Assert.NotEqual(a, UiModels.PermanentFingerprint(UiModels.Permanent(plan)));
            Assert.Equal("", UiModels.PermanentFingerprint(UiModels.Permanent(new ComposedPlan())));

            var j = JsonValue.Parse(UiModels.Summary(new SummaryInput { Plan = plan }).ToString());
            Assert.Single(j["permanent"].Items);
            Assert.False(j["permanentAcknowledged"].AsBool());
            j = JsonValue.Parse(UiModels.Summary(new SummaryInput { Plan = plan, PermanentAcknowledged = true }).ToString());
            Assert.True(j["permanentAcknowledged"].AsBool());
        }
    }

    public class ToolMarkerTests
    {
        private static Recipe RecipeFor(ToolFakeGenerator g)
        {
            var r = new Recipe { Id = "t", Root = new RecipeStep { Layer = g.Id } };
            r.Root.Children.Add(new RecipeStep { Layer = g.Id, Target = "block" });
            r.Root.Children.Add(new RecipeStep { Layer = g.Id, Target = "" });
            return r;
        }

        private static ToolFakeGenerator Gen()
        {
            var g = new ToolFakeGenerator { NeedsEntrance = true };
            g.Suggestions.Add(new EdgeMarker { Kind = "entrance", Position = new float3(5, 0, 0) });
            g.Suggestions.Add(new EdgeMarker { Kind = "bogus", Position = new float3(10, 0, 5) });
            return g;
        }

        private static AreaShape Shape() => new AreaShape { Ring = ToolTestData.Square(10) };

        [Fact]
        public void SuggestionsBecomeRealMarkersOnWholeOutlineSteps()
        {
            var g = Gen();
            var r = RecipeFor(g);
            Assert.True(MarkerAuto.Apply(r, Shape(), null, new HashSet<string>(), false, _ => g));
            Assert.Single(r.Root.Markers);                      // unknown kinds dropped
            Assert.Equal("entrance", r.Root.Markers[0].Kind);
            Assert.Empty(r.Root.Children[0].Markers);           // region-targeted child: its layer handles its own blocks
            Assert.Single(r.Root.Children[1].Markers);          // add-on on the whole area
            Assert.False(MarkerAuto.Apply(r, Shape(), null, new HashSet<string>(), false, _ => g));
        }

        [Fact]
        public void EditedStepsAreNeverRefilled()
        {
            var g = Gen();
            var r = RecipeFor(g);
            var edited = new HashSet<string> { RecipePaths.Root };
            MarkerAuto.Apply(r, Shape(), null, edited, true, _ => g);
            Assert.Empty(r.Root.Markers);   // the player removed them all: stays empty, step unready
            var ready = g.CheckReady(new AreaInput { Markers = r.Root.Markers });
            Assert.False(ready.Ready);
        }

        [Fact]
        public void FillOnlyKeepsExistingAndReplaceRefreshes()
        {
            var g = Gen();
            var r = RecipeFor(g);
            r.Root.Markers.Add(new EdgeMarker { Kind = "entrance", Position = new float3(0, 0, 5) });
            Assert.False(MarkerAuto.Apply(r, Shape(), null, new HashSet<string> { "0/1" }, false, _ => g));
            Assert.Equal(new float3(0, 0, 5), r.Root.Markers[0].Position);
            Assert.True(MarkerAuto.Apply(r, Shape(), null, new HashSet<string> { "0/1" }, true, _ => g));
            Assert.Equal(new float3(5, 0, 0), r.Root.Markers[0].Position);
        }

        [Fact]
        public void NoSuggestionPossibleLeavesStepUnready()
        {
            var g = new ToolFakeGenerator { NeedsEntrance = true };
            var r = new Recipe { Id = "t", Root = new RecipeStep { Layer = g.Id } };
            Assert.False(MarkerAuto.Apply(r, Shape(), null, new HashSet<string>(), true, _ => g));
            var f = new StageFacts
            {
                AreaValid = true, HasRecipe = true, PlanReady = true, UnreadyStepPath = "0", UnreadyMarkerKind = "entrance",
                UnreadyNextStep = new LocText("Placemaker.Step.PlaceEntrance"),
            };
            var p = StageMachine.Primary(ToolStage.Review, f);
            Assert.Equal(PrimaryKind.PlaceMarkers, p.Kind);
        }

        [Fact]
        public void StepViewListsMarkersKindsAndRequirement()
        {
            var g = Gen();
            var r = new Recipe { Id = "t", Root = new RecipeStep { Layer = g.Id } };
            r.Root.Markers.Add(new EdgeMarker { Kind = "entrance", Position = new float3(1, 2, 3) });
            var j = JsonValue.Parse(UiModels.Design(r, "t", false, "0", null, null, _ => g).ToString())["root"];
            var m = j["markerList"].Items[0];
            Assert.Equal("entrance", m["kind"].AsString());
            Assert.Equal(0, m["index"].AsNumber());
            Assert.Equal(3, m["position"].Items[2].AsNumber());
            var k = j["markerKinds"].Items[0];
            Assert.Equal("entrance", k["kind"].AsString());
            Assert.Equal("Placemaker.Fake.Marker.Entrance", k["label"].AsString());
            Assert.Equal("Media/Game/Icons/SmallRoad.svg", k["icon"].AsString());
            Assert.Equal(1, j["markersRequired"].AsNumber());
            g.NeedsEntrance = false;
            j = JsonValue.Parse(UiModels.Design(r, "t", false, "0", null, null, _ => g).ToString())["root"];
            Assert.Equal(0, j["markersRequired"].AsNumber());
        }

        [Fact]
        public void MarkerKindLabelPrefersControlOption()
        {
            var g = new ToolFakeGenerator();
            var kinds = MarkerKinds.For(g);
            Assert.Single(kinds);
            Assert.Equal("Placemaker.Fake.Marker.Entrance", kinds[0].LabelKey);
            Assert.Equal("Placemaker.Parking.Marker.Entrance", MarkerKinds.For(new Placemaker.Core.Layers.Parking.ParkingLayer())[0].LabelKey);
        }

        [Fact]
        public void ControlDefaultInCatalog()
        {
            var j = JsonValue.Parse(UiModels.Catalog(new IAreaGenerator[] { new ToolFakeGenerator() }, new RecipeListEntry[0]).ToString());
            var controls = j["layers"].Items[0]["controls"].Items;
            Assert.Equal(50, controls[0]["default"].AsNumber());
            Assert.True(controls[1]["default"].AsBool());
            Assert.Equal("formal", controls[2]["default"].AsString());
            Assert.False(controls[4].Has("default"));
        }

        [Fact]
        public void IconFilterAppliesToCatalogIcons()
        {
            try
            {
                UiModels.IconFilter = s => s == "Media/Game/Icons/ParkingFacility.svg" ? "Media/Game/Icons/Parking.svg" : s;
                var j = JsonValue.Parse(UiModels.Catalog(new IAreaGenerator[] { new Placemaker.Core.Layers.Parking.ParkingLayer() }, new RecipeListEntry[0]).ToString());
                Assert.Equal("Media/Game/Icons/Parking.svg", j["layers"].Items[0]["icon"].AsString());
            }
            finally { UiModels.IconFilter = null; }
        }
    }
}
