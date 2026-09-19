using Mono.Cecil;

if (args.Length != 1)
{
    Console.Error.WriteLine(
        "Usage: JotunnCompat.ValheimContractCheck <Valheim_Data/Managed>");
    return 2;
}

var managed = Path.GetFullPath(args[0]);
if (!Directory.Exists(managed))
{
    throw new DirectoryNotFoundException(
        $"Valheim managed directory not found: {managed}");
}

var modules = new List<ModuleDefinition>();
foreach (var path in Directory.EnumerateFiles(managed, "*.dll"))
{
    try
    {
        modules.Add(ModuleDefinition.ReadModule(
            path,
            new ReaderParameters
            {
                ReadingMode = ReadingMode.Deferred,
                ReadSymbols = false
            }));
    }
    catch (BadImageFormatException)
    {
        // Ignore any native/helper DLL that is not a managed assembly.
    }
}

if (modules.Count == 0)
{
    throw new InvalidDataException(
        $"No managed assemblies could be read from {managed}");
}

try
{
    // Private Valheim members reached by GameInternals and Harmony string targets.
    RequireField(modules, "ZNetScene", "m_namedPrefabs", isStatic: false);
    RequireMethod(modules, "ZNetScene", "Awake");

    RequireField(modules, "ObjectDB", "m_itemByHash", isStatic: false);
    RequireField(modules, "ObjectDB", "m_terrainOps", isStatic: false);
    RequireField(modules, "ObjectDB", "m_terrainOpsByHash", isStatic: false);
    RequireMethod(modules, "ObjectDB", "GetPrefabHash", "UnityEngine.GameObject");
    RequireMethod(modules, "ObjectDB", "Awake");
    RequireMethod(modules, "ObjectDB", "CopyOtherDB", "ObjectDB");

    RequireField(modules, "ZoneSystem", "m_locationsByHash", isStatic: false);
    RequireMethod(modules, "ZoneSystem", "SetupLocations");

    RequireField(modules, "DungeonDB", "m_rooms", isStatic: false);
    RequireMethod(modules, "DungeonDB", "GenerateHashList");
    RequireMethod(modules, "DungeonDB", "GetRoom", "System.Int32");
    RequireMethod(modules, "DungeonDB", "Start");

    RequireField(modules, "DungeonGenerator", "m_availableRooms", isStatic: true);
    RequireMethod(modules, "DungeonGenerator", "SetupAvailableRooms");

    RequireMethod(modules, "Game", "Start");
    RequireMethod(modules, "Localization", "AddWord", "System.String", "System.String");
    RequireUniqueMethod(modules, "Localization", "SetupLanguage");

    // Current-game private/version-sensitive bridges used by generic Jotunn APIs.
    RequireField(modules, "ZNet", "m_adminList", isStatic: false);
    RequireUniqueMethod(modules, "ZNet", "ListContainsId");
    RequireField(modules, "GameCamera", "m_mouseCapture", isStatic: false);
    RequireUniqueMethod(modules, "GameCamera", "UpdateMouseCapture");
    RequireAnyMethod(modules, "Minimap", "LoadMapData", "Start");

    var imageConversion = RequireType(modules, "UnityEngine.ImageConversion");
    RequireMethodOnType(
        imageConversion,
        "LoadImage",
        "UnityEngine.Texture2D",
        "System.Byte[]");

    // SoftReferenceableAssets internals used by the slim runtime asset bridge.
    RequireField(
        modules,
        "SoftReferenceableAssets.Runtime",
        "s_assetLoader",
        isStatic: true);
    RequireMethod(
        modules,
        "SoftReferenceableAssets.Runtime",
        "GetAllAssetPathsInBundleMappedToAssetID");

    var assetLoader = RequireType(modules, "SoftReferenceableAssets.AssetLoader");
    RequireFieldOnType(assetLoader, "m_assetID", isStatic: false);
    RequireFieldOnType(assetLoader, "m_asset", isStatic: false);
    RequireFieldOnType(assetLoader, "m_bundleLoaderIndex", isStatic: false);
    RequireConstructor(
        assetLoader,
        "SoftReferenceableAssets.AssetID",
        "SoftReferenceableAssets.AssetLocation");
    RequireMethodOnType(assetLoader, "HoldReference");
    RequireMethodOnType(
        assetLoader,
        "InvokeCallbacks",
        "SoftReferenceableAssets.LoadResult");

    var assetLocation = RequireType(modules, "SoftReferenceableAssets.AssetLocation");
    RequireConstructor(assetLocation, "System.String", "System.String");

    var assetBundleLoader = RequireType(
        modules,
        "SoftReferenceableAssets.AssetBundleLoader");
    RequireFieldOnType(
        assetBundleLoader,
        "m_assetIDToLoaderIndex",
        isStatic: false);
    RequireFieldOnType(assetBundleLoader, "m_assetLoaders", isStatic: false);
    RequireFieldOnType(
        assetBundleLoader,
        "m_bundleNameToLoaderIndex",
        isStatic: false);
    RequireFieldOnType(assetBundleLoader, "m_bundleLoaders", isStatic: false);
    RequireMethodOnType(assetBundleLoader, "OnInitCompleted");

    var bundleLoader = RequireType(modules, "SoftReferenceableAssets.BundleLoader");
    RequireConstructor(bundleLoader, "System.String", "System.String");
    RequireMethodOnType(bundleLoader, "HoldReference");
    RequireMethodOnType(bundleLoader, "SetDependencies", "System.String[]");

    Console.WriteLine("Valheim runtime contract check passed.");
    Console.WriteLine($"  Managed assemblies scanned: {modules.Count}");
    Console.WriteLine("  Reflection/Harmony targets checked: 42");
    return 0;
}
finally
{
    foreach (var module in modules)
    {
        module.Dispose();
    }
}

