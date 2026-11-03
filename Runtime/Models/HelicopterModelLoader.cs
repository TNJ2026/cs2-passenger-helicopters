using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CityWeaver.HelicopterModels
{
    internal sealed class HelicopterModelOperation
    {
        internal readonly string Id=Guid.NewGuid().ToString("N");
        internal int RouteCacheHits,RouteCacheWrites;
        internal readonly List<VehicleRouteColorMask> RouteColorMasks=new List<VehicleRouteColorMask>();
    }

    // Value-only model preparation. Native prefab replacement and route repair
    // are owned by Mi26ModelSystem, with no MCP query service or connection.
    internal sealed partial class HelicopterModelLoader
    {
        internal static RenderPrefab Prepare(RenderPrefab source,JObject configuration)
        {
            var operation=new HelicopterModelOperation();
            var clone=(RenderPrefab)source.Clone("PassengerHelicopters_Mi26_"+operation.Id);
            try
            {
                PrepareVehicleRouteColor(operation,clone,configuration);
                ApplyVehicleRouteColors(operation);
                return clone;
            }
            catch
            {
                operation.RouteColorMasks.Clear();
                UnityEngine.Object.Destroy(clone);
                throw;
            }
        }
        private static void VehicleFields(JObject value,params string[] allowed)
        {
            foreach(var property in value.Properties())
                if(!allowed.Contains(property.Name))throw new ArgumentException("Unknown route color field: "+property.Name);
        }
        private static float VehicleNumber(JObject value,string key,float fallback,float minimum,float maximum)
        {
            float number=(float?)value[key]??fallback;
            if(float.IsNaN(number)||float.IsInfinity(number)||number<minimum||number>maximum)
                throw new ArgumentException("Invalid route color value: "+key);
            return number;
        }
        private static Texture2D ReadableTexture(Texture2D source)
        {
            if(source.width<1||source.height<1||(long)source.width*source.height>16777216)
                throw new ArgumentException("Mi-26 texture exceeds the pixel budget.");
            bool srgb=UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,srgb?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;bool write=GL.sRGBWrite;Texture2D result=null;
            try
            {
                GL.sRGBWrite=srgb;Graphics.Blit(source,target);RenderTexture.active=target;
                result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,!srgb){wrapMode=source.wrapMode,filterMode=source.filterMode,anisoLevel=source.anisoLevel};
                result.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);result.Apply(false,false);return result;
            }
            catch {if(result!=null)UnityEngine.Object.Destroy(result);throw;}
            finally {RenderTexture.active=previous;GL.sRGBWrite=write;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
