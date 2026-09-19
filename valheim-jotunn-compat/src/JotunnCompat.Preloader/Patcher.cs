using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace JotunnCompat.Preloader
{
    /// <summary>
    /// BepInEx 5 preloader entrypoint.
    ///
    /// The BepInEx 5 patcher contract requires a TargetDLLs getter and Patch method.
    /// We intentionally do not Cecil-patch a game assembly. Initialize runs before the
    /// chainloader, which lets us arm an AssemblyLoad hook before plugin discovery starts.
    /// </summary>
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new string[0];

        public static void Initialize()
        {
            EarlyBootstrap.Arm();
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            // Required by BepInEx 5's patcher discovery contract.
            // Runtime compatibility hooks are installed when Jotunn itself is loaded.
        }
    }
}
