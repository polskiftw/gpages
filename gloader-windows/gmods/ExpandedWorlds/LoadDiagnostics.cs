#if GLOADER
// Runtime diagnostics are written to gdeps\logs\expanded-worlds-load.log.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Terraria;

internal static class ExpandedWorldLoadDiagnostics
{
    private const string WorldFileTypeName = "Terraria.IO.WorldFile";
    private static readonly object LogLock = new object();
    private static readonly Type WorldFileType = AccessTools.TypeByName(WorldFileTypeName);
    private static readonly FieldInfo LastThrownLoadExceptionField =
        WorldFileType == null ? null : AccessTools.Field(WorldFileType, "LastThrownLoadException");
    private static readonly FieldInfo LoadFailedField = AccessTools.Field(typeof(WorldGen), "loadFailed");
    private static readonly FieldInfo WorldPathNameField = AccessTools.Field(typeof(Main), "worldPathName");
    private static readonly FieldInfo MainMapField = AccessTools.Field(typeof(Main), "Map");

    public static MethodBase RequireLoadWorldMethod()
    {
        if (WorldFileType == null)
            throw new TypeLoadException("[Expanded Worlds] Terraria.IO.WorldFile was not found for load diagnostics.");

        MethodInfo method = AccessTools.Method(WorldFileType, "LoadWorld", Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(WorldFileType.FullName, "LoadWorld()");

        return method;
    }

    public static bool IsExpandedCurrentWorld()
    {
        return ExpandedWorldMath.IsExpandedPresetDimensions(Main.maxTilesX, Main.maxTilesY);
    }

    public static bool GetLoadFailed()
    {
        try
        {
            return LoadFailedField != null && (bool)LoadFailedField.GetValue(null);
        }
        catch
        {
            return false;
        }
    }

    public static Exception GetLastThrownLoadException()
    {
        try
        {
            return LastThrownLoadExceptionField == null
                ? null
                : LastThrownLoadExceptionField.GetValue(null) as Exception;
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string stage, Exception exception = null)
    {
        try
        {
            string logDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "gdeps",
                "logs");
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "expanded-worlds-load.log");

            lock (LogLock)
            {
                using (var stream = new FileStream(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream))
                {
                    writer.AutoFlush = true;
                    writer.WriteLine("=== {0} | {1:O} ===", stage, DateTime.Now);
                    writer.WriteLine("Process architecture: {0}-bit", Environment.Is64BitProcess ? 64 : 32);
                    writer.WriteLine("World path: {0}", GetWorldPath());
                    writer.WriteLine(
                        "Logical dimensions: {0}x{1}; sections: {2}x{3}",
                        Main.maxTilesX,
                        Main.maxTilesY,
                        Main.maxSectionsX,
                        Main.maxSectionsY);
                    WriteTileStorage(writer);
                    WriteMapStorage(writer);
                    WriteProcessMemory(writer);
                    writer.WriteLine("WorldGen.loadFailed: {0}", GetLoadFailed());

                    if (exception != null)
                        WriteException(writer, exception);
                    else
                        writer.WriteLine("Exception: <none>");

                    writer.WriteLine();
                }
            }
        }
        catch
        {
            // Diagnostics must never alter Terraria's load behavior, especially
            // while recovering from an OutOfMemoryException.
        }
    }

