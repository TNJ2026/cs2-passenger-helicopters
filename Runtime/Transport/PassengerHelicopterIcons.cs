using System;
using System.IO;
using Colossal.UI;
using UnityEngine;

namespace CityWeaver
{
    internal static class PassengerHelicopterIcons
    {
        private const string Host = "cityweaver-icons";
        internal const string Helicopter = "coui://cityweaver-icons/helicopter.svg";
        internal const string Airport = "coui://cityweaver-icons/helicopter-airport-menu.png";
        internal const string Station = "coui://cityweaver-icons/helicopter-station-menu.png";
        internal const string Line = "coui://cityweaver-icons/helicopter-line.svg";
        private static string s_Path;

        // World-space notifications use a native texture array, not a COUI URL.
        // Match the template's dimensions, compression and mip chain so adding
        // this icon cannot change the format of every notification in the city.
        internal static Texture2D CreateStopMarker(Texture2D template)
        {
            Texture2D source = null, result = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                using (var input = typeof(PassengerHelicopterIcons).Assembly.GetManifestResourceStream("CityWeaver.Icons.helicopter-stop-marker.png"))
                using (var bytes = new MemoryStream())
                {
                    if (input == null) throw new InvalidOperationException("Missing helicopter stop marker");
                    input.CopyTo(bytes);
                    source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(source, bytes.ToArray())) throw new InvalidOperationException("Invalid helicopter stop marker");
                }
                var format = template.format;
                if (format != TextureFormat.DXT1 && format != TextureFormat.DXT5 &&
                    format != TextureFormat.RGBA32 && format != TextureFormat.RGB24)
                    throw new InvalidOperationException("Unsupported notification texture format: " + format);
                target = RenderTexture.GetTemporary(template.width, template.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                result = new Texture2D(template.width, template.height,
                    format == TextureFormat.DXT1 || format == TextureFormat.RGB24 ? TextureFormat.RGB24 : TextureFormat.RGBA32,
                    template.mipmapCount > 1);
                result.ReadPixels(new Rect(0, 0, template.width, template.height), 0, 0);
                result.Apply(true, false);
                if (format == TextureFormat.DXT1 || format == TextureFormat.DXT5) result.Compress(true);
                if (result.format != format || result.mipmapCount != template.mipmapCount)
                    throw new InvalidOperationException("Helicopter marker texture array layout mismatch");
                result.name = "CityWeaver Helicopter Stop Marker";
                result.filterMode = template.filterMode;
                result.wrapMode = template.wrapMode;
                return result;
            }
            catch
            {
                if (result != null) UnityEngine.Object.Destroy(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (source != null) UnityEngine.Object.Destroy(source);
            }
        }

        internal static void Register()
        {
            var assembly = typeof(PassengerHelicopterIcons).Assembly;
            s_Path = Path.Combine(Application.persistentDataPath, "PassengerHelicopters", "UI", "1.0.0");
            Directory.CreateDirectory(s_Path);
            foreach (var name in new[] { "helicopter.svg", "helicopter-airport-menu.png", "helicopter-station-menu.png", "helicopter-line.svg" })
            {
                using (var input = assembly.GetManifestResourceStream("CityWeaver.Icons." + name))
                {
                    if (input == null) throw new InvalidOperationException("Missing bundled UI icon: " + name);
                    using (var output = File.Create(Path.Combine(s_Path, name))) input.CopyTo(output);
                }
            }
            UIManager.defaultUISystem.AddHostLocation(Host, s_Path, false);
        }

        internal static void Unregister()
        {
            if (s_Path != null && UIManager.defaultUISystem != null)
                UIManager.defaultUISystem.RemoveHostLocation(Host, s_Path);
            s_Path = null;
        }

        internal static string ForPrefab(string name)
        {
            if (name == PassengerHelicopterSystem.Category || name == PassengerHelicopterSystem.Vehicle) return Helicopter;
            if (name == PassengerHelicopterSystem.Airport || name == "CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0") return Airport;
            if (name == PassengerHelicopterSystem.Station || name == ImportedHelipadMenuSystem.Station) return Station;
            if (name == PassengerHelicopterSystem.Line) return Line;
            return null;
        }
    }
}
