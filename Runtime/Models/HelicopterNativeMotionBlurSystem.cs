using Game;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CityWeaver.HelicopterModels
{
    // Diagnostics only: native ObjectInterpolate/ProceduralUpload own the real
    // rotor angles and previous-frame matrices. Inspect the actual HDCamera
    // stack; VolumeManager's default stack is not the game camera's stack.
    public partial class HelicopterNativeMotionBlurSystem:GameSystemBase
    {
        public bool RealBladeMode=true;
        public int ManagedRenders;
        public bool RotorMaterialHasMotionVectorPass;
        public bool MotionVectorsEnabled;
        public bool BoneHistoryEnabled;
        public bool QualityEnabled;
        public bool VolumeActive;
        public float EffectiveIntensity;
        public int QualitySampleCount;
        public bool CameraMotionBlurEnabled,CameraObjectMotionVectorsEnabled,CameraCustomPassEnabled;
        private float m_NextCheck;
        private string m_Signature;
        internal void ResetForLoad(){ManagedRenders=0;RotorMaterialHasMotionVectorPass=false;m_Signature=null;m_NextCheck=0;}
        internal void Track(RenderPrefab render)
        {
            var material=render.GetSurfaceAsset(0)?.GetTemplateMaterial();
            bool supported=material!=null&&material.FindPass("MotionVectors")>=0;
            RotorMaterialHasMotionVectorPass=ManagedRenders==0?supported:RotorMaterialHasMotionVectorPass&&supported;
            ManagedRenders++;
        }
        protected override void OnUpdate()
        {
            var game=GameManager.instance;
            if(game==null||game.isGameLoading||game.gameMode!=GameMode.Game||UnityEngine.Time.unscaledTime<m_NextCheck)return;
            m_NextCheck=UnityEngine.Time.unscaledTime+1;
            MotionVectorsEnabled=World.GetExistingSystemManaged<RenderingSystem>()?.motionVectors??false;
            BoneHistoryEnabled=World.GetExistingSystemManaged<ProceduralSkeletonSystem>()?.isMotionBlurEnabled??false;
            var quality=SharedSettings.instance?.graphics?.GetQualitySetting<MotionBlurQualitySettings>();
            QualityEnabled=quality?.enabled??false;
            var camera=World.GetExistingSystemManaged<CameraUpdateSystem>()?.activeCamera;
            var hd=camera!=null?HDCamera.GetOrCreate(camera):null;
            var effect=hd?.volumeStack?.GetComponent<MotionBlur>();
            CameraMotionBlurEnabled=hd!=null&&hd.frameSettings.IsEnabled(FrameSettingsField.MotionBlur);
            CameraObjectMotionVectorsEnabled=hd!=null&&hd.frameSettings.IsEnabled(FrameSettingsField.ObjectMotionVectors);
            CameraCustomPassEnabled=hd!=null&&hd.frameSettings.IsEnabled(FrameSettingsField.CustomPass);
            VolumeActive=effect!=null&&effect.active;
            EffectiveIntensity=effect?.intensity.value??0;
            QualitySampleCount=quality?.sampleCount??0;
            string signature=QualityEnabled+":"+EffectiveIntensity+":"+CameraCustomPassEnabled+":"+CameraMotionBlurEnabled;
            if(ManagedRenders>0&&signature!=m_Signature)
            {
                HelicopterRuntime.log.Info("Mi-26 native motion blur: real blades only; vectors="+MotionVectorsEnabled+
                    ", bone history="+BoneHistoryEnabled+", material pass="+RotorMaterialHasMotionVectorPass+
                    ", graphics enabled="+QualityEnabled+", camera volume intensity="+EffectiveIntensity+
                    ", camera motion blur="+CameraMotionBlurEnabled+", object vectors="+CameraObjectMotionVectorsEnabled+", custom pass="+CameraCustomPassEnabled+
                    ". Angular rotor sampling is independent of global motion blur; no global settings were changed.");
                m_Signature=signature;
            }
        }
    }
}
