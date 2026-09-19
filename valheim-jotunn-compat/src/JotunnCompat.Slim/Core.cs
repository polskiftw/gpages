using BepInEx.Logging;

namespace Jotunn
{
    public static class Logger
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(Main.ModName);
        public static void LogDebug(object value) => Log.LogDebug(value);
        public static void LogInfo(object value) => Log.LogInfo(value);
        public static void LogMessage(object value) => Log.LogMessage(value);
        public static void LogWarning(object value) => Log.LogWarning(value);
        public static void LogError(object value) => Log.LogError(value);
    }
}
