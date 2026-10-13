using Mono.Cecil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: JotunnCompat.HookAudit <Jotunn.dll> <Valheim_Data/Managed>");
    return 2;
}

string slimPath = Path.GetFullPath(args[0]);
string managedPath = Path.GetFullPath(args[1]);
if (!File.Exists(slimPath) || !Directory.Exists(managedPath))
{
    throw new FileNotFoundException("Missing slim Jotunn.dll or Valheim managed references.");
}

using ModuleDefinition jotunn = ModuleDefinition.ReadModule(slimPath);
var gameModules = new List<ModuleDefinition>();
foreach (string path in Directory.EnumerateFiles(managedPath, "*.dll"))
{
    try
    {
        gameModules.Add(ModuleDefinition.ReadModule(
            path, new ReaderParameters { ReadSymbols = false }));
    }
    catch (BadImageFormatException)
    {
        // Native files can live alongside the managed references.
    }
}

int tested = 0;
int warnings = 0;
var failures = new List<string>();
try
{
    var types = gameModules.SelectMany(m => AllTypes(m.Types))
        .Concat(AllTypes(jotunn.Types))
        .GroupBy(t => t.FullName, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    foreach (var patchType in AllTypes(jotunn.Types))
    {
        foreach (MethodDefinition patchMethod in patchType.Methods)
        {
            if (!IsAttributePresent(patchMethod, "HarmonyPrefix") &&
                !IsAttributePresent(patchMethod, "HarmonyPostfix") &&
                !IsAttributePresent(patchMethod, "HarmonyTranspiler") &&
                !IsAttributePresent(patchMethod, "HarmonyFinalizer"))
            {
                continue;
            }

            var patchAttribute = patchMethod.CustomAttributes
                .FirstOrDefault(a => HasAttributeName(a, "HarmonyPatch"))
                ?? patchType.CustomAttributes
                    .FirstOrDefault(a => HasAttributeName(a, "HarmonyPatch"));
            if (patchAttribute == null)
            {
                // Installed manually by Harmony.Patch or declared by a
                // TargetMethod dynamic selector; no static mapping exists.
                warnings++;
                Console.WriteLine("DYNAMIC " + patchType.FullName + "::" + patchMethod.Name);
                continue;
            }

            TypeReference? targetType = patchAttribute.ConstructorArguments
                .Select(a => a.Value).OfType<TypeReference>().FirstOrDefault();
            string? targetName = patchAttribute.ConstructorArguments
                .Select(a => a.Value).OfType<string>().FirstOrDefault();

            if (targetType == null || string.IsNullOrWhiteSpace(targetName))
            {
                // Harmony also supports patch-class TargetMethod selectors,
                // field/property constructors and parameter-type overloads.
                warnings++;
                Console.WriteLine("SELECTOR " + patchType.FullName + "::" + patchMethod.Name);
                continue;
            }

            if (!types.TryGetValue(targetType.FullName, out var target))
            {
                failures.Add("MISSING TYPE " + targetType.FullName + " for " +
                             patchType.FullName + "::" + patchMethod.Name);
                continue;
            }

            var candidates = target.Methods.Where(m => m.Name == targetName).ToArray();
            if (candidates.Length != 1)
            {
                // Without a method-argument overload selector, Harmony needs
                // a unique name. Report all ambiguity rather than guessing.
                failures.Add("MISSING/AMBIGUOUS TARGET " +
                    target.FullName + "::" + targetName + " (" + candidates.Length +
                    " overloads) for " + patchType.FullName + "::" + patchMethod.Name);
                continue;
            }

            var original = candidates[0];
            tested++;
            Console.WriteLine("PATCH " + patchType.FullName + "::" + patchMethod.Name +
                " => " + original.FullName);

            bool isTranspiler = IsAttributePresent(patchMethod, "HarmonyTranspiler");
            foreach (var param in patchMethod.Parameters)
            {
                string name = param.Name;
                if (isTranspiler &&
                    (name == "instructions" || name == "generator" ||
                     name == "original"))
                {
                    // Harmony transpiler parameters are not original method
                    // parameters and should never be name-matched against them.
                    continue;
                }
                if (name.StartsWith("___", StringComparison.Ordinal))
                {
                    var field = target.Fields.FirstOrDefault(f => f.Name == name.Substring(3));
                    if (field == null)
                    {
                        failures.Add("MISSING FIELD INJECTION " + original.FullName +
                                     " :::" + name);
                    }
                    else if (target.IsValueType)
                    {
                        warnings++;
                        Console.WriteLine("VALUE-TYPE FIELD INJECTION " + name +
                            " on " + target.FullName +
                            " (needs live Mono/Harmony validation)");
                    }
                }
                else if (name == "__instance")
                {
                    if (original.IsStatic)
                    {
                        failures.Add("INSTANCE IN STATIC PATCH " + original.FullName);
                    }
                }
                else if (name == "__result")
                {
                    if (original.ReturnType.FullName == "System.Void")
                    {
                        failures.Add("RESULT IN VOID PATCH " + original.FullName);
                    }
                }
                else if (name == "__state" ||
                         name == "__runOriginal" ||
                         name == "__originalMethod" ||
                         name == "__exception" ||
                         name == "__args" ||
                         name == "__resultRef")
                {
                    // Harmony special injections.
                }
                else if (name.StartsWith("__", StringComparison.Ordinal) &&
                         int.TryParse(name.Substring(2), out var position))
                {
                    if (position >= original.Parameters.Count)
                    {
                        failures.Add("ARGUMENT INDEX MISMATCH " + original.FullName +
                            " :::" + name);
                    }
                }
                else if (!original.Parameters.Any(p => p.Name == name) &&
                         !param.CustomAttributes.Any(a => HasAttributeName(a, "HarmonyArgument")))
                {
                    failures.Add("ARGUMENT NAME MISMATCH " + original.FullName +
                                 " :::" + name);
                }
            }
        }
    }

    if (types.TryGetValue("SoftReferenceableAssets.AssetLoader", out var loader))
    {
        Console.WriteLine("ASSET LOADER " +
            (loader.IsValueType ? "VALUETYPE" : "REFERENCE-TYPE") +
            " : " + loader.Module.Name);
        var callbacks = loader.Methods.Where(m => m.Name == "InvokeCallbacks").ToArray();
        foreach (var signature in callbacks)
        {
            Console.WriteLine("ASSET CALLBACK " + signature.FullName);
        }
        var releases = loader.Methods.Where(m => m.Name == "Release").ToArray();
        var refCount = loader.Properties.FirstOrDefault(p => p.Name == "ReferenceCount");
        Console.WriteLine("ASSET RELEASE candidates=" + releases.Length +
            " refcount=" + (refCount?.PropertyType.FullName ?? "(missing)"));
        if (releases.Length != 1 || releases[0].Parameters.Count != 0)
        {
            failures.Add("Expected unique AssetLoader.Release() for generic mock cleanup");
        }
        if (refCount?.PropertyType.FullName != "System.UInt32" &&
            refCount?.PropertyType.FullName != "System.Int32")
        {
            failures.Add("AssetLoader.ReferenceCount must be an integer for cleanup contract");
        }

        // This hook is selected dynamically by AssetManager.Patches.TargetMethod.
        // Check the original private struct's injected fields explicitly; a
        // normal public-API probe misses field types and by-reference injection.
        var patchedAssetType = AllTypes(jotunn.Types).FirstOrDefault(t =>
            t.FullName == "Jotunn.Managers.AssetManager/Patches");
        var hook = patchedAssetType?.Methods.FirstOrDefault(m => m.Name == "Loaded");
        if (hook == null || callbacks.Length != 1)
        {
            failures.Add("Missing or ambiguous AssetLoader.InvokeCallbacks hook contract");
        }
        else
        {
            tested++;
            bool wholeStructByRef = hook.Parameters.Any(p =>
                p.Name == "__instance" &&
                p.ParameterType is ByReferenceType br &&
                br.ElementType.FullName == loader.FullName);
            Console.WriteLine("ASSET PATCH WHOLE-STRUCT BYREF: " + wholeStructByRef);
            if (loader.IsValueType && !wholeStructByRef)
            {
                warnings++;
                Console.WriteLine(
                    "RISK: Internal value-type AssetLoader still uses existing " +
                    "Harmony field injections in its InvokeCallbacks prefix; " +
                    "full struct byref is inaccessible with raw game references.");
            }
            foreach (var param in hook.Parameters.Where(p =>
                         p.Name.StartsWith("___", StringComparison.Ordinal)))
            {
                string name = param.Name.Substring(3);
                var field = loader.Fields.FirstOrDefault(f => f.Name == name);
                if (field == null)
                {
                    failures.Add("Missing AssetLoader field injected by Harmony: " + name);
                    continue;
                }

                TypeReference injectedType = param.ParameterType is ByReferenceType reference
                    ? reference.ElementType
                    : param.ParameterType;
                if (injectedType.FullName != field.FieldType.FullName)
                {
                    failures.Add("AssetLoader field injection type mismatch for " + name +
                        ": hook expects " + injectedType.FullName +
                        ", runtime field is " + field.FieldType.FullName);
                }
                else
                {
                    Console.WriteLine("ASSET FIELD " + name + " : " +
                        field.FieldType.FullName +
                        (param.ParameterType is ByReferenceType ? " (by ref)" : ""));
                }
            }

            Console.WriteLine("ASSET HOOK dynamically targets a " +
                (loader.IsValueType ? "value-type" : "reference-type") +
                " method; managed metadata alone cannot validate native Mono/Harmony JIT behavior.");
        }

        var releasePatchType = AllTypes(jotunn.Types).FirstOrDefault(t =>
            t.FullName == "Jotunn.Managers.AssetManager/ReleasePatches");
        var releasePatch = releasePatchType?.Methods.FirstOrDefault(m =>
            m.Name == "Released" && IsAttributePresent(m, "HarmonyPostfix"));
        bool hasTargetSelector = releasePatchType?.Methods.Any(m =>
            m.Name == "TargetMethod" && m.IsStatic) ?? false;
        // Never regress to boxing the game's internal value type through
        // Harmony's unsafe object __instance injection. CI has reproduced
        // an actual AccessViolationException with that signature.
        bool safeRelease = releasePatch != null &&
            hasTargetSelector && releasePatch.Parameters.Count == 1 &&
            releasePatch.Parameters[0].Name == "___m_assetID" &&
            releasePatch.Parameters[0].ParameterType.FullName ==
                "SoftReferenceableAssets.AssetID";
        Console.WriteLine("ASSET RELEASE PATCH FIELD-ONLY (NO __instance): " +
            safeRelease);
        if (!safeRelease)
        {
            failures.Add("Release postfix must inject only AssetID, never boxed __instance");
        }

        // The safe postfix retrieves the boxed loader via the owner registry.
        // Verify that the private array's element type and lookup field are
        // still available in the exact shipped game assemblies.
        bool validRegistry = types.TryGetValue(
            "SoftReferenceableAssets.AssetBundleLoader", out var bundleRegistry) &&
            bundleRegistry.Fields.Any(f =>
                f.Name == "m_assetIDToLoaderIndex" &&
                f.FieldType.FullName.Contains("Dictionary")) &&
            bundleRegistry.Fields.Any(f =>
                f.Name == "m_assetLoaders" &&
                f.FieldType is ArrayType arr &&
                arr.ElementType.FullName == loader.FullName);
        Console.WriteLine("ASSET RELEASE REGISTRY CONTRACT: " + validRegistry);
        if (!validRegistry)
        {
            failures.Add("Release postfix cannot reflect AssetID-to-loader registry");
        }
    }
    else
    {
        failures.Add("Missing current SoftReferenceableAssets.AssetLoader");
    }

    Console.WriteLine("Harmony hook audit: " + tested + " static targets validated, " +
                      warnings + " dynamic/optional warnings, " +
                      failures.Count + " failures.");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("FAIL: " + failure);
    }
    return failures.Count == 0 ? 0 : 1;
}
finally
{
    foreach (var module in gameModules) module.Dispose();
}

static bool IsAttributePresent(MethodDefinition method, string name) =>
    method.CustomAttributes.Any(a => HasAttributeName(a, name));

static bool HasAttributeName(CustomAttribute attribute, string name) =>
    attribute.AttributeType.Name == name ||
    attribute.AttributeType.Name == name + "Attribute";

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> root)
{
    foreach (var type in root)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes))
        {
            yield return nested;
        }
    }
}
