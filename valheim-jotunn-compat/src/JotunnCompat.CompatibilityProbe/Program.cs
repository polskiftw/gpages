using Mono.Cecil;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: JotunnCompat.CompatibilityProbe <Jotunn.dll> <dependent-mod.dll>");
    return 2;
}

var jotunnPath = Path.GetFullPath(args[0]);
var modPath = Path.GetFullPath(args[1]);

if (!File.Exists(jotunnPath))
{
    throw new FileNotFoundException("Jotunn.dll not found", jotunnPath);
}
if (!File.Exists(modPath))
{
    throw new FileNotFoundException("Dependent mod DLL not found", modPath);
}

using var resolver = new PinnedAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(jotunnPath)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(modPath)!);

using var jotunn = AssemblyDefinition.ReadAssembly(
    jotunnPath,
    new ReaderParameters
    {
        AssemblyResolver = resolver,
        ReadingMode = ReadingMode.Deferred,
        ReadSymbols = false
    });

// The compatibility question is deliberately version-independent: an existing mod
// can reference an older Jotunn assembly version while the drop-in compatibility
// package supplies a newer assembly named Jotunn. Pin every Cecil resolution of
// that assembly name to the exact baseline DLL passed on the command line.
resolver.Pin(jotunn);

using var mod = AssemblyDefinition.ReadAssembly(
    modPath,
    new ReaderParameters
    {
        AssemblyResolver = resolver,
        ReadingMode = ReadingMode.Deferred,
        ReadSymbols = false
    });

var jotunnReference = mod.MainModule.AssemblyReferences
    .FirstOrDefault(x => string.Equals(x.Name, "Jotunn", StringComparison.Ordinal));

if (jotunnReference == null)
{
    Console.WriteLine($"Dependent assembly: {mod.Name.FullName}");
    Console.WriteLine("No Jotunn assembly reference; compatibility probe skipped.");
    return 0;
}

var jotunnTypes = mod.MainModule.GetTypeReferences()
    .Where(IsJotunnTypeReference)
    .GroupBy(x => x.FullName, StringComparer.Ordinal)
    .Select(x => x.First())
    .OrderBy(x => x.FullName, StringComparer.Ordinal)
    .ToArray();

var jotunnMembers = mod.MainModule.GetMemberReferences()
    .Where(x => IsJotunnTypeReference(x.DeclaringType))
    .GroupBy(MemberKey, StringComparer.Ordinal)
    .Select(x => x.First())
    .OrderBy(MemberKey, StringComparer.Ordinal)
    .ToArray();

var failures = new List<string>();

foreach (var type in jotunnTypes)
{
    try
    {
        var resolved = type.Resolve();
        if (resolved == null ||
            !ReferenceEquals(resolved.Module.Assembly, jotunn))
        {
            failures.Add("TYPE " + type.FullName);
        }
    }
    catch (Exception ex)
    {
        failures.Add("TYPE " + type.FullName + " :: " + ex.GetType().Name + ": " + ex.Message);
    }
}

foreach (var member in jotunnMembers)
{
    try
    {
        ModuleDefinition? resolvedModule = member switch
        {
            MethodReference method => method.Resolve()?.Module,
            FieldReference field => field.Resolve()?.Module,
            TypeReference type => type.Resolve()?.Module,
            _ => null
        };

        if (resolvedModule == null ||
            !ReferenceEquals(resolvedModule.Assembly, jotunn))
        {
            failures.Add("MEMBER " + MemberKey(member));
        }
    }
    catch (Exception ex)
    {
        failures.Add(
            "MEMBER " + MemberKey(member) + " :: " +
            ex.GetType().Name + ": " + ex.Message);
    }
}

Console.WriteLine(
    $"Dependent assembly: {mod.Name.FullName}");
Console.WriteLine(
    $"Compiled Jotunn reference: {jotunnReference.FullName}");
Console.WriteLine(
    $"Compatibility baseline: {jotunn.Name.FullName}");
Console.WriteLine(
    $"Referenced Jotunn types: {jotunnTypes.Length}");
Console.WriteLine(
    $"Referenced Jotunn members: {jotunnMembers.Length}");

Console.WriteLine("Referenced Jotunn type surface:");
foreach (var type in jotunnTypes)
{
    Console.WriteLine("  TYPE " + type.FullName);
}

Console.WriteLine("Referenced Jotunn member surface:");
foreach (var member in jotunnMembers)
{
    Console.WriteLine("  MEMBER " + MemberKey(member));
}

if (failures.Count != 0)
{
    Console.Error.WriteLine(
        $"Unresolved Jotunn references: {failures.Count}");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("  " + failure);
    }

    return 1;
}

Console.WriteLine("All referenced Jotunn symbols resolve against the compatibility baseline.");
return 0;

static bool IsJotunnTypeReference(TypeReference type)
{
    if (type is TypeSpecification specification)
    {
        return IsJotunnTypeReference(specification.ElementType);
    }

    if (type.DeclaringType != null &&
        IsJotunnTypeReference(type.DeclaringType))
    {
        return true;
    }

    return type.Scope switch
    {
        AssemblyNameReference assembly =>
            string.Equals(assembly.Name, "Jotunn", StringComparison.Ordinal),
        ModuleDefinition module =>
            string.Equals(module.Assembly?.Name?.Name, "Jotunn", StringComparison.Ordinal),
        _ => false
    };
}

static string MemberKey(MemberReference member)
{
    return member switch
    {
        MethodReference method =>
            method.DeclaringType.FullName + "::" +
            method.Name + "(" +
            string.Join(",", method.Parameters.Select(x => x.ParameterType.FullName)) +
            "):" + method.ReturnType.FullName,
        FieldReference field =>
            field.DeclaringType.FullName + "::" +
            field.Name + ":" + field.FieldType.FullName,
        _ => member.FullName
    };
}

sealed class PinnedAssemblyResolver : DefaultAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> pinned =
        new(StringComparer.Ordinal);

    public void Pin(AssemblyDefinition assembly)
    {
        pinned[assembly.Name.Name] = assembly;
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        return pinned.TryGetValue(name.Name, out var assembly)
            ? assembly
            : base.Resolve(name);
    }

    public override AssemblyDefinition Resolve(
        AssemblyNameReference name,
        ReaderParameters parameters)
    {
        return pinned.TryGetValue(name.Name, out var assembly)
            ? assembly
            : base.Resolve(name, parameters);
    }
}
