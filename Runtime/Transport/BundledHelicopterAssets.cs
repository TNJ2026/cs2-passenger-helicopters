using System;
using System.Collections.Generic;

namespace CityWeaver
{
    // Stable native resource IDs shared by local imports and subscription content.
    // A matching display name alone must never authorize modifying another mod.
    internal static class BundledHelicopterAssets
    {
        private static readonly Dictionary<string, string> Ids = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0", "2f2dd49bae855f1a7db2f980737b0ac0" },
            { "CityWeaver_7e149f3fb9574d47adfac7977c83d900", "85c24783f85a5c705e2c1015689d1c1f" },
            { "CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2", "0bc60203402d25f6b1e1245f5f1661a1" },
            { "CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2_0_CWMilMi26tcPart01 Mesh", "6cd1156866fa2b925b89cb144c490ab0" },
            { "CityWeaver_bb6e274887ec4c5ea8bef7d725e3313d_0_CWHeliportPart01 Mesh", "7cc3933cb599f03bf4e07022cd483d7e" },
            { "CityWeaver_ca89b95a71b64787a5ead4ea15f5ecc1_0_CWHangarAnimatedPart09 Mesh", "08e59d859d84a3663d7826755b78b123" },
            { "CityWeaver_7ecb90d4a10f4ae1b921aade4455da6e_0_CWHangarAnimatedPart17 Mesh", "d5b647f633fcd215f070517f5700fae2" },
            { "CityWeaver_22b0ac34577b451caa08a343bc489fd7_0_CWHeliportSiteAsphalt Mesh", "82b6dfb4573d10998067b3a67121cf27" },
            { "CityWeaver_7ed34b9a7a2543adb99b382a79255ecd_0_CWHeliportTerminalPart01 Mesh", "a124de4a7f012c5a0e45d0e0ecc8f268" },
            { "CityWeaver_5867035097c349bc821bf786d28376fb_0_CWHeliportTerminalPart09 Mesh", "8491255619150eee45eef4dcb2a1e8a0" },
            { "CityWeaver Helicopter Boarding Stop", "e2592fa67f8e2465d7d32de27b2ab067" },
            { "CityWeaver Helicopter Transport", "a07c329a0d2f7ac631f9e46f12ce9065" }
        };

        internal static bool Matches(string name, string id) => name != null && id != null &&
            Ids.TryGetValue(name, out var expected) && string.Equals(id, expected, StringComparison.OrdinalIgnoreCase);
    }
}
