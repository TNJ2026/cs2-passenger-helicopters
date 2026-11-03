using System.Collections.Generic;
using System.Reflection;
using Colossal.UI.Binding;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;

namespace CityWeaver
{
    // Filter the native information section after it computes its vehicle list,
    // before it serializes the binding. No prefab, fleet or route data is removed.
    public partial class PassengerHelicopterVehiclePickerSystem : GameSystemBase
    {
        private static readonly FieldInfo Sections=typeof(SelectedInfoUISystem).GetField("m_MiddleSections",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly PropertyInfo Available=typeof(SelectVehiclesSection).GetProperty("availablePrimaryVehicles",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly PropertyInfo TooltipKeys=typeof(InfoSectionBase).GetProperty("tooltipKeys",BindingFlags.Instance|BindingFlags.NonPublic);
        private bool m_Installed,m_Reported;
        protected override void OnUpdate()
        {
            if(m_Installed)return;
            if(Sections==null||Available==null||TooltipKeys==null)
            {
                if(!m_Reported){HelicopterRuntime.log.Info("Helicopter vehicle picker unavailable: native section API changed");m_Reported=true;}
                return;
            }
            var info=World.GetExistingSystemManaged<SelectedInfoUISystem>();
            if(info==null||!(Sections.GetValue(info) is List<ISectionSource> sections))return;
            for(int i=0;i<sections.Count;i++)
                if(sections[i] is SelectVehiclesSection native)
                {
                    sections[i]=new PickerSection(this,info,native);
                    m_Installed=true;info.RequestUpdate();
                    HelicopterRuntime.log.Info("Helicopter vehicle picker presentation installed");
                    break;
                }
        }
        private sealed class PickerSection : ISectionSource
        {
            private readonly PassengerHelicopterVehiclePickerSystem m_System;
            private readonly SelectedInfoUISystem m_Info;
            private readonly SelectVehiclesSection m_Native;
            public PickerSection(PassengerHelicopterVehiclePickerSystem system,SelectedInfoUISystem info,SelectVehiclesSection native)
            {m_System=system;m_Info=info;m_Native=native;}
            public void RequestUpdate()=>m_Native.RequestUpdate();
            public void PerformUpdate()
            {
                m_Native.PerformUpdate();
                var em=m_System.EntityManager;
                var ps=m_System.World.GetExistingSystemManaged<PrefabSystem>();
                var line=m_Info.selectedEntity;
                if(ps==null||!em.HasComponent<Game.Routes.TransportLine>(line)||!em.HasComponent<PrefabRef>(line)||
                    !ps.TryGetPrefab<TransportLinePrefab>(em.GetComponentData<PrefabRef>(line).m_Prefab,out var route)||
                    route==null||route.name!=PassengerHelicopterSystem.Line||route.isBuiltin||route.isSubscribedMod)return;
                var list=(NativeList<Entity>)Available.GetValue(m_Native);
                // Keep the native list intact if the replacement is unavailable.
                bool hasDefault=false;
                foreach(var entity in list)
                    if(ps.TryGetPrefab<HelicopterPrefab>(entity,out var prefab)&&prefab!=null&&
                        prefab.name==PassengerHelicopterSystem.Vehicle&&!prefab.isSubscribedMod)hasDefault=true;
                if(hasDefault)
                    for(int i=list.Length-1;i>=0;i--)
                        if(ps.TryGetPrefab<HelicopterPrefab>(list[i],out var prefab)&&prefab!=null&&
                            prefab.name=="HelicopterPassenger01"&&!prefab.isSubscribedMod)list.RemoveAt(i);
                if(TooltipKeys.GetValue(m_Native) is List<string> keys&&!keys.Contains("CityWeaverHelicopter"))keys.Add("CityWeaverHelicopter");
            }
            public void Write(IJsonWriter writer)=>m_Native.Write(writer);
        }
    }
}
