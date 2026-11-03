using System;
using Game;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // HelicopterData is serialized with prefab copies. Apply the same cruising
    // limit to both passenger models on new games and after save restoration.
    public partial class PassengerHelicopterSpeedSystem : GameSystemBase
    {
        private EntityQuery m_Prefabs;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs=GetEntityQuery(ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadWrite<HelicopterData>(),ComponentType.ReadOnly<PublicTransportVehicleData>(),
                ComponentType.Exclude<Created>(),ComponentType.Exclude<Deleted>());
        }
        protected override void OnUpdate()
        {
            var ps=World.GetExistingSystemManaged<PrefabSystem>();
            if(ps==null)return;
            using(var entities=m_Prefabs.ToEntityArray(Allocator.Temp))foreach(var entity in entities)
            {
                if(!ps.TryGetPrefab<HelicopterPrefab>(entity,out var prefab)||prefab==null||prefab.isSubscribedMod||
                    (prefab.name!=PassengerHelicopterSystem.Vehicle&&prefab.name!="HelicopterPassenger01")||
                    EntityManager.GetComponentData<PublicTransportVehicleData>(entity).m_TransportType!=TransportType.Helicopter)continue;
                float speed=PassengerHelicopterRules.FlyingMaxSpeedKph/3.6f;
                prefab.m_FlyingMaxSpeed=PassengerHelicopterRules.FlyingMaxSpeedKph;
                var data=EntityManager.GetComponentData<HelicopterData>(entity);
                // Native initialization divides visual sway by maximum speed.
                // Preserve the authored sway amplitude when the speed changes.
                float sway=prefab.m_VelocitySwayFactor/speed;
                if(Math.Abs(data.m_FlyingMaxSpeed-speed)<.0001f&&Math.Abs(data.m_VelocitySwayFactor-sway)<.000001f)continue;
                data.m_FlyingMaxSpeed=speed;
                data.m_VelocitySwayFactor=sway;
                EntityManager.SetComponentData(entity,data);
                HelicopterRuntime.log.Info("Passenger helicopter cruise speed set to "+PassengerHelicopterRules.FlyingMaxSpeedKph+" km/h: "+entity);
            }
        }
    }
}
