using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace StopYap;

public class Mod : IPuckMod
{
    public const string NAME = "StopYap";
    public const string GUID = $"0x57696c6c.{NAME}";
    public const string VERSION = "2.0.1";

    private readonly Harmony harmony = new Harmony(GUID);

    public bool OnEnable()
    {
        if (IsDedicatedServer())
        {
            LogWarn("This is a client-side mod.");
            return false;
        }
        harmony.PatchAll();
        LogPatchedMethods();
        return true;
    }

    public bool OnDisable()
    {
        harmony.UnpatchSelf();
        return true;
    }

    public static bool IsDedicatedServer()
    {
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    public void LogPatchedMethods()
    {
        LogDebug("Patched these methods:");
        foreach (var method in harmony.GetPatchedMethods())
        {
            if (method.DeclaringType != null) LogDebug($" - {method.DeclaringType.FullName}.{method.Name}");
        }
    }

    // Logging wrappers that prefix messages with the GUID.

    public static void LogDebug(object message)
    {
        Debug.Log($"[{GUID}] {message}");
    }

    public static void LogWarn(object message)
    {
        Debug.LogWarning($"[{GUID}] {message}");
    }

    public static void LogError(object message)
    {
        Debug.LogError($"[{GUID}] {message}");
    }
}