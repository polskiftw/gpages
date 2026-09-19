using Mono.Cecil;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: JotunnCompat.ContractCheck <Jotunn.dll>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
if (!File.Exists(path))
{
    throw new FileNotFoundException("Jotunn.dll not found", path);
}

using var module = ModuleDefinition.ReadModule(path, new ReaderParameters
{
    ReadingMode = ReadingMode.Deferred,
    ReadSymbols = false
});

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

Console.WriteLine($"Jotunn contract check passed: {module.Assembly.Name.FullName}");
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
