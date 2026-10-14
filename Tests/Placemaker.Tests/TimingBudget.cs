using System;
using System.Globalization;

namespace Placemaker.Tests
{
    /// <summary>
    /// Wall-clock budgets for the timing tests. The numbers in the tests are the design budgets on a
    /// quiet developer machine (ARCHITECTURE: &lt; 250 ms for 5 ha on one core); shared CI runners and
    /// parallel worktree builds are several times slower and made those asserts flaky.
    ///
    /// Scale (first that applies):
    ///   PLACEMAKER_TIMING_SCALE=&lt;factor&gt;   explicit factor (e.g. 0 or "off" disables the budgets)
    ///   CI / PLACEMAKER_PARALLEL set        ×4 (CI runners, several test runs at once)
    ///   ≤ 2 logical processors              ×2
    ///   otherwise                           ×1
    /// Timing classes still run in TimingCollection (sequential, after the parallel tests).
    /// </summary>
    public static class TimingBudget
    {
        public static readonly double Scale = ReadScale();

        /// <summary>The budget in ms for a design budget of <paramref name="designMs"/>.</summary>
        public static long Ms(double designMs) =>
            Scale <= 0 ? long.MaxValue : (long)Math.Ceiling(designMs * Scale);

        private static double ReadScale()
        {
            var env = Environment.GetEnvironmentVariable("PLACEMAKER_TIMING_SCALE");
            if (!string.IsNullOrWhiteSpace(env))
            {
                if (string.Equals(env.Trim(), "off", StringComparison.OrdinalIgnoreCase)) return 0;
                if (double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 0) return f;
            }
            if (Set("CI") || Set("PLACEMAKER_PARALLEL") || Set("TF_BUILD") || Set("GITHUB_ACTIONS")) return 4;
            if (Environment.ProcessorCount <= 2) return 2;
            return 1;
        }

        private static bool Set(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(v) && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
        }
    }
}
