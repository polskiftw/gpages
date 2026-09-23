using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace GLoader
{
    internal static class HostPlayServerRedirect
    {
        private const string HarmonyId = "gloader.core.hostplay-server";
        private static string _loaderPath;
        private static string _modsDirectory;
        private static string _serverTargetOverride;
        private static bool _installed;

        public static void Install(
            Assembly gameAssembly,
            string loaderPath,
            string modsDirectory,
            string serverTargetOverride = null)
        {
            if (_installed)
                return;

            if (gameAssembly == null)
                throw new ArgumentNullException(nameof(gameAssembly));

            _loaderPath = Path.GetFullPath(loaderPath);
            _modsDirectory = Path.GetFullPath(modsDirectory);
            _serverTargetOverride = string.IsNullOrWhiteSpace(serverTargetOverride)
                ? null
                : Path.GetFullPath(serverTargetOverride);

            var launcher = FindServerLauncher(gameAssembly);
            if (launcher == null)
                throw new MissingMethodException("Could not locate Terraria's Host & Play server launcher.");

            var transpiler = typeof(HostPlayServerRedirect).GetMethod(
                nameof(ServerLaunchTranspiler),
                BindingFlags.NonPublic | BindingFlags.Static);

            new Harmony(HarmonyId).Patch(
                launcher,
                transpiler: new HarmonyMethod(transpiler));

            _installed = true;
            Log.Info("Host & Play server redirect enabled.");
        }

        private static MethodInfo FindServerLauncher(Assembly gameAssembly)
        {
            var mainType = gameAssembly.GetType("Terraria.Main", throwOnError: false);
            if (mainType == null)
                return null;

            var candidates = mainType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(method => MethodContainsString(method, "TerrariaServer.exe"))
                .ToArray();

            if (candidates.Length == 1)
                return candidates[0];

            return candidates.FirstOrDefault(method => MethodContainsString(method, " -hosttoken "))
                ?? candidates.FirstOrDefault();
        }

        private static bool MethodContainsString(MethodInfo method, string expected)
        {
            MethodBody body;
            try
            {
                body = method.GetMethodBody();
            }
            catch
            {
                return false;
            }

            var il = body?.GetILAsByteArray();
            if (il == null || il.Length < 5)
                return false;

            for (var index = 0; index <= il.Length - 5; index++)
            {
                if (il[index] != 0x72)
                    continue;

                try
                {
                    var token = BitConverter.ToInt32(il, index + 1);
                    if (string.Equals(method.Module.ResolveString(token), expected, StringComparison.Ordinal))
                        return true;
                }
                catch
                {
                    // The byte can occur inside another instruction's operand. Keep scanning.
                }

                index += 4;
            }

            return false;
        }

        private static IEnumerable<CodeInstruction> ServerLaunchTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var argumentsSetter = typeof(ProcessStartInfo)
                .GetProperty(nameof(ProcessStartInfo.Arguments))
                ?.GetSetMethod();
            var argumentsRedirect = typeof(HostPlayServerRedirect).GetMethod(
                nameof(SetArgumentsAndRedirect),
                BindingFlags.NonPublic | BindingFlags.Static);

            var instanceStart = typeof(Process).GetMethod(
                nameof(Process.Start),
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            var staticStart = typeof(Process).GetMethod(
                nameof(Process.Start),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ProcessStartInfo) },
                modifiers: null);
            var instanceRedirect = typeof(HostPlayServerRedirect).GetMethod(
                nameof(StartAndRedirect),
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Process) },
                modifiers: null);
            var staticRedirect = typeof(HostPlayServerRedirect).GetMethod(
                nameof(StartAndRedirect),
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ProcessStartInfo) },
                modifiers: null);

            if (argumentsSetter == null || argumentsRedirect == null ||
                instanceStart == null || staticStart == null ||
                instanceRedirect == null || staticRedirect == null)
            {
                throw new MissingMethodException("Could not build Host & Play redirect patch.");
            }

            var preparationReplacements = 0;
            var startReplacements = 0;

            foreach (var instruction in instructions)
            {
                if (instruction.Calls(argumentsSetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = argumentsRedirect;
                    preparationReplacements++;
                }
                else if (instruction.Calls(instanceStart))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = instanceRedirect;
                    startReplacements++;
                }
                else if (instruction.Calls(staticStart))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = staticRedirect;
                    startReplacements++;
                }

                yield return instruction;
            }

            if (preparationReplacements == 0 && startReplacements == 0)
            {
                throw new InvalidOperationException(
                    "Terraria's Host & Play launcher no longer prepares or starts a Process as expected.");
            }
        }

        private static void SetArgumentsAndRedirect(ProcessStartInfo startInfo, string arguments)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));

            startInfo.Arguments = arguments ?? string.Empty;
            RedirectIfTerrariaServer(startInfo);
        }

        private static bool StartAndRedirect(Process process)
        {
            if (process == null)
                throw new ArgumentNullException(nameof(process));

            RedirectIfTerrariaServer(process.StartInfo);
            return process.Start();
        }

        private static Process StartAndRedirect(ProcessStartInfo startInfo)
        {
            RedirectIfTerrariaServer(startInfo);
            return Process.Start(startInfo);
        }

        private static void RedirectIfTerrariaServer(ProcessStartInfo startInfo)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));

            var requestedFile = TrimQuotes(startInfo.FileName);
            if (string.IsNullOrWhiteSpace(requestedFile) ||
                !string.Equals(Path.GetFileName(requestedFile), "TerrariaServer.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                ? Environment.CurrentDirectory
                : startInfo.WorkingDirectory;

            var requestedServerPath = Path.IsPathRooted(requestedFile)
                ? Path.GetFullPath(requestedFile)
                : Path.GetFullPath(Path.Combine(workingDirectory, requestedFile));

            var serverTarget = _serverTargetOverride;
            if (string.IsNullOrWhiteSpace(serverTarget))
            {
                var managedSibling = Path.ChangeExtension(requestedServerPath, ".dll");
                if (File.Exists(managedSibling))
                    serverTarget = managedSibling;
                else if (File.Exists(requestedServerPath))
                    serverTarget = requestedServerPath;
                else
                {
                    Log.Warn("Host & Play requested TerrariaServer.exe, but no managed server target exists at: " + requestedServerPath);
                    return;
                }
            }

            var originalArguments = startInfo.Arguments ?? string.Empty;
            startInfo.FileName = _loaderPath;
            startInfo.Arguments =
                "--server --target " + Quote(serverTarget) +
                " --mods " + Quote(_modsDirectory) +
                " --" +
                (string.IsNullOrWhiteSpace(originalArguments) ? string.Empty : " " + originalArguments);

            Log.Info("Routing Host & Play server through gloader x64: " + serverTarget);
        }

        private static string TrimQuotes(string value)
        {
            return value?.Trim().Trim('"');
        }

        private static string Quote(string value)
        {
            if (value == null)
                return "\"\"";

            var builder = new System.Text.StringBuilder();
            builder.Append('"');
            var backslashes = 0;

            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
