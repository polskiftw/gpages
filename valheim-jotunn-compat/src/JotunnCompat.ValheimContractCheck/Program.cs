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

var assemblyCSharpPath = Path.Combine(managed, "Assembly-CSharp.dll");
var softRefsPath = Path.Combine(managed, "SoftReferenceableAssets.dll");

if (!File.Exists(assemblyCSharpPath))
{
    throw new FileNotFoundException("Assembly-CSharp.dll not found", assemblyCSharpPath);
}
if (!File.Exists(softRefsPath))
{
    throw new FileNotFoundException("SoftReferenceableAssets.dll not found", softRefsPath);
}

using var game = ReadModule(assemblyCSharpPath);
using var soft = ReadModule(softRefsPath);

// Private Valheim members reached by GameInternals and Harmony string targets.
RequireField(game, "ZNetScene", "m_namedPrefabs", isStatic: false);
RequireMethod(game, "ZNetScene", "Awake");

RequireField(game, "ObjectDB", "m_itemByHash", isStatic: false);
RequireMethod(game, "ObjectDB", "Awake");
RequireMethod(game, "ObjectDB", "CopyOtherDB", "ObjectDB");

RequireField(game, "ZoneSystem", "m_locationsByHash", isStatic: false);
RequireMethod(game, "ZoneSystem", "SetupLocations");

RequireField(game, "DungeonDB", "m_rooms", isStatic: false);
RequireMethod(game, "DungeonDB", "GenerateHashList");
RequireMethod(game, "DungeonDB", "Start");

RequireField(game, "DungeonGenerator", "m_availableRooms", isStatic: true);
RequireMethod(game, "DungeonGenerator", "SetupAvailableRooms");

RequireMethod(game, "Game", "Start");
RequireMethod(game, "Localization", "AddWord", "System.String", "System.String");
RequireUniqueMethod(game, "Localization", "SetupLanguage");

// SoftReferenceableAssets internals used by the slim runtime asset bridge.
RequireField(soft, "SoftReferenceableAssets.Runtime", "s_assetLoader", isStatic: true);
RequireMethod(
    soft,
    "SoftReferenceableAssets.Runtime",
    "GetAllAssetPathsInBundleMappedToAssetID");

var assetLoader = RequireType(soft, "SoftReferenceableAssets.AssetLoader");
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

var assetLocation = RequireType(soft, "SoftReferenceableAssets.AssetLocation");
RequireConstructor(assetLocation, "System.String", "System.String");

var assetBundleLoader = RequireType(soft, "SoftReferenceableAssets.AssetBundleLoader");
RequireFieldOnType(assetBundleLoader, "m_assetIDToLoaderIndex", isStatic: false);
RequireFieldOnType(assetBundleLoader, "m_assetLoaders", isStatic: false);
RequireFieldOnType(assetBundleLoader, "m_bundleNameToLoaderIndex", isStatic: false);
RequireFieldOnType(assetBundleLoader, "m_bundleLoaders", isStatic: false);

var bundleLoader = RequireType(soft, "SoftReferenceableAssets.BundleLoader");
RequireConstructor(bundleLoader, "System.String", "System.String");
RequireMethodOnType(bundleLoader, "HoldReference");
RequireMethodOnType(bundleLoader, "SetDependencies", "System.String[]");

Console.WriteLine($"Valheim runtime contract check passed.");
Console.WriteLine($"  Assembly-CSharp: {game.Assembly?.Name.FullName ?? game.Name}");
Console.WriteLine($"  SoftReferenceableAssets: {soft.Assembly?.Name.FullName ?? soft.Name}");
Console.WriteLine("  Reflection/Harmony targets checked: 31");
return 0;

static ModuleDefinition ReadModule(string path)
{
    return ModuleDefinition.ReadModule(path, new ReaderParameters
    {
        ReadingMode = ReadingMode.Deferred,
        ReadSymbols = false
    });
}

static TypeDefinition RequireType(ModuleDefinition module, string fullName)
{
    foreach (var type in module.Types)
    {
        var found = FindTypeRecursive(type, fullName);
        if (found != null)
        {
            return found;
        }
    }

    throw new MissingMemberException(
        $"{module.Name}: required type not found: {fullName}");
}

static TypeDefinition? FindTypeRecursive(TypeDefinition type, string fullName)
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
    ModuleDefinition module,
    string typeFullName,
    string fieldName,
    bool isStatic)
{
    return RequireFieldOnType(
        RequireType(module, typeFullName),
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
    ModuleDefinition module,
    string typeFullName,
    string methodName,
    params string[] parameterTypes)
{
    return RequireMethodOnType(
        RequireType(module, typeFullName),
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

static MethodDefinition RequireUniqueMethod(
    ModuleDefinition module,
    string typeFullName,
    string methodName)
{
    var type = RequireType(module, typeFullName);
    var methods = type.Methods
        .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
        .ToArray();

    if (methods.Length != 1)
    {
        throw new InvalidDataException(
            $"{typeFullName}::{methodName} expected exactly one overload, " +
            $"found {methods.Length}.");
    }

    Console.WriteLine(
        $"  METHOD {methods[0].FullName}");
    return methods[0];
}

static MethodDefinition RequireConstructor(
    TypeDefinition type,
    params string[] parameterTypes)
{
    return RequireMethodOnType(type, ".ctor", parameterTypes);
}
