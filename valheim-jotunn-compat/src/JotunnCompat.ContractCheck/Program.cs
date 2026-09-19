using Mono.Cecil;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: JotunnCompat.ContractCheck <Jotunn.dll> [JotunnCompat.Preloader.dll]");
    return 2;
}

var jotunnPath = Path.GetFullPath(args[0]);
if (!File.Exists(jotunnPath))
{
    throw new FileNotFoundException("Jotunn.dll not found", jotunnPath);
}

using (var module = ModuleDefinition.ReadModule(jotunnPath, new ReaderParameters
{
    ReadingMode = ReadingMode.Deferred,
    ReadSymbols = false
}))
{
    if (!string.Equals(module.Assembly.Name.Name, "Jotunn", StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            $"Expected assembly name 'Jotunn', got '{module.Assembly.Name.Name}'.");
    }

    var expectedVersion = new Version(2, 30, 1, 0);
    if (module.Assembly.Name.Version != expectedVersion)
    {
        throw new InvalidDataException(
            $"Expected Jotunn assembly version {expectedVersion}, got {module.Assembly.Name.Version}.");
    }

    RequireMethod(module, "Jotunn.Utils.PatchInit", "InitializePatches");
    RequireMethod(module, "Jotunn.Utils.AutomaticLocalizationsLoading", "Init");
    RequireMethod(
        module,
        "Jotunn.Managers.MockManager",
        "FixReferences",
        "System.Object",
        "System.Int32");
    RequireMethod(
        module,
        "Jotunn.Managers.MockManager",
        "GetRealPrefabFromMock",
        "UnityEngine.Object",
        "System.Type");
    RequireMethod(
        module,
        "Jotunn.Managers.PrefabManager/Cache",
        "GetPrefab",
        "System.Type",
        "System.String");
    RequireMethod(
        module,
        "Jotunn.Managers.PrefabManager/Cache",
        "Clear");
    RequireMethod(
        module,
        "Jotunn.Managers.AssetManager/Patches",
        "AssetBundleLoader_GetAllAssetPathsMappedToAssetID",
        "System.Collections.Generic.IEnumerable`1<HarmonyLib.CodeInstruction>");

    Console.WriteLine($"Jotunn contract check passed: {module.Assembly.Name.FullName}");
}

if (args.Length == 2)
{
    var preloaderPath = Path.GetFullPath(args[1]);
    if (!File.Exists(preloaderPath))
    {
        throw new FileNotFoundException(
            "JotunnCompat.Preloader.dll not found",
            preloaderPath);
    }

    using var preloader = ModuleDefinition.ReadModule(preloaderPath, new ReaderParameters
    {
        ReadingMode = ReadingMode.Deferred,
        ReadSymbols = false
    });

    if (!string.Equals(
            preloader.Assembly.Name.Name,
            "JotunnCompat.Preloader",
            StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            $"Expected preloader assembly name 'JotunnCompat.Preloader', got " +
            $"'{preloader.Assembly.Name.Name}'.");
    }

    var patcher = FindType(preloader, "JotunnCompat.Preloader.Patcher")
        ?? throw new MissingMemberException(
            "Required preloader type not found: JotunnCompat.Preloader.Patcher");

    if (!(patcher.IsPublic && patcher.IsAbstract && patcher.IsSealed))
    {
        throw new InvalidDataException(
            "JotunnCompat.Preloader.Patcher must be a public static class.");
    }

    var initialize = RequireMethod(
        preloader,
        "JotunnCompat.Preloader.Patcher",
        "Initialize");
    RequirePublicStaticVoid(initialize);

    var patch = RequireMethod(
        preloader,
        "JotunnCompat.Preloader.Patcher",
        "Patch",
        "Mono.Cecil.AssemblyDefinition&");
    RequirePublicStaticVoid(patch);

    var targetDlls = RequireMethod(
        preloader,
        "JotunnCompat.Preloader.Patcher",
        "get_TargetDLLs");
    if (!(targetDlls.IsPublic && targetDlls.IsStatic) ||
        !string.Equals(
            targetDlls.ReturnType.FullName,
            "System.Collections.Generic.IEnumerable`1<System.String>",
            StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            "Patcher.TargetDLLs must be a public static IEnumerable<string> getter.");
    }

    Console.WriteLine(
        $"BepInEx 5 patcher contract check passed: {preloader.Assembly.Name.FullName}");
}

return 0;

static MethodDefinition RequireMethod(
    ModuleDefinition module,
    string typeFullName,
    string methodName,
    params string[] parameterTypes)
{
    var type = FindType(module, typeFullName)
        ?? throw new MissingMemberException($"Required type not found: {typeFullName}");

    var candidates = type.Methods.Where(m =>
        string.Equals(m.Name, methodName, StringComparison.Ordinal) &&
        !m.HasGenericParameters &&
        m.Parameters.Count == parameterTypes.Length);

    foreach (var candidate in candidates)
    {
        var match = true;
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            if (!string.Equals(
                    candidate.Parameters[i].ParameterType.FullName,
                    parameterTypes[i],
                    StringComparison.Ordinal))
            {
                match = false;
                break;
            }
        }

        if (match)
        {
            return candidate;
        }
    }

    throw new MissingMethodException(
        typeFullName,
        methodName + "(" + string.Join(", ", parameterTypes) + ")");
}

static void RequirePublicStaticVoid(MethodDefinition method)
{
    if (!(method.IsPublic && method.IsStatic) ||
        !string.Equals(method.ReturnType.FullName, "System.Void", StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            $"{method.FullName} must be public static void.");
    }
}

static TypeDefinition? FindType(ModuleDefinition module, string fullName)
{
    foreach (var type in module.Types)
    {
        var match = FindTypeRecursive(type, fullName);
        if (match != null)
        {
            return match;
        }
    }

    return null;
}

static TypeDefinition? FindTypeRecursive(TypeDefinition type, string fullName)
{
    if (string.Equals(type.FullName, fullName, StringComparison.Ordinal))
    {
        return type;
    }

    foreach (var nested in type.NestedTypes)
    {
        var match = FindTypeRecursive(nested, fullName);
        if (match != null)
        {
            return match;
        }
    }

    return null;
}
