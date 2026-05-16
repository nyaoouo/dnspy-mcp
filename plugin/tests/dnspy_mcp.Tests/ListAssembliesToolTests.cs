using System.Collections.Generic;
using System.Linq;
using DnspyMcp.Tools;
using DnspyMcp.Util;
using Xunit;

namespace DnspyMcp.Tests;

internal sealed class FakeServices : IDnSpyServices
{
    public readonly List<AssemblyHandle> Assemblies = new();
    public List<TypeSummary> Types = new();
    public List<FieldSummary> Fields = new();
    public List<MethodSummary> Methods = new();
    public List<PropertyResult> Properties = new();

    // For new IL tools
    public List<TypePath> Paths = new();
    public MethodIlResult? MethodIl;
    public bool PatchSucceeded = true;
    public bool RevertSucceeded = true;
    public List<RevertedMethod> RevertedAll = new();

    public IEnumerable<AssemblyHandle> EnumerateAssemblies() => Assemblies;

    public AssemblyHandle OpenAssembly(string path)
    {
        var h = new AssemblyHandle(
            path,
            $"{System.IO.Path.GetFileName(path)}, Version=1.0",
            System.IO.Path.GetFileNameWithoutExtension(path));
        Assemblies.Add(h);
        return h;
    }

    public bool CloseAssembly(string identifier)
    {
        var found = Assemblies.Find(
            a => a.Path == identifier
                 || a.FullName == identifier
                 || a.Name == identifier);
        if (found is null) return false;
        Assemblies.Remove(found);
        return true;
    }

    public string DecompileMethod(string asm, uint token) =>
        $"// decompiled {asm} {token:X8}\nvoid M() {{}}";

    public void SaveAssembly(string identifier, string? destination)
    {
        // no-op for the fake
    }

    public AssemblyInfoResult GetAssemblyInfo(string id) =>
        new(id, id, $"C:/{id}.dll", "v4.0.30319", 1, Types.Count);

    public TypeListResult ListTypes(string asm, string? ns, int offset, int limit)
    {
        var pool = Types.Where(t => ns is null || t.Namespace == ns).ToList();
        var slice = pool.Skip(offset).Take(limit).ToList();
        var next = offset + slice.Count < pool.Count ? offset + slice.Count : -1;
        return new TypeListResult(slice, pool.Count, next);
    }

    public TypeInfoResult GetTypeInfo(string asm, string typeIdentifier)
    {
        var t = Types.FirstOrDefault(x => x.FullName == typeIdentifier || x.Name == typeIdentifier)
            ?? throw new System.InvalidOperationException($"Type not found: {typeIdentifier}");
        return new TypeInfoResult(
            t.Namespace, t.Name, t.FullName, t.Token, t.Kind,
            null, new List<string>(),
            Fields.Count, Methods.Count, Properties.Count, 0);
    }

    public FieldListResult GetTypeFields(string asm, string typeIdentifier, string pattern, int offset, int limit)
    {
        var all = Fields
            .Where(f => pattern == "*" || f.Name.Contains(pattern.Trim('*')))
            .ToList();
        var slice = all.Skip(offset).Take(limit).ToList();
        var next = offset + slice.Count < all.Count ? offset + slice.Count : -1;
        return new FieldListResult(slice, all.Count, next);
    }

    public PropertyResult GetTypeProperty(string asm, string typeIdentifier, string propertyName)
    {
        var p = Properties.FirstOrDefault(x => x.Name == propertyName)
            ?? throw new System.InvalidOperationException($"Property not found: {propertyName}");
        return p;
    }

    public MethodListResult ListMethods(string asm, string typeIdentifier, int offset, int limit)
    {
        var slice = Methods.Skip(offset).Take(limit).ToList();
        var next = offset + slice.Count < Methods.Count ? offset + slice.Count : -1;
        return new MethodListResult(slice, Methods.Count, next);
    }

    public TypeSearchResult SearchTypes(string pattern, string? scopeAssembly, int offset, int limit)
    {
        var asmName = scopeAssembly ?? "FakeAssembly";
        var trimmed = pattern.Trim('*');
        var all = Types
            .Where(t => pattern == "*" || t.FullName.Contains(trimmed) || t.Name.Contains(trimmed))
            .Select(t => new TypeSearchHit(asmName, t.Namespace, t.Name, t.FullName, t.Token))
            .ToList();
        var slice = all.Skip(offset).Take(limit).ToList();
        var next = offset + slice.Count < all.Count ? offset + slice.Count : -1;
        return new TypeSearchResult(slice, all.Count, next);
    }

    public PathSearchResult FindPathToType(string asm, string from, string to, int max)
        => new PathSearchResult(Paths);

    public MethodIlResult GetMethodIl(string asm, uint token)
        => MethodIl ?? throw new System.InvalidOperationException("no fake MethodIl configured");

    public PatchResult PatchMethodIl(string asm, uint token, System.Collections.Generic.IReadOnlyList<IlEdit> edits)
        => new PatchResult(PatchSucceeded, edits.Count, true);

    public RevertResult RevertMethodIl(string asm, uint token)
        => new RevertResult(RevertSucceeded);

    public RevertAllResult RevertAllPendingPatches(string? scope)
        => new RevertAllResult(RevertedAll);
}

public class ListAssembliesToolTests
{
    [Fact]
    public void Lists_RegisteredAssemblies()
    {
        var svc = new FakeServices();
        svc.OpenAssembly("C:/a.dll");
        var result = new ListAssembliesTool().Invoke(
            new Dictionary<string, object?>(), svc);
        Assert.False(result.IsError);
        Assert.Contains("a.dll", result.Content[0].Text);
    }
}
