using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;

namespace SharedDay.Mod;

/// <summary>Snapshots Junimo's crop records into the same save as a raw checkpoint.</summary>
internal static class JunimoCheckpointBridge
{
    internal static void CaptureForCheckpoint(IModHelper helper)
    {
        if (!helper.ModRegistry.IsLoaded("JunimoHost.Server")) return;

        // Pinned Junimo 1.4.1 exposes these accessors. Invoke its own persistence routine
        // with its own helper, preserving its SMAPI save-data key and serialization.
        // Do not raise arbitrary Saving events or run CropSaver's overnight mutations.
        var type = AccessTools.TypeByName("JunimoServer.Services.CropSaver.CropSaver")
            ?? throw new MissingMemberException("JunimoServer's CropSaver type changed; its records cannot be checkpointed.");
        var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(type.FullName, "Instance");
        var instance = instanceProperty.GetValue(null)
            ?? throw new InvalidOperationException("JunimoServer's CropSaver has not initialized.");
        var loaderProperty = type.GetProperty("DataLoader", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException(type.FullName, "DataLoader");
        var loader = loaderProperty.GetValue(instance)
            ?? throw new InvalidOperationException("JunimoServer's CropSaver data loader is unavailable.");
        var save = loader.GetType().GetMethod("SaveDataToDisk", BindingFlags.Public | BindingFlags.Instance,
            binder: null, types: Type.EmptyTypes, modifiers: null)
            ?? throw new MissingMethodException(loader.GetType().FullName, "SaveDataToDisk");
        if (save.ReturnType != typeof(void))
            throw new InvalidOperationException("JunimoServer's CropSaver save contract changed.");

        try { save.Invoke(loader, null); }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException("JunimoServer crop records could not be included in this checkpoint.", ex.InnerException ?? ex);
        }
    }
}
