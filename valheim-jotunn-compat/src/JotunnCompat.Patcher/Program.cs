using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: JotunnCompat.Patcher <input-Jotunn.dll> <fast-path.dll> <output-Jotunn.dll>");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var fastPath = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);

if (!File.Exists(inputPath))
{
    throw new FileNotFoundException("Input Jotunn.dll not found", inputPath);
}
if (!File.Exists(fastPath))
{
    throw new FileNotFoundException("Fast-path assembly not found", fastPath);
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(inputPath)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(fastPath)!);

using var module = ModuleDefinition.ReadModule(inputPath, new ReaderParameters
{
    AssemblyResolver = resolver,
    ReadingMode = ReadingMode.Immediate,
    ReadSymbols = false
});

if (!string.Equals(module.Assembly.Name.Name, "Jotunn", StringComparison.Ordinal))
{
    throw new InvalidDataException($"Expected assembly name Jotunn, got {module.Assembly.Name.Name}");
}

var originalVersion = module.Assembly.Name.Version;
var originalApi = PublicApiSnapshot(module);

using var fastModule = ModuleDefinition.ReadModule(fastPath, new ReaderParameters
{
    AssemblyResolver = resolver,
    ReadingMode = ReadingMode.Immediate,
    ReadSymbols = false
});

var startup = RequireType(fastModule, "JotunnCompat.FastPath.Startup");
var fastPatchInit = RequireMethod(startup, "InitializePatches", 0);
var fastLocalization = RequireMethod(startup, "LoadLocalizations", 0);

ReplaceBody(
    RequireMethod(RequireType(module, "Jotunn.Utils.PatchInit"), "InitializePatches", 0),
    module.ImportReference(fastPatchInit));

ReplaceBody(
    RequireMethod(RequireType(module, "Jotunn.Utils.AutomaticLocalizationsLoading"), "Init", 0),
    module.ImportReference(fastLocalization));

module.Write(outputPath, new WriterParameters { WriteSymbols = false });

using var verify = ModuleDefinition.ReadModule(outputPath, new ReaderParameters
{
    ReadingMode = ReadingMode.Immediate,
    ReadSymbols = false
});

if (!string.Equals(verify.Assembly.Name.Name, "Jotunn", StringComparison.Ordinal))
{
    throw new InvalidDataException("Patched output changed the Jotunn assembly identity");
}

if (verify.Assembly.Name.Version != originalVersion)
{
    throw new InvalidDataException($"Patched output changed assembly version {originalVersion} -> {verify.Assembly.Name.Version}");
}

var patchedApi = PublicApiSnapshot(verify);
var removed = originalApi.Except(patchedApi, StringComparer.Ordinal).ToArray();
var added = patchedApi.Except(originalApi, StringComparer.Ordinal).ToArray();

if (removed.Length != 0 || added.Length != 0)
{
    throw new InvalidDataException(
        "Public API changed while patching. " +
        $"Removed: {string.Join(", ", removed.Take(20))}. " +
        $"Added: {string.Join(", ", added.Take(20))}.");
}

var main = RequireType(verify, "Jotunn.Main");
AssertConstant(main, "ModGuid", "com.jotunn.jotunn");
AssertConstant(main, "ModName", "Jotunn");

Console.WriteLine($"Patched {Path.GetFileName(inputPath)} -> {outputPath}");
Console.WriteLine($"Assembly identity preserved: {verify.Assembly.Name.FullName}");
Console.WriteLine($"Public API preserved: {patchedApi.Count} signatures");
Console.WriteLine("Fast paths: PatchInit discovery + automatic localization discovery");
return 0;

static TypeDefinition RequireType(ModuleDefinition module, string fullName)
{
    return module.GetType(fullName)
        ?? throw new MissingMemberException($"Type not found: {fullName}");
}

static MethodDefinition RequireMethod(TypeDefinition type, string name, int parameterCount)
{
    return type.Methods.SingleOrDefault(x => x.Name == name && x.Parameters.Count == parameterCount)
        ?? throw new MissingMethodException(type.FullName, name);
}

static void ReplaceBody(MethodDefinition target, MethodReference replacement)
{
    if (!target.IsStatic || target.Parameters.Count != 0 || target.ReturnType.FullName != "System.Void")
    {
        throw new InvalidDataException($"Unexpected target signature: {target.FullName}");
    }

    target.Body.ExceptionHandlers.Clear();
    target.Body.Variables.Clear();
    target.Body.Instructions.Clear();
    target.Body.InitLocals = false;

    var il = target.Body.GetILProcessor();
    il.Append(il.Create(OpCodes.Call, replacement));
    il.Append(il.Create(OpCodes.Ret));
}

static void AssertConstant(TypeDefinition type, string fieldName, string expected)
{
    var field = type.Fields.SingleOrDefault(x => x.Name == fieldName)
        ?? throw new MissingFieldException(type.FullName, fieldName);

    var actual = field.Constant as string;
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        throw new InvalidDataException($"{type.FullName}.{fieldName} expected '{expected}', got '{actual}'");
    }
}

static HashSet<string> PublicApiSnapshot(ModuleDefinition module)
{
    var api = new HashSet<string>(StringComparer.Ordinal);

    foreach (var type in module.Types)
    {
        AddType(type, api);
    }

    return api;
}

static void AddType(TypeDefinition type, HashSet<string> api)
{
    if (!(type.IsPublic || type.IsNestedPublic))
    {
        foreach (var nested in type.NestedTypes)
        {
            AddType(nested, api);
        }
        return;
    }

    api.Add($"T:{type.FullName}:{type.BaseType?.FullName}");

    foreach (var generic in type.GenericParameters)
    {
        api.Add($"TG:{type.FullName}:{generic.Position}:{generic.Name}:{generic.Attributes}");
    }

    foreach (var field in type.Fields.Where(x => x.IsPublic))
    {
        api.Add($"F:{type.FullName}.{field.Name}:{field.FieldType.FullName}:{field.Attributes}:{field.Constant}");
    }

    foreach (var method in type.Methods.Where(x => x.IsPublic))
    {
        var parameters = string.Join(",", method.Parameters.Select(x => x.ParameterType.FullName));
        var generics = string.Join(",", method.GenericParameters.Select(x => x.Name));
        api.Add($"M:{type.FullName}.{method.Name}<{generics}>({parameters}):{method.ReturnType.FullName}:{method.Attributes}");
    }

    foreach (var property in type.Properties)
    {
        var getPublic = property.GetMethod?.IsPublic == true;
        var setPublic = property.SetMethod?.IsPublic == true;
        if (getPublic || setPublic)
        {
            var parameters = string.Join(",", property.Parameters.Select(x => x.ParameterType.FullName));
            api.Add($"P:{type.FullName}.{property.Name}({parameters}):{property.PropertyType.FullName}:g={getPublic}:s={setPublic}");
        }
    }

    foreach (var evt in type.Events)
    {
        var addPublic = evt.AddMethod?.IsPublic == true;
        var removePublic = evt.RemoveMethod?.IsPublic == true;
        if (addPublic || removePublic)
        {
            api.Add($"E:{type.FullName}.{evt.Name}:{evt.EventType.FullName}:a={addPublic}:r={removePublic}");
        }
    }

    foreach (var nested in type.NestedTypes)
    {
        AddType(nested, api);
    }
}
