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
            Log.Info("Placemaker loaded");
        }

        public void OnDispose() { }
    }
}
