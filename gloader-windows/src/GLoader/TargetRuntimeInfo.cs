using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GLoader
{
    internal sealed class TargetRuntimeInfo
    {
        private TargetRuntimeInfo(
            bool hasMetadata,
            bool requires32Bit,
            bool prefers32Bit,
            bool usesLegacyXna,
            bool referencesFna,
            bool referencesSystemRuntime,
            bool referencesMscorlib,
            Machine machine,
            IReadOnlyList<string> assemblyReferences)
        {
            HasMetadata = hasMetadata;
            Requires32Bit = requires32Bit;
            Prefers32Bit = prefers32Bit;
            UsesLegacyXna = usesLegacyXna;
            ReferencesFna = referencesFna;
            ReferencesSystemRuntime = referencesSystemRuntime;
            ReferencesMscorlib = referencesMscorlib;
            Machine = machine;
            AssemblyReferences = assemblyReferences;
        }

        public bool HasMetadata { get; }
        public bool Requires32Bit { get; }
        public bool Prefers32Bit { get; }
        public bool UsesLegacyXna { get; }
        public bool ReferencesFna { get; }
        public bool ReferencesSystemRuntime { get; }
        public bool ReferencesMscorlib { get; }
        public Machine Machine { get; }
        public IReadOnlyList<string> AssemblyReferences { get; }

        public bool IsModernCoreClr =>
            HasMetadata &&
            !Requires32Bit &&
            !UsesLegacyXna &&
            ReferencesSystemRuntime &&
            !ReferencesMscorlib;

        public bool IsLegacyVanilla => Requires32Bit || UsesLegacyXna || ReferencesMscorlib;

        public string Description
        {
            get
            {
                if (!HasMetadata)
                    return "native/non-managed PE";
                if (UsesLegacyXna)
                    return Requires32Bit ? "legacy 32-bit XNA" : "legacy XNA";
                if (IsModernCoreClr && ReferencesFna)
                    return "64-bit CoreCLR/FNA";
                if (IsModernCoreClr)
                    return "64-bit CoreCLR";
                if (Requires32Bit)
                    return "32-bit managed";
                return "managed (unknown runtime family)";
            }
        }

        public static TargetRuntimeInfo Inspect(string path)
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);

            if (!peReader.HasMetadata)
            {
                return new TargetRuntimeInfo(
                    hasMetadata: false,
                    requires32Bit: false,
                    prefers32Bit: false,
                    usesLegacyXna: false,
                    referencesFna: false,
                    referencesSystemRuntime: false,
                    referencesMscorlib: false,
                    machine: peReader.PEHeaders.CoffHeader.Machine,
                    assemblyReferences: Array.Empty<string>());
            }

            var metadata = peReader.GetMetadataReader();
            var references = new List<string>();

            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                references.Add(metadata.GetString(reference.Name));
            }

            var corFlags = peReader.PEHeaders.CorHeader?.Flags ?? (CorFlags)0;
            var requires32Bit = (corFlags & CorFlags.Requires32Bit) != 0;
            var prefers32Bit = (corFlags & CorFlags.Prefers32Bit) != 0;
            var usesLegacyXna = references.Any(name =>
                name.StartsWith("Microsoft.Xna.Framework", StringComparison.OrdinalIgnoreCase));
            var referencesFna = references.Any(name =>
                string.Equals(name, "FNA", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("FNA.", StringComparison.OrdinalIgnoreCase));
            var referencesSystemRuntime = references.Any(name =>
                string.Equals(name, "System.Runtime", StringComparison.OrdinalIgnoreCase));
            var referencesMscorlib = references.Any(name =>
                string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase));

            return new TargetRuntimeInfo(
                hasMetadata: true,
                requires32Bit: requires32Bit,
                prefers32Bit: prefers32Bit,
                usesLegacyXna: usesLegacyXna,
                referencesFna: referencesFna,
                referencesSystemRuntime: referencesSystemRuntime,
                referencesMscorlib: referencesMscorlib,
                machine: peReader.PEHeaders.CoffHeader.Machine,
                assemblyReferences: references.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }
}
