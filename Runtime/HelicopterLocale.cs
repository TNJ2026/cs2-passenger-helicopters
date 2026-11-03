using System.Collections.Generic;
using Colossal;
namespace CityWeaver {
 public sealed class HelicopterLocale : IDictionarySource {
  private readonly bool chinese;
  public HelicopterLocale(bool chinese) { this.chinese=chinese; }
  public static string RouteErrorKey(string error) {
   switch (error) {
    case null:
    case "INCOMPATIBLE_TRANSPORT_STOP": return null;
    case "HELICOPTER_FIRST_STOP_REQUIRES_AIRPORT": return "CityWeaver.HelicopterRouteError[FIRST_STOP_REQUIRES_AIRPORT]";
    case "HELICOPTER_SAME_AIRPORT_LEG": return "CityWeaver.HelicopterRouteError[SAME_AIRPORT_LEG]";
    default: return "CityWeaver.HelicopterRouteError[UNAVAILABLE]";
   }
  }
  public IEnumerable<KeyValuePair<string,string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string,int> indexCounts) {
   return new Dictionary<string,string> {
                { "Assets.NAME[CityWeaver Helicopter Transport]", chinese ? "直升机客运" : "Passenger Helicopters" },
                { "Assets.DESCRIPTION[CityWeaver Helicopter Transport]", chinese ? "使用直升机场和独立停机坪建立市民客运线路。" : "Create passenger helicopter lines using heliports and independent helipads." },
                // Native construction tabs use category-specific keys rather than Assets.NAME/DESCRIPTION.
                { "SubServices.NAME[CityWeaver Helicopter Transport]", chinese ? "直升机客运" : "Passenger Helicopters" },
                { "Assets.SUB_SERVICE_DESCRIPTION[CityWeaver Helicopter Transport]", chinese ? "使用直升机场和独立停机坪建立市民客运线路。" : "Create passenger helicopter lines using heliports and independent helipads." },
                { "Assets.NAME[" + ImportedHelipadMenuSystem.Airport + "]", chinese ? "双停机坪直升机场" : "Twin-pad Passenger Heliport" },
                { "Assets.DESCRIPTION[" + ImportedHelipadMenuSystem.Airport + "]", chinese ? "沿道路建设的直升机场，设有两个独立登机机位及机库，提供六架直升机的调度和维护。需接入道路及人行道。" : "Roadside heliport with two independent boarding pads and a depot providing dispatch and maintenance for six helicopters. Requires road and pedestrian access." },
                { "Assets.NAME[" + ImportedHelipadMenuSystem.Station + "]", chinese ? "屋顶停机坪客运站" : "Rooftop Helipad Passenger Station" },
                { "Assets.DESCRIPTION[" + ImportedHelipadMenuSystem.Station + "]", chinese ? "沿道路建设的直升机客运站。需另建含机库的直升机场提供车辆。" : "Roadside helicopter passenger stop. Requires a separate heliport depot to supply vehicles." },
                { "Assets.NAME[CityWeaver Passenger Heliport]", chinese ? "客运直升机场（含机库）" : "Passenger Heliport with Depot" },
                { "Assets.DESCRIPTION[CityWeaver Passenger Heliport]", chinese ? "必须沿道路建设。提供客运站点及 6 架直升机的调度和维护设施。" : "Roadside passenger terminal with dispatch and maintenance for six helicopters." },
                { "Assets.NAME[CityWeaver Helicopter Station]", chinese ? "直升机客运站" : "Helicopter Passenger Station" },
                { "Assets.DESCRIPTION[CityWeaver Helicopter Station]", chinese ? "必须沿道路建设。连接客运直升机线路，需另建带机库的客运直升机场。" : "Roadside passenger stop. Requires a separate passenger heliport depot." },
                { "Assets.NAME[CityWeaver Helicopter Boarding Stop]", chinese ? "直升机登机站点" : "Helicopter Boarding Stop" },
                { "Assets.DESCRIPTION[CityWeaver Helicopter Boarding Stop]", chinese ? "直升机客运线路的候车、登机和下机站点。" : "A waiting, boarding and alighting stop for passenger helicopter lines." },
                { "Assets.NAME[CityWeaver Passenger Helicopter]", "Mil Mi-26TC" },
                { "SelectedInfoPanel.SELECT_VEHICLE_PRIMARY[CityWeaver Helicopter Line]", chinese ? "选择直升机机型" : "Select helicopter model" },
                { "SelectedInfoPanel.TOOLTIP[SelectVehiclesSectionCityWeaverHelicopter]", chinese ? "选择这条线路使用的直升机机型。默认Mil Mi-26TC可搭载60名乘客，最高巡航速度350公里/小时。" : "Choose the helicopter model for this line. The default Mil Mi-26TC carries 60 passengers and cruises at up to 350 km/h." },
                { "Assets.DESCRIPTION[CityWeaver Passenger Helicopter]", chinese ? "Mil Mi-26TC客运直升机，60座，最高巡航速度350公里/小时。" : "Mil Mi-26TC passenger helicopter, 60 seats, cruising speed up to 350 km/h." },
                { "Assets.NAME[CityWeaver Helicopter Line]", chinese ? "直升机客运线路" : "Passenger Helicopter Line" },
                { "CityWeaver.RouteName[CityWeaver Helicopter Line]", chinese ? "线路{NUMBER}" : "Line {NUMBER}" },
                { "Assets.DESCRIPTION[CityWeaver Helicopter Line]", chinese ? "连接不同机场的登机站点，运输市民。" : "Connect airport boarding stops to transport citizens." },
                { "CityWeaver.HelicopterRouteError[FIRST_STOP_REQUIRES_AIRPORT]", chinese ? "首站必须选择直升机机场；独立停机坪只能作为后续站点。" : "The first stop must be a heliport with a depot. Independent helipads can only be added as subsequent stops." },
                { "CityWeaver.HelicopterRouteError[SAME_AIRPORT_LEG]", chinese ? "同一机场的两个停机坪之间不能建立航段，请选择其他机场或停机坪。" : "A flight leg cannot connect two pads at the same heliport. Select another heliport or helipad." },
                { "CityWeaver.HelicopterRouteError[UNAVAILABLE]", chinese ? "直升机线路工具暂不可用。" : "The helicopter line tool is temporarily unavailable." },
   };
  }
  public void Unload() { }
 }
}
