using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace CityWeaver.HelicopterModels
{
    internal static class HelicopterModelDefaults
    {
        internal static VehicleModelSaveData Load()
        {
            using(var stream=typeof(HelicopterModelDefaults).Assembly.GetManifestResourceStream("CityWeaver.Defaults.Mi26.json"))
            {
                if(stream==null)throw new InvalidOperationException("Bundled Mi-26 model configuration missing.");
                using(var reader=new StreamReader(stream))
                {
                    var configuration=JObject.Parse(reader.ReadToEnd());
                    var data=new VehicleModelSaveData();
                    data.Put("CityWeaver Passenger Helicopter",configuration);
                    data.Put("HelicopterPassenger01",configuration);
                    return data;
                }
            }
        }
    }
}
