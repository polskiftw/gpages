using System;
using System.Collections.Generic;
using System.IO;

namespace GLoader
{
    internal sealed class LoaderOptions
    {
        public string TargetPath { get; private set; }
        public string ModsPath { get; private set; }
        public bool DedicatedServer { get; private set; }
        public bool DisableMods { get; private set; }
        public bool ShowHelp { get; private set; }
        public bool DirectRun { get; private set; }
        public List<string> GameArguments { get; } = new List<string>();

        public static LoaderOptions Parse(string[] args)
        {
            var result = new LoaderOptions();
            var passThrough = false;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (passThrough)
                {
                    result.GameArguments.Add(arg);
                    continue;
                }

                if (arg == "--")
                {
                    passThrough = true;
                    continue;
                }

                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    result.ShowHelp = true;
                    continue;
                }

                if (arg.Equals("--run", StringComparison.OrdinalIgnoreCase))
                {
                    result.DirectRun = true;
                    continue;
                }

                if (arg.Equals("--server", StringComparison.OrdinalIgnoreCase))
                {
                    result.DedicatedServer = true;
                    continue;
                }

                if (arg.Equals("--no-mods", StringComparison.OrdinalIgnoreCase))
                {
                    result.DisableMods = true;
                    continue;
                }

                if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase))
                {
                    result.TargetPath = RequireValue(args, ref i, "--target");
                    continue;
                }

                if (arg.Equals("--mods", StringComparison.OrdinalIgnoreCase))
                {
                    result.ModsPath = RequireValue(args, ref i, "--mods");
                    continue;
                }

                if (result.TargetPath == null && IsManagedTargetPath(arg))
                {
                    var unquoted = Unquote(arg);
                    if (File.Exists(unquoted))
                    {
                        result.TargetPath = unquoted;
                        continue;
                    }
                }

                result.GameArguments.Add(arg);
            }

            return result;
        }

        public void DisableModsForRun()
        {
            DisableMods = true;
        }

        public static void PrintHelp()
        {
            Console.WriteLine("gloader - 64-bit raw C# source mod loader for Terraria");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  gloader.exe                         Open the mod launcher GUI");
            Console.WriteLine("  gloader.exe --run                   Launch directly without the GUI");
            Console.WriteLine("  gloader.exe --target \"C:\\...\\TerrariaRelease.dll\"");
            Console.WriteLine("  gloader.exe --server");
            Console.WriteLine("  gloader.exe --mods \"C:\\...\\gmods\"");
            Console.WriteLine("  gloader.exe --no-mods");
            Console.WriteLine("  gloader.exe -- <arguments passed to Terraria>");
            Console.WriteLine();
            Console.WriteLine("Default x64 runtime: gdeps\\x64-runtime\\TerrariaRelease.dll");
            Console.WriteLine("Stock 32-bit/XNA Terraria.exe is not loaded into the x64 process.");
            Console.WriteLine("gmods contains mod folders; gdeps contains support files, logs, and the x64 runtime.");
        }

        private static string RequireValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException(option + " requires a value.");

            index++;
            return Unquote(args[index]);
        }

        private static bool IsManagedTargetPath(string value)
        {
            var unquoted = Unquote(value);
            return unquoted != null &&
                (unquoted.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                 unquoted.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        }

        private static string Unquote(string value)
        {
            if (value == null)
                return null;

            return value.Trim().Trim('"');
        }
    }
}
