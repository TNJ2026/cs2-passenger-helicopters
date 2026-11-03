using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CityWeaver.HelicopterModels
{
    // Transient rendering only. Native momentum and flight control stay intact;
    // a continuous visual phase applies the grounded power envelope to both the
    // native bones and the custom real-blade angular shutter.
    public partial class HelicopterRotorExposureSystem:GameSystemBase
    {
        private sealed class Model:IDisposable
        {
            internal RenderPrefab Render;
            internal readonly HelicopterRotorExposureMesh[] Rotors=new HelicopterRotorExposureMesh[2];
            internal readonly Dictionary<int,Material> Materials=new Dictionary<int,Material>();
            public void Dispose(){foreach(var r in Rotors)r?.Dispose();foreach(var m in Materials.Values)UnityEngine.Object.Destroy(m);Materials.Clear();}
        }
        private sealed class Pose
        {
            internal Entity Prefab,Render;
            internal int Offset,Seen;
            internal float Main,Tail,StableCruise;
            internal float Power=1,MainPhase,TailPhase;
            internal bool PhaseReady;
            internal int PowerBand=-1;
            internal bool Hidden;
            internal float3 MainScale,TailScale;
        }
        private struct Draw
        {
            internal Model Model;internal int Rotor;internal Matrix4x4 Matrix;
            internal UnityEngine.Color Tint;internal float Amount,Sweep,Distance;
        }
        [Serializable]
        private sealed class ExposurePass:CustomPass
        {
            [NonSerialized]internal HelicopterRotorExposureSystem Owner;
            protected override bool executeInSceneView=>false;
            protected override void Execute(CustomPassContext ctx)=>Owner?.DrawExposure(ctx);
        }
        private EntityQuery m_Vehicles;
        private readonly Dictionary<VehiclePrefab,Model> m_Models=new Dictionary<VehiclePrefab,Model>();
        private readonly Dictionary<Entity,Pose> m_Poses=new Dictionary<Entity,Pose>();
        private readonly List<Entity> m_Expired=new List<Entity>();
        private readonly List<Draw> m_Draws=new List<Draw>(32);
        private readonly Plane[] m_Frustum=new Plane[6];
        private readonly MaterialPropertyBlock m_Block=new MaterialPropertyBlock();
        private AssetBundle m_Bundle;private Shader m_Shader;private GameObject m_VolumeObject;
        private CustomPassVolume m_Volume;private Camera m_Camera;private bool m_Failed;
        private int m_LastPass=-1,m_Generation;private float m_NextLog;private string m_LogSignature;
        private HelicopterRotorFrameGate m_FrameGate;
        private bool m_DrawLogged,m_ClearDrawLogged;
        public int ActiveVehicles,AngularVehicles,ClearBladeVehicles,LastDrawCalls,LastDrawTriangles;
        public double PrepareMilliseconds;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Vehicles=GetEntityQuery(ComponentType.ReadOnly<Game.Vehicles.Helicopter>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadWrite<Bone>(),ComponentType.ReadWrite<Skeleton>(),ComponentType.ReadOnly<Momentum>(),
                ComponentType.ReadOnly<InterpolatedTransform>(),ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
        }
        internal void Track(VehiclePrefab target,RenderPrefab render)
        {
            if(m_Models.ContainsKey(target)||m_Failed)return;
            Model model=null;
            try
            {
                EnsurePass();if(m_Shader==null)return;
                var rig=render.GetComponent<ProceduralAnimationProperties>();
                if(rig?.m_Bones?.Length!=3||rig.m_Bones[1].name!="MainRotor"||rig.m_Bones[2].name!="TailRotor")return;
                model=new Model{Render=render};
                var sources=render.ObtainMeshes();
                var binds=new Matrix4x4[3];for(int i=0;i<3;i++)binds[i]=rig.m_Bones[i].bindPose;
                try{if(sources==null)throw new InvalidOperationException("No readable real-blade mesh");for(int i=0;i<2;i++)model.Rotors[i]=HelicopterRotorExposureMesh.Build(sources,binds,i+1);}
                finally{render.ReleaseMeshes();}
                var materials=render.ObtainMaterials(false);
                try
                {
                    foreach(var rotor in model.Rotors)foreach(var slice in rotor.Slices)
                    {
                        if(model.Materials.ContainsKey(slice.Surface))continue;
                        if(slice.Surface>=materials.Length)throw new InvalidOperationException("Rotor material slot mismatch");
                        var source=materials[slice.Surface];var material=new Material(m_Shader){name="Mi26 angular real-blade surface",hideFlags=HideFlags.HideAndDontSave};
                        var albedo=source.GetTexture("_BaseColorMap");
                        if(!(albedo is Texture2D)){UnityEngine.Object.Destroy(material);throw new InvalidOperationException("Real-blade surface has no regular 2D albedo");}
                        material.SetTexture("_BaseColorMap",albedo);
                        material.SetTexture("_ControlMask",source.HasProperty("_ControlMask")?source.GetTexture("_ControlMask"):Texture2D.blackTexture);
                        model.Materials.Add(slice.Surface,material);
                    }
                }
                finally{render.ReleaseMaterials();}
                m_Models.Add(target,model);
                HelicopterRuntime.log.Info("Mi-26 angular exposure cached: "+target.name+", samples="+HelicopterRotorExposureRules.Samples+", main vertices="+model.Rotors[0].Vertices+", main triangles="+model.Rotors[0].Triangles+", tail vertices="+model.Rotors[1].Vertices+", tail triangles="+model.Rotors[1].Triangles+"; real geometry only, no disk assets.");
            }
            catch(Exception e){model?.Dispose();m_Failed=true;HelicopterRuntime.log.Error(e,"Mi-26 angular exposure unavailable; native real blades remain enabled");}
        }
        private void EnsurePass()
        {
            if(m_Shader!=null&&m_Volume!=null)return;
            if(m_Shader==null)
            {
                using(var stream=typeof(HelicopterRotorExposureSystem).Assembly.GetManifestResourceStream("CityWeaver.Rendering.RotorAngular.bundle"))
                {
                    if(stream==null)throw new InvalidOperationException("Embedded angular shader missing");
                    using(var bytes=new MemoryStream()){stream.CopyTo(bytes);m_Bundle=AssetBundle.LoadFromMemory(bytes.ToArray());}
                }
                m_Shader=m_Bundle?.LoadAsset<Shader>("Assets/RotorAngular.shader");
                if(m_Shader==null||!m_Shader.isSupported)throw new InvalidOperationException("Angular shader is not supported on this graphics device");
            }
            m_VolumeObject=new GameObject("PassengerHelicopters angular shutter"){hideFlags=HideFlags.HideAndDontSave};
            UnityEngine.Object.DontDestroyOnLoad(m_VolumeObject);
            m_Volume=m_VolumeObject.AddComponent<CustomPassVolume>();m_Volume.isGlobal=true;
            // After native motion blur: do not feed curved shutter samples back
            // into the native straight velocity blur, or smear the aircraft body.
            m_Volume.injectionPoint=CustomPassInjectionPoint.AfterPostProcess;
            m_Volume.customPasses.Add(new ExposurePass{Owner=this,name="Mi26 real-blade angular exposure",targetColorBuffer=CustomPass.TargetBuffer.Camera,targetDepthBuffer=CustomPass.TargetBuffer.Camera});
        }
        internal void ResetForLoad()
        {
            m_FrameGate.Reset();
            RestoreAll();m_Draws.Clear();m_Poses.Clear();foreach(var model in m_Models.Values)model.Dispose();m_Models.Clear();
            m_LastPass=-1;m_LogSignature=null;m_DrawLogged=m_ClearDrawLogged=false;m_Failed=false;ActiveVehicles=AngularVehicles=ClearBladeVehicles=LastDrawCalls=LastDrawTriangles=0;
        }
        protected override void OnDestroy()
        {
            ResetForLoad();m_Failed=true;if(m_Volume!=null)m_Volume.enabled=false;if(m_VolumeObject!=null)UnityEngine.Object.Destroy(m_VolumeObject);if(m_Bundle!=null)m_Bundle.Unload(true);base.OnDestroy();
        }
        protected override void OnUpdate()
        {
            if(!m_FrameGate.Enter(UnityEngine.Time.frameCount))return;
            long started=Stopwatch.GetTimestamp();m_Draws.Clear();ActiveVehicles=AngularVehicles=ClearBladeVehicles=0;m_Generation++;
            var gm=GameManager.instance;var cameraSystem=World.GetExistingSystemManaged<CameraUpdateSystem>();var camera=cameraSystem?.activeCamera;
            if(camera!=m_Camera)m_LastPass=-1;m_Camera=camera;
            if(m_Volume!=null)m_Volume.targetCamera=m_Camera;
            if(!HelicopterRuntime.Loaded||gm==null||gm.isGameLoading||gm.gameMode!=GameMode.Game||m_Camera==null||m_Models.Count==0){RestoreAll();return;}
            var ps=World.GetExistingSystemManaged<PrefabSystem>();var simulation=World.GetExistingSystemManaged<Game.Simulation.SimulationSystem>();
            // A stalled interpolation frame is not a pause and must not reset
            // the stable-cruise hold, especially in accelerated simulation.
            bool paused=simulation==null||HelicopterRotorExposureRules.Paused(simulation.selectedSpeed,simulation.smoothSpeed);
            var rendering=World.GetExistingSystemManaged<RenderingSystem>();
            float animationSeconds=HelicopterRotorSpeedRules.StepSeconds(rendering?.frameDelta??0,paused);
            bool ready=!m_Failed&&m_Shader!=null&&HelicopterRotorExposureRules.Fresh(UnityEngine.Time.frameCount,m_LastPass);
            var hd=HDCamera.GetOrCreate(m_Camera);var nativeBlur=hd?.volumeStack?.GetComponent<MotionBlur>();
            bool nativeMotionBlur=hd!=null&&hd.frameSettings.IsEnabled(FrameSettingsField.MotionBlur)&&nativeBlur!=null&&nativeBlur.active&&nativeBlur.intensity.value>0;
            GeometryUtility.CalculateFrustumPlanes(m_Camera,m_Frustum);
            // Synchronize the rotor inputs, not unrelated city simulation jobs.
            EntityManager.CompleteDependencyBeforeRW<Bone>();
            EntityManager.CompleteDependencyBeforeRW<Skeleton>();
            EntityManager.CompleteDependencyBeforeRO<Momentum>();
            EntityManager.CompleteDependencyBeforeRO<InterpolatedTransform>();
            using(var entities=m_Vehicles.ToEntityArray(Allocator.Temp))foreach(var entity in entities)
            {
                var prefab=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                if(!ps.TryGetPrefab<VehiclePrefab>(prefab,out var target)||!m_Models.TryGetValue(target,out var model)||!EntityManager.HasBuffer<SubMesh>(prefab))continue;
                var meshes=EntityManager.GetBuffer<SubMesh>(prefab,true);var skeletons=EntityManager.GetBuffer<Skeleton>(entity);
                if(meshes.Length!=1||skeletons.Length!=1||meshes[0].m_SubMesh!=ps.GetEntity(model.Render))continue;
                var skeleton=skeletons[0];var bones=EntityManager.GetBuffer<Bone>(entity);var spin=EntityManager.GetBuffer<Momentum>(entity,true);
                var rig=EntityManager.GetBuffer<ProceduralBone>(meshes[0].m_SubMesh,true);
                int offset=skeleton.m_BoneOffset;
                if(skeleton.m_BufferAllocation.Empty||rig.Length!=3||offset+2>=bones.Length||offset+2>=spin.Length)continue;
                if(!m_Poses.TryGetValue(entity,out var pose)){pose=new Pose();m_Poses.Add(entity,pose);}
                if(pose.Prefab!=prefab||pose.Render!=meshes[0].m_SubMesh||pose.Offset!=offset){pose.PhaseReady=false;pose.Power=1;pose.PowerBand=-1;}
                pose.Prefab=prefab;pose.Render=meshes[0].m_SubMesh;pose.Offset=offset;pose.Seen=m_Generation;
                pose.MainScale=rig[1].m_Scale;pose.TailScale=rig[2].m_Scale;
                var transform=EntityManager.GetComponentData<InterpolatedTransform>(entity);
                bool visible=GeometryUtility.TestPlanesAABB(m_Frustum,new Bounds((Vector3)transform.m_Position,Vector3.one*45));
                bool budget=ActiveVehicles<HelicopterRotorExposureRules.MaxVisibleVehicles&&math.distancesq(transform.m_Position,(float3)m_Camera.transform.position)<640000;
                var flags=EntityManager.HasComponent<Game.Vehicles.AircraftCurrentLane>(entity)?EntityManager.GetComponentData<Game.Vehicles.AircraftCurrentLane>(entity).m_LaneFlags:0;
                bool flying=(flags&Game.Vehicles.AircraftLaneFlags.Flying)!=0;
                bool maneuver=(flags&(Game.Vehicles.AircraftLaneFlags.TakingOff|Game.Vehicles.AircraftLaneFlags.Landing|Game.Vehicles.AircraftLaneFlags.Approaching))!=0;
                var transport=EntityManager.HasComponent<Game.Vehicles.PublicTransport>(entity)?EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(entity):default;
                bool boarding=(transport.m_State&Game.Vehicles.PublicTransportFlags.Boarding)!=0;
                var velocity=EntityManager.HasComponent<Game.Objects.Moving>(entity)?EntityManager.GetComponentData<Game.Objects.Moving>(entity).m_Velocity:float3.zero;
                float speed=math.length(velocity);
                float departureSeconds=HelicopterRotorSpeedRules.DepartureSeconds(simulation?.frameIndex??0,transport.m_DepartureFrame);
                float targetPower=HelicopterRotorSpeedRules.Target(flying,maneuver,boarding,speed,velocity.y,departureSeconds);
                float beforePower=pose.Power;
                pose.Power=HelicopterRotorSpeedRules.Advance(pose.Power,targetPower,animationSeconds,flying||maneuver);
                // A midair load starts at full power. A ground load decelerates
                // from the native pose instead of snapping angle or speed.
                if(!pose.PhaseReady)
                {
                    for(int rotor=0;rotor<2;rotor++)
                    {
                        var relative=math.mul(math.inverse(rig[rotor+1].m_Rotation),bones[offset+rotor+1].m_Rotation);
                        var direction=math.forward(relative);float phase=math.atan2(direction.x,direction.z);
                        if(rotor==0)pose.MainPhase=phase;else pose.TailPhase=phase;
                    }
                }
                int powerBand=pose.Power<=HelicopterRotorSpeedRules.IdleFactor+.001f?0:pose.Power>=.999f?2:1;
                if(powerBand!=pose.PowerBand){pose.PowerBand=powerBand;HelicopterRuntime.log.Info("Mi-26 rotor power: "+entity+", factor="+pose.Power.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+", target="+targetPower.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+", flying="+flying+", maneuver="+maneuver+", boarding="+boarding+", departure seconds="+departureSeconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"; display only, native momentum unchanged.");}
                bool cruise=HelicopterRotorExposureRules.CruiseEligible(speed,velocity.y,flying,maneuver,boarding,paused);
                pose.StableCruise=HelicopterRotorExposureRules.StableCruise(pose.StableCruise,cruise,UnityEngine.Time.unscaledDeltaTime);
                var tint=UnityEngine.Color.white;
                if(EntityManager.HasComponent<Game.Routes.CurrentRoute>(entity)){var line=EntityManager.GetComponentData<Game.Routes.CurrentRoute>(entity).m_Route;if(EntityManager.HasComponent<Game.Routes.Color>(line))tint=(UnityEngine.Color)EntityManager.GetComponentData<Game.Routes.Color>(line).m_Color;}
                var primary=target.m_Meshes[0];var objectMatrix=Matrix4x4.TRS((Vector3)transform.m_Position,(Quaternion)transform.m_Rotation,Vector3.one)*Matrix4x4.TRS((Vector3)primary.m_Position,(Quaternion)primary.m_Rotation,Vector3.one);
                bool hidden=false,angularVehicle=false,clearVehicle=false;
                for(int rotor=0;rotor<2;rotor++)
                {
                    int index=offset+rotor+1;var bone=bones[index];float momentum=spin[index].m_Momentum;
                    float phase=rotor==0?pose.MainPhase:pose.TailPhase;
                    if(pose.PhaseReady)phase=HelicopterRotorSpeedRules.Phase(phase,momentum,animationSeconds,(flying||maneuver)?pose.Power:beforePower,pose.Power);
                    if(rotor==0)pose.MainPhase=phase;else pose.TailPhase=phase;
                    var rotation=math.mul(rig[rotor+1].m_Rotation,quaternion.RotateY(phase));
                    skeleton.m_CurrentUpdated|=!bone.m_Rotation.Equals(rotation);bone.m_Rotation=rotation;
                    float visualSpin=momentum*pose.Power;
                    float amount=rotor==0?pose.Main:pose.Tail;
                    float targetAmount=HelicopterRotorExposureRules.Target(momentum,speed,flying,maneuver,boarding,paused,velocity.y);
                    bool canBlur=cruise&&HelicopterRotorExposureRules.Finite(momentum)&&math.abs(momentum)>=3;
                    bool renderable=ready&&visible&&budget;
                    amount=!renderable?0:HelicopterRotorExposureRules.Advance(amount,targetAmount,UnityEngine.Time.unscaledDeltaTime,canBlur,pose.StableCruise);
                    if(rotor==0)pose.Main=amount;else pose.Tail=amount;
                    bool replace=renderable&&HelicopterRotorExposureRules.CustomBlades(amount,nativeMotionBlur);
                    if(replace)
                    {
                        m_Draws.Add(new Draw{Model=model,Rotor=rotor,Matrix=objectMatrix*Matrix4x4.TRS((Vector3)bone.m_Position,(Quaternion)bone.m_Rotation,(Vector3)rig[rotor+1].m_Scale),
                            Tint=tint,Amount=amount,Sweep=HelicopterRotorExposureRules.Sweep(visualSpin,rotor==1),Distance=math.distancesq(transform.m_Position,(float3)m_Camera.transform.position)});
                        hidden=true;
                        if(amount>.001f)angularVehicle=true;else clearVehicle=true;
                    }
                    var scale=rig[rotor+1].m_Scale*(replace?.0001f:1);
                    skeleton.m_CurrentUpdated|=!bone.m_Scale.Equals(scale);bone.m_Scale=scale;bones[index]=bone;
                }
                pose.PhaseReady=true;pose.Hidden=hidden;skeletons[0]=skeleton;if(hidden)ActiveVehicles++;if(angularVehicle)AngularVehicles++;if(clearVehicle)ClearBladeVehicles++;
            }
            m_Expired.Clear();foreach(var p in m_Poses)if(p.Value.Seen!=m_Generation){Restore(p.Key,p.Value);m_Expired.Add(p.Key);}foreach(var e in m_Expired)m_Poses.Remove(e);
            m_Draws.Sort((a,b)=>b.Distance.CompareTo(a.Distance));
            PrepareMilliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
            if(UnityEngine.Time.unscaledTime>=m_NextLog)
            {
                m_NextLog=UnityEngine.Time.unscaledTime+2;string signature=ready+":"+ActiveVehicles+":"+AngularVehicles+":"+ClearBladeVehicles+":"+LastDrawCalls;
                if(signature!=m_LogSignature){m_LogSignature=signature;HelicopterRuntime.log.Info("Mi-26 angular exposure: pass ready="+ready+", active vehicles="+ActiveVehicles+", previous-frame draw calls="+LastDrawCalls+", triangles="+LastDrawTriangles+", prepare ms="+PrepareMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+", angular vehicles="+AngularVehicles+", clear blade vehicles="+ClearBladeVehicles);}
            }
        }
        private void RestoreAll(){if(m_Poses.Count==0)return;EntityManager.CompleteAllTrackedJobs();foreach(var p in m_Poses)Restore(p.Key,p.Value);}
        private void Restore(Entity entity,Pose pose)
        {
            if(!pose.Hidden||!EntityManager.Exists(entity)||!EntityManager.HasBuffer<Bone>(entity)||!EntityManager.HasBuffer<Skeleton>(entity)||!EntityManager.HasComponent<PrefabRef>(entity))return;
            var prefab=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if(prefab!=pose.Prefab||!EntityManager.HasBuffer<SubMesh>(prefab))return;
            var meshes=EntityManager.GetBuffer<SubMesh>(prefab,true);var skeletons=EntityManager.GetBuffer<Skeleton>(entity);
            if(meshes.Length!=1||meshes[0].m_SubMesh!=pose.Render||skeletons.Length!=1||skeletons[0].m_BoneOffset!=pose.Offset)return;
            var bones=EntityManager.GetBuffer<Bone>(entity);if(pose.Offset+2>=bones.Length)return;var skeleton=skeletons[0];
            for(int i=1;i<=2;i++){var bone=bones[pose.Offset+i];bone.m_Scale=i==1?pose.MainScale:pose.TailScale;bones[pose.Offset+i]=bone;}
            skeleton.m_CurrentUpdated=true;skeletons[0]=skeleton;pose.Hidden=false;pose.Main=pose.Tail=pose.StableCruise=0;
        }
        private void DrawExposure(CustomPassContext ctx)
        {
            if(!HelicopterRuntime.Loaded||m_Failed||m_Camera==null||ctx.hdCamera.camera!=m_Camera)return;
            LastDrawCalls=LastDrawTriangles=0;
            try
            {
            int angularDraws=0,clearDraws=0;
            var clip=GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix,true)*m_Camera.worldToCameraMatrix;
            foreach(var draw in m_Draws)foreach(var slice in draw.Model.Rotors[draw.Rotor].Slices)
            {
                m_Block.Clear();m_Block.SetMatrix("_RotorLocalToClip",clip*draw.Matrix);m_Block.SetColor("_RotorTint",draw.Tint.linear);
                m_Block.SetFloat("_RotorAmount",draw.Amount);m_Block.SetFloat("_RotorSweep",draw.Sweep);m_Block.SetFloat("_RotorRadius",slice.Radius);
                int submesh=HelicopterRotorExposureRules.DrawSubmesh(draw.Amount);
                ctx.cmd.DrawMesh(slice.Mesh,Matrix4x4.identity,draw.Model.Materials[slice.Surface],submesh,0,m_Block);
                LastDrawCalls++;LastDrawTriangles+=slice.Mesh.GetIndexCount(submesh)>int.MaxValue?0:(int)slice.Mesh.GetIndexCount(submesh)/3;
                if(submesh==0)angularDraws++;else clearDraws++;
            }
            if(angularDraws>0&&!m_DrawLogged){m_DrawLogged=true;HelicopterRuntime.log.Info("Mi-26 angular real-blade shutter executed on main camera; "+HelicopterRotorExposureRules.Samples+" curved samples, no native velocity reblur.");}
            if(clearDraws>0&&!m_ClearDrawLogged){m_ClearDrawLogged=true;HelicopterRuntime.log.Info("Mi-26 current real blades rendered after postprocess; native motion blur bypassed for rotors only, global graphics unchanged.");}
            m_LastPass=UnityEngine.Time.frameCount;
            }
            catch(Exception e){m_Failed=true;m_LastPass=-1;HelicopterRuntime.log.Error(e,"Mi-26 angular pass failed; restoring native real blades");}
        }
    }
}
