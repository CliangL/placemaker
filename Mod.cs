using System;
using Colossal.Logging;
using Game;
using Game.Modding;

namespace Placemaker
{
    public sealed class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(Placemaker)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"Placemaker {typeof(Mod).Assembly.GetName().Version} loading");
            // Each workstream registers its own systems; a failure in one area is logged and the
            // rest still loads, so a broken feature never takes the whole mod (or the game) down.
            Try("build", () => Game.Build.BuildRegistration.Register(updateSystem));
        }

        public void OnDispose()
        {
            Game.Services.Reset();
        }

        private static void Try(string area, Action register)
        {
            try { register(); }
            catch (Exception e) { Log.Error(e, $"Placemaker: registering '{area}' failed"); }
        }
    }
}