static TypeDefinition RequireType(
    IEnumerable<ModuleDefinition> modules,
    string fullName)
{
    foreach (var module in modules)
    {
        foreach (var type in module.Types)
        {
            var found = FindTypeRecursive(type, fullName);
            if (found != null)
            {
                Console.WriteLine(
                    $"  TYPE {fullName} [{module.Name}]");
                return found;
            }
        }
    }

    throw new MissingMemberException(
        $"Required type not found in Valheim managed assemblies: {fullName}");
}

static TypeDefinition? FindTypeRecursive(
    TypeDefinition type,
    string fullName)
{
    if (string.Equals(type.FullName, fullName, StringComparison.Ordinal))
    {
        return type;
    }

    foreach (var nested in type.NestedTypes)
    {
        var found = FindTypeRecursive(nested, fullName);
        if (found != null)
        {
            return found;
        }
    }

    return null;
}

static FieldDefinition RequireField(
    IEnumerable<ModuleDefinition> modules,
    string typeFullName,
    string fieldName,
    bool isStatic)
{
    return RequireFieldOnType(
        RequireType(modules, typeFullName),
        fieldName,
        isStatic);
}

static FieldDefinition RequireFieldOnType(
    TypeDefinition type,
    string fieldName,
    bool isStatic)
{
    var field = type.Fields.FirstOrDefault(
        f => string.Equals(f.Name, fieldName, StringComparison.Ordinal));

    if (field == null)
    {
        throw new MissingFieldException(type.FullName, fieldName);
    }

    if (field.IsStatic != isStatic)
    {
        throw new InvalidDataException(
            $"{type.FullName}::{fieldName} static contract changed. " +
            $"Expected IsStatic={isStatic}, got {field.IsStatic}.");
    }

    Console.WriteLine(
        $"  FIELD {type.FullName}::{field.Name} : {field.FieldType.FullName}");
    return field;
}

static MethodDefinition RequireMethod(
    IEnumerable<ModuleDefinition> modules,
    string typeFullName,
    string methodName,
    params string[] parameterTypes)
{
    return RequireMethodOnType(
        RequireType(modules, typeFullName),
        methodName,
        parameterTypes);
}

static MethodDefinition RequireMethodOnType(
    TypeDefinition type,
    string methodName,
    params string[] parameterTypes)
{
    foreach (var method in type.Methods)
    {
        if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
            method.Parameters.Count != parameterTypes.Length)
        {
            continue;
        }

        var matches = true;
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            if (!string.Equals(
                    method.Parameters[i].ParameterType.FullName,
                    parameterTypes[i],
                    StringComparison.Ordinal))
            {
                matches = false;
                break;
            }
        }

        if (matches)
        {
            Console.WriteLine(
                $"  METHOD {type.FullName}::{method.Name}(" +
                string.Join(",", parameterTypes) + ")");
            return method;
        }
    }

    throw new MissingMethodException(
        type.FullName,
        methodName + "(" + string.Join(", ", parameterTypes) + ")");
}

static MethodDefinition RequireAnyMethod(
    IEnumerable<ModuleDefinition> modules,
    string typeFullName,
    params string[] methodNames)
{
    var type = RequireType(modules, typeFullName);
    foreach (var name in methodNames)
    {
        var method = type.Methods.FirstOrDefault(
            m => string.Equals(m.Name, name, StringComparison.Ordinal));
        if (method != null)
        {
            Console.WriteLine($"  METHOD {method.FullName}");
            return method;
        }
    }

    throw new MissingMethodException(
        typeFullName,
        string.Join(" or ", methodNames));
}

static MethodDefinition RequireUniqueMethod(
    IEnumerable<ModuleDefinition> modules,
    string typeFullName,
    string methodName)
{
    var type = RequireType(modules, typeFullName);
    var methods = type.Methods
        .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
        .ToArray();

    if (methods.Length != 1)
    {
        throw new InvalidDataException(
            $"{typeFullName}::{methodName} expected exactly one overload, " +
            $"found {methods.Length}.");
    }

    Console.WriteLine($"  METHOD {methods[0].FullName}");
    return methods[0];
}

static MethodDefinition RequireConstructor(
    TypeDefinition type,
    params string[] parameterTypes)
{
    return RequireMethodOnType(type, ".ctor", parameterTypes);
}
