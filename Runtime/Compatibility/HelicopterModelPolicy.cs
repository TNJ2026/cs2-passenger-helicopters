using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace CityWeaver
{
    internal static class HelicopterModelPolicy
    {
        private static bool? s_Enabled;
        internal static bool IsHelicopter(string name) => name == "CityWeaver Passenger Helicopter" || name == "HelicopterPassenger01";
        internal static bool Parse(string json)
        {
            if (json == null) return true;
            var value = JObject.Parse(json)["imported_models_enabled"];
            if (value == null || value.Type != JTokenType.Boolean) throw new ArgumentException("imported_models_enabled must be a boolean.");
            return (bool)value;
        }
        internal static bool ImportedModelsEnabled
        {
            get
            {
                if (s_Enabled.HasValue) return s_Enabled.Value;
                var file = Path.Combine(Colossal.PSI.Environment.EnvPath.kUserDataPath,"ModsData","CityWeaver","HelicopterModels.json");
                try { s_Enabled = Parse(File.Exists(file) ? File.ReadAllText(file) : null); }
                catch (Exception e) { HelicopterRuntime.log.Error("Helicopter model policy: " + e.Message); s_Enabled = false; }
                return s_Enabled.Value;
            }
        }
    }
}
