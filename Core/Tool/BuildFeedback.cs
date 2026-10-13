// Placemaker — what the player sees about the last build attempt (message + red markers).
//
// Playtest 1: a failed build left its red error markers on the map while the player started the
// next area. Rule (FEEDBACK-1 US8): the failure shows where and why, and everything about it
// disappears as soon as anything changes — area or design edit, stage change, a new build — and
// after a successful build. Pure so the flow is unit-tested (Tests/ToolBuildFlowTests.cs).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public sealed class BuildFeedback
    {
        private readonly List<float3> _errors = new List<float3>();

        /// <summary>Issue shown in the review card after a refused or failed build, null otherwise.</summary>
        public PlanIssue Failure { get; private set; }

        /// <summary>Red markers on the map (only while the failed attempt is still what the player looks at).</summary>
        public IReadOnlyList<float3> ErrorPositions => _errors;

        public bool HasFailure => Failure != null;

        /// <summary>Bumped on every change (UI change detection).</summary>
        public int Version { get; private set; }

        public void Clear()
        {
            if (Failure == null && _errors.Count == 0) return;
            Failure = null;
            _errors.Clear();
            Version++;
        }

        /// <summary>A build starts: the previous attempt's feedback is gone.</summary>
        public void Started() => Clear();

        public void Succeeded() => Clear();

        /// <summary>Area, design or markers changed: the failed attempt no longer describes what would be built.</summary>
        public void InputChanged() => Clear();

        /// <summary>The player moved to another stage (the tool's own Building→Review transition is not reported here).</summary>
        public void StageChanged() => Clear();

        public void Failed(string messageKey, object[] args, IEnumerable<float3> positions, string fallbackKey = "Placemaker.Issue.BuildFailed")
        {
            _errors.Clear();
            if (positions != null) _errors.AddRange(positions);
            Failure = new PlanIssue
            {
                Severity = IssueSeverity.Error,
                Key = string.IsNullOrEmpty(messageKey) ? fallbackKey : messageKey,
                Args = args ?? Array.Empty<object>(),
                At = _errors.Count > 0 ? _errors[0] : (float3?)null,
            };
            Version++;
        }
    }
}
