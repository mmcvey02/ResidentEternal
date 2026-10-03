// Compile-only stand-ins for the parts of Cities: Skylines, Unity 5.6, ColossalFramework, ICities, Harmony and
// CitiesHarmony.API that the mod touches. They let CI type-check the whole mod without the game's DLLs. Every member
// here mirrors the real signature as used by published CS1 mods; none of it ships, and none of it runs.
// When the real build (RaccoonCitySkylines.csproj against the game's Managed folder) disagrees, the real one wins:
// fix the mod, then this file.
#pragma warning disable 0067, 0649, 0169
using System;
using System.Collections;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object o) { }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public T GetComponent<T>() where T : Component { return null; }
    }

    public class Behaviour : Component
    {
        public bool enabled;
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator e) { return null; }
        public void StopAllCoroutines() { }
    }

    public class Coroutine { }
    public class YieldInstruction { }
    public sealed class WaitForEndOfFrame : YieldInstruction { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component { return null; }
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main;
        public float fieldOfView, nearClipPlane, farClipPlane;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color Lerp(Color a, Color b, float t) { return a; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float yMax { get { return y + height; } }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Rad2Deg = 57.29578f;
        public static float Clamp01(float v) { return v; }
        public static float Sin(float v) { return 0; }
        public static float Min(float a, float b) { return a; }
        public static int Min(int a, int b) { return a; }
        public static float Max(float a, float b) { return a; }
        public static int Max(int a, int b) { return a; }
        public static int RoundToInt(float v) { return 0; }
    }

    public static class Random
    {
        public static int Range(int min, int max) { return min; }
    }

    public static class Time
    {
        public static float realtimeSinceStartup;
    }

    public static class Screen
    {
        public static int width, height;
    }

    public enum TextureFormat { RGBA32 = 4 }

    public class Texture : Object
    {
        public int width, height;
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mipmap) { }
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps) { }
        public byte[] GetRawTextureData() { return null; }
        public void LoadRawTextureData(byte[] data) { }
        public void Apply(bool updateMipmaps) { }
    }

    public static class GUI
    {
        public static void DrawTextureWithTexCoords(Rect position, Texture image, Rect texCoords) { }
        public static void Label(Rect position, string text) { }
    }

    public enum KeyCode { F9 = 290, F10 = 291, F11 = 292 }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) { return false; }
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
    }
}

namespace ColossalFramework
{
    public class Singleton<T> where T : class
    {
        public static T instance;
        public static bool exists;
    }

    public class Array16<T>
    {
        public T[] m_buffer;
    }

    public class Array32<T>
    {
        public T[] m_buffer;
    }

    public class Array8<T>
    {
        public T[] m_buffer;
    }
}

namespace ColossalFramework.IO
{
    public static class DataLocation
    {
        public static string localApplicationData;
    }
}

namespace ColossalFramework.UI
{
    public class UIView
    {
        public static void Show(bool show) { }
    }
}

namespace ICities
{
    public interface IUserMod
    {
        string Name { get; }
        string Description { get; }
    }

    public enum LoadMode { NewGame, LoadGame, NewMap, LoadMap, NewAsset, LoadAsset, NewTheme, LoadTheme, NewScenarioFromGame, NewScenarioFromMap, LoadScenario, NewGameFromScenario, UpdateScenarioFromGame, UpdateScenarioFromMap }

    public interface ILoadingExtension { }

    public class LoadingExtensionBase : ILoadingExtension
    {
        public virtual void OnLevelLoaded(LoadMode mode) { }
        public virtual void OnLevelUnloading() { }
    }

    public delegate void OnCheckChanged(bool isChecked);
    public delegate void OnValueChanged(float val);
    public delegate void OnTextChanged(string text);
    public delegate void OnTextSubmitted(string text);
    public delegate void OnButtonClicked();

    public interface UIHelperBase
    {
        UIHelperBase AddGroup(string text);
        object AddCheckbox(string text, bool defaultValue, OnCheckChanged eventCallback);
        object AddSlider(string text, float min, float max, float step, float defaultValue, OnValueChanged eventCallback);
        object AddTextfield(string text, string defaultContent, OnTextChanged eventChangedCallback, OnTextSubmitted eventSubmittedCallback = null);
        object AddButton(string text, OnButtonClicked eventCallback);
    }
}

