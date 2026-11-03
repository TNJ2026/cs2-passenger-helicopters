using Game.Prefabs;

namespace CityWeaver
{
    internal static class HelicopterAssetOwnership
    {
        // Preserve local-import behavior. Subscription assets are accepted only
        // when both their native prefab name and original CID belong to this mod.
        internal static bool RejectForeignSubscription(PrefabBase prefab) => prefab != null && prefab.isSubscribedMod &&
            !BundledHelicopterAssets.Matches(prefab.name, prefab.asset?.id.guid.ToString());
    }
}
