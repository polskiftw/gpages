using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GLoader
{
    internal static class TerrariaStartupState
    {
        public static void Prepare(Assembly gameAssembly, IReadOnlyList<string> gameArguments)
        {
            var programType = gameAssembly.GetType("Terraria.Program", throwOnError: false);
            if (programType == null)
            {
                return;
            }

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var savePath = ResolveSavePath(gameArguments);

            // Vanilla Terraria 1.4.x exposes Program.SavePath as a static field.
            // Keep property support as a fallback for alternate/future builds.
            var savePathField = programType.GetField("SavePath", flags);
            if (savePathField != null && savePathField.FieldType == typeof(string))
            {
                var existing = savePathField.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return;
                }

                if (savePathField.IsInitOnly)
                {
                    throw new InvalidOperationException(
                        "Terraria.Program.SavePath is empty and readonly; gloader could not initialize it before loading mods.");
                }

                savePathField.SetValue(null, savePath);
                var confirmed = savePathField.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(confirmed))
                {
                    throw new InvalidOperationException(
                        "Terraria.Program.SavePath remained empty after gloader initialized startup state.");
                }

                Log.Info("Prepared Terraria save path before mod initialization: " + confirmed);
                return;
            }

            var savePathProperty = programType.GetProperty("SavePath", flags);
            if (savePathProperty == null || savePathProperty.PropertyType != typeof(string))
            {
                return;
            }

            var propertyExisting = savePathProperty.GetValue(null, null) as string;
            if (!string.IsNullOrWhiteSpace(propertyExisting))
            {
                return;
            }

            var setter = savePathProperty.GetSetMethod(nonPublic: true);
            if (setter == null)
            {
                throw new InvalidOperationException(
                    "Terraria.Program.SavePath is empty and gloader could not initialize it before loading mods.");
            }

            setter.Invoke(null, new object[] { savePath });

            var propertyConfirmed = savePathProperty.GetValue(null, null) as string;
            if (string.IsNullOrWhiteSpace(propertyConfirmed))
            {
                throw new InvalidOperationException(
                    "Terraria.Program.SavePath remained empty after gloader initialized startup state.");
            }

            Log.Info("Prepared Terraria save path before mod initialization: " + propertyConfirmed);
        }

        private static string ResolveSavePath(IReadOnlyList<string> gameArguments)
        {
            for (var i = 0; i < gameArguments.Count; i++)
            {
                var argument = gameArguments[i];
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                if (argument.Equals("-savedirectory", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= gameArguments.Count || string.IsNullOrWhiteSpace(gameArguments[i + 1]))
                    {
                        throw new ArgumentException("Terraria -savedirectory requires a path.");
                    }

                    return Path.GetFullPath(Unquote(gameArguments[i + 1]));
                }

                const string equalsPrefix = "-savedirectory=";
                if (argument.StartsWith(equalsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = Unquote(argument.Substring(equalsPrefix.Length));
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentException("Terraria -savedirectory requires a path.");
                    }

                    return Path.GetFullPath(value);
                }
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
            {
                throw new InvalidOperationException("Windows did not provide a Documents folder for Terraria saves.");
            }

            return Path.Combine(documents, "My Games", "Terraria");
        }

        private static string Unquote(string value)
        {
            return value == null ? null : value.Trim().Trim('"');
        }
    }
}