    private static string GetWorldPath()
    {
        try
        {
            object value = WorldPathNameField == null ? null : WorldPathNameField.GetValue(null);
            return value as string ?? "<unavailable>";
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static void WriteTileStorage(StreamWriter writer)
    {
        try
        {
            Tile[,] tiles = Main.tile;
            if (tiles == null)
            {
                writer.WriteLine("Tile backing storage: <null>");
                return;
            }

            writer.WriteLine(
                "Tile backing storage: {0}x{1}; slots: {2:N0}",
                tiles.GetLength(0),
                tiles.GetLength(1),
                tiles.LongLength);
        }
        catch (Exception ex)
        {
            writer.WriteLine("Tile backing storage: <error: {0}>", ex.GetType().FullName);
        }
    }

    private static void WriteMapStorage(StreamWriter writer)
    {
        try
        {
            if (MainMapField == null)
            {
                writer.WriteLine("Map backing storage: <field unavailable>");
                return;
            }

            object map = MainMapField.GetValue(null);
            if (map == null)
            {
                writer.WriteLine("Map backing storage: <null>");
                return;
            }

            FieldInfo maxWidth = AccessTools.Field(map.GetType(), "MaxWidth");
            FieldInfo maxHeight = AccessTools.Field(map.GetType(), "MaxHeight");
            if (maxWidth == null || maxHeight == null)
            {
                writer.WriteLine("Map backing storage: <dimension fields unavailable>");
                return;
            }

            writer.WriteLine(
                "Map backing storage: {0}x{1}",
                maxWidth.GetValue(map),
                maxHeight.GetValue(map));
        }
        catch (Exception ex)
        {
            writer.WriteLine("Map backing storage: <error: {0}>", ex.GetType().FullName);
        }
    }

    private static void WriteProcessMemory(StreamWriter writer)
    {
        try
        {
            using (Process process = Process.GetCurrentProcess())
            {
                writer.WriteLine("Process private bytes: {0} ({1:F1} MiB)",
                    process.PrivateMemorySize64,
                    process.PrivateMemorySize64 / 1048576d);
                writer.WriteLine("Process working set: {0} ({1:F1} MiB)",
                    process.WorkingSet64,
                    process.WorkingSet64 / 1048576d);
                writer.WriteLine("Process virtual bytes: {0} ({1:F1} MiB)",
                    process.VirtualMemorySize64,
                    process.VirtualMemorySize64 / 1048576d);
            }
        }
        catch (Exception ex)
        {
            writer.WriteLine("Process memory: <error: {0}>", ex.GetType().FullName);
        }

        try
        {
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            writer.WriteLine("Managed GC bytes: {0} ({1:F1} MiB)", managed, managed / 1048576d);
            writer.WriteLine(
                "GC collections: gen0={0}, gen1={1}, gen2={2}",
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
        catch (Exception ex)
        {
            writer.WriteLine("Managed memory: <error: {0}>", ex.GetType().FullName);
        }
    }

    private static void WriteException(StreamWriter writer, Exception exception)
    {
        try
        {
            int depth = 0;
            Exception current = exception;
            while (current != null && depth < 8)
            {
                writer.WriteLine(
                    depth == 0 ? "Exception type: {0}" : "Inner exception type: {0}",
                    current.GetType().FullName);
                writer.WriteLine("HResult: 0x{0:X8}", current.HResult);
                writer.WriteLine("Message: {0}", current.Message ?? string.Empty);
                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    writer.WriteLine("Stack trace:");
                    writer.WriteLine(current.StackTrace);
                }

                current = current.InnerException;
                depth++;
            }
        }
        catch
        {
            writer.WriteLine("Exception details could not be fully rendered.");
        }
    }
}

/// <summary>
/// Terraria 1.4.5.8 catches world-load exceptions internally and only exposes
/// them through WorldFile.LastThrownLoadException + WorldGen.loadFailed. Capture
/// that otherwise-hidden exception, along with the physical world backing sizes
/// and process memory state, after every failed load.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldLoadDiagnosticsPatch
{
    private static MethodBase TargetMethod()
    {
        return ExpandedWorldLoadDiagnostics.RequireLoadWorldMethod();
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        ExpandedWorldLoadDiagnostics.Write("WorldFile.LoadWorld BEGIN");
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (ExpandedWorldLoadDiagnostics.GetLoadFailed())
        {
            ExpandedWorldLoadDiagnostics.Write(
                "WorldFile.LoadWorld FAILED",
                ExpandedWorldLoadDiagnostics.GetLastThrownLoadException());
            return;
        }

        if (ExpandedWorldLoadDiagnostics.IsExpandedCurrentWorld())
            ExpandedWorldLoadDiagnostics.Write("WorldFile.LoadWorld SUCCESS");
    }
}

/// <summary>
/// Record the exact expanded clearWorld allocation boundary as well. This
/// separates backing-storage/allocation failures from later .wld decoding
/// problems. The finalizer always returns the original exception unchanged.
/// </summary>
[HarmonyPatch(typeof(WorldGen), "clearWorld")]
internal static class ExpandedWorldClearWorldDiagnosticsPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref bool __state)
    {
        __state = ExpandedWorldLoadDiagnostics.IsExpandedCurrentWorld();
        if (__state)
            ExpandedWorldLoadDiagnostics.Write("WorldGen.clearWorld BEGIN");
    }

    [HarmonyPostfix]
    private static void Postfix(bool __state)
    {
        if (__state)
            ExpandedWorldLoadDiagnostics.Write("WorldGen.clearWorld SUCCESS");
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state && __exception != null)
            ExpandedWorldLoadDiagnostics.Write("WorldGen.clearWorld EXCEPTION", __exception);

        return __exception;
    }
}
#endif