namespace CitiesHarmony.API
{
    public static class HarmonyHelper
    {
        public static bool IsHarmonyInstalled { get { return false; } }
        public static void EnsureHarmonyInstalled() { }
        public static void DoOnHarmonyReady(Action action) { }
    }
}

namespace HarmonyLib
{
    public class Harmony
    {
        public Harmony(string id) { }
        public MethodInfo Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null, HarmonyMethod finalizer = null) { return null; }
        public void UnpatchAll(string harmonyID = null) { }
    }

    public class HarmonyMethod
    {
        public HarmonyMethod(Type type, string name, Type[] argumentTypes = null) { }
    }

    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] parameters = null, Type[] generics = null) { return null; }
    }
}

// Cities: Skylines' own types live in the global namespace (Assembly-CSharp).
public class CameraController : UnityEngine.MonoBehaviour { }

public class ItemClass
{
    public enum Service { None, Residential, Commercial, Industrial, Natural, Unused2, Citizen, Tourism, Office, Road, Electricity, Water, Beautification, Garbage, HealthCare, PoliceDepartment, Education, Monument, FireDepartment, PublicTransport, Disaster }
    public Service m_service;
}

public class PrefabInfo { }

public class BuildingInfo : PrefabInfo
{
    public ItemClass m_class;
}

public struct Building
{
    [Flags]
    public enum Flags : uint { None = 0, Created = 1 }
    public Flags m_flags;
    public UnityEngine.Vector3 m_position;
    public float m_angle;
    public BuildingInfo Info { get { return null; } }
}

public class BuildingManager
{
    public ColossalFramework.Array16<Building> m_buildings;
}

public struct DistrictPrivateData
{
    public uint m_finalCount;
}

public struct District
{
    [Flags]
    public enum Flags : ushort { None = 0, Created = 1 }
    public Flags m_flags;
    public DistrictPrivateData m_populationData;
}

public class DistrictManager
{
    public ColossalFramework.Array8<District> m_districts;
    public byte GetDistrict(UnityEngine.Vector3 worldPos) { return 0; }
    public string GetDistrictName(int district) { return null; }
}

public struct Citizen
{
    [Flags]
    public enum Flags : uint { None = 0, Created = 1 }
    public Flags m_flags;
    public ushort m_homeBuilding;
    public bool Sick { get { return false; } set { } }
    public bool Dead { get { return false; } set { } }
}

public class CitizenManager
{
    public ColossalFramework.Array32<Citizen> m_citizens;
}

public class SimulationMetaData
{
    public string m_CityName;
}

public class SimulationManager
{
    public SimulationMetaData m_metaData;
    public float m_currentDayTimeHour;
    public uint m_currentFrameIndex;
    public bool SimulationPaused { get { return false; } }
    public void AddAction(Action action) { }
}

public class TerrainManager
{
    public float SampleRawHeightSmooth(UnityEngine.Vector3 worldPos) { return 0; }
}

public class ImmaterialResourceManager
{
    public enum Resource { HealthCare, FireDepartment, PoliceDepartment }
    public void CheckLocalResource(Resource resource, UnityEngine.Vector3 position, out int local) { local = 0; }
}

public class ElectricityManager
{
    public bool CheckElectricity(UnityEngine.Vector3 pos) { return true; }
}

public class OverlayEffect
{
    public void DrawCircle(RenderManager.CameraInfo cameraInfo, UnityEngine.Color color, UnityEngine.Vector3 center, float size, float minY, float maxY, bool renderLimits, bool alphaBlend) { }
}

public class RenderManager
{
    public class CameraInfo { }
    public OverlayEffect OverlayEffect;
}

public struct DrawCallData
{
    public int m_overlayCalls;
}

public class ToolManager
{
    public static ToolManager instance;
    public DrawCallData m_drawCallData;
    protected void EndOverlayImpl(RenderManager.CameraInfo cameraInfo) { }
}
