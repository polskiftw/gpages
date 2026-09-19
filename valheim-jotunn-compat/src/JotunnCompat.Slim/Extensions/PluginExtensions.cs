using System;
using System.Linq;
using BepInEx;
using Jotunn.Utils;

namespace Jotunn.Extensions
{
    internal static class PluginExtensions
    {
        internal static NetworkCompatibilityAttribute GetNetworkCompatibilityAttribute(this BaseUnityPlugin plugin)
        {
            return plugin.GetType()
                .GetCustomAttributes(typeof(NetworkCompatibilityAttribute), true)
                .Cast<NetworkCompatibilityAttribute>()
                .FirstOrDefault();
        }

        internal static SynchronizationModeAttribute GetSynchronizationModeAttribute(this BaseUnityPlugin plugin)
        {
            return plugin.GetType()
                .GetCustomAttributes(typeof(SynchronizationModeAttribute), true)
                .Cast<SynchronizationModeAttribute>()
                .FirstOrDefault();
        }
    }
}
