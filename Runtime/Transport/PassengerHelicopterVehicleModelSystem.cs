using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Hiding an obsolete candidate does not remove serialized line selections.
    // Migrate those selections through the same buffer used by the native picker.
    // Native line management handles any fleet change and passenger drop-off.
    public partial class PassengerHelicopterVehicleModelSystem : GameSystemBase
    {
        private EntityQuery m_Lines;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Lines=GetEntityQuery(ComponentType.ReadOnly<TransportLine>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadWrite<VehicleModel>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            if(ps==null||!ps.TryGetPrefab(new PrefabID(nameof(HelicopterPrefab),PassengerHelicopterSystem.Vehicle),out var canonical)||
                !(canonical is HelicopterPrefab)||canonical.isBuiltin||canonical.isSubscribedMod||!ps.TryGetEntity(canonical,out var model)||
                !EntityManager.HasComponent<PublicTransportVehicleData>(model)||EntityManager.HasComponent<Created>(model)||
                EntityManager.HasComponent<Deleted>(model)||
                EntityManager.GetComponentData<PublicTransportVehicleData>(model).m_TransportType!=TransportType.Helicopter)return;
            EntityManager.CompleteAllTrackedJobs();
            using(var lines=m_Lines.ToEntityArray(Allocator.Temp))foreach(var line in lines)
            {
                if(!ps.TryGetPrefab<TransportLinePrefab>(EntityManager.GetComponentData<PrefabRef>(line).m_Prefab,out var route)||
                    route==null||route.name!=PassengerHelicopterSystem.Line||route.isBuiltin||route.isSubscribedMod)continue;
                var models=EntityManager.GetBuffer<VehicleModel>(line);
                bool changed=false;
                if(models.Length==0)
                {
                    models.Add(new VehicleModel{m_PrimaryPrefab=model,m_SecondaryPrefab=Entity.Null});
                    changed=true;
                }
                var seen=new HashSet<(Entity,Entity)>();
                for(int i=0;i<models.Length;i++)
                {
                    var entry=models[i];
                    bool automatic=entry.m_PrimaryPrefab==Entity.Null&&entry.m_SecondaryPrefab==Entity.Null;
                    bool legacy=entry.m_PrimaryPrefab!=Entity.Null&&ps.TryGetPrefab<HelicopterPrefab>(entry.m_PrimaryPrefab,out var old)&&
                        old!=null&&old.name=="HelicopterPassenger01"&&!old.isSubscribedMod;
                    if(automatic||legacy){entry.m_PrimaryPrefab=model;models[i]=entry;changed=true;}
                    if(!seen.Add((entry.m_PrimaryPrefab,entry.m_SecondaryPrefab)))
                    {models.RemoveAt(i--);changed=true;}
                }
                if(changed)
                {
                    World.GetExistingSystemManaged<Game.UI.InGame.SelectedInfoUISystem>()?.RequestUpdate();
                    HelicopterRuntime.log.Info("Passenger helicopter line model selection migrated to Mil Mi-26TC: "+line);
                }
            }
        }
    }
}
