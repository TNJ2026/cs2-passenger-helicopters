using CityWeaver;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace PassengerHelicopters
{
    public sealed class Mod : IMod
    {
        public void OnLoad(UpdateSystem updateSystem)
        {
            GameManager.instance.localizationManager.AddSource("en-US",new HelicopterLocale(false));
            GameManager.instance.localizationManager.AddSource("zh-HANS",new HelicopterLocale(true));
            HelicopterRuntime.Load(updateSystem);
        }
        public void OnDispose() { HelicopterRuntime.Unload(); }
    }
}
