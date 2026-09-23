using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GLoader
{
    internal static class ConsoleManager
    {
        private const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        public static bool HasConsole => GetConsoleWindow() != IntPtr.Zero;

        public static void DetachForGui()
        {
            if (HasConsole)
            {
                FreeConsole();
            }
        }

        public static void EnsureConsole()
        {
            if (!HasConsole)
            {
                if (!AttachConsole(AttachParentProcess) && !AllocConsole())
                {
                    return;
                }
            }

            RebindStandardStreams();
        }

        private static void RebindStandardStreams()
        {
            try
            {
                var output = Console.OpenStandardOutput();
                Console.SetOut(new StreamWriter(output) { AutoFlush = true });
            }
            catch
            {
                // Console output is a debugging convenience, never a startup requirement.
            }

            try
            {
                var error = Console.OpenStandardError();
                Console.SetError(new StreamWriter(error) { AutoFlush = true });
            }
            catch
            {
                // Console output is a debugging convenience, never a startup requirement.
            }
        }
    }
}
