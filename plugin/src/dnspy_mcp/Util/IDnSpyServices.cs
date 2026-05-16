using System.Collections.Generic;

namespace DnspyMcp.Util;

public interface IDnSpyServices
{
    /// <summary>Enumerate currently-loaded assemblies as (path, fullName, name) tuples.</summary>
    IEnumerable<AssemblyHandle> EnumerateAssemblies();

    /// <summary>Open an assembly from a filesystem path. Returns the handle.</summary>
    AssemblyHandle OpenAssembly(string path);

    /// <summary>Remove the assembly identified by path/fullName/name from the document tree.</summary>
    bool CloseAssembly(string identifier);

    /// <summary>Decompile a method to C# given assembly identifier + method token.</summary>
    string DecompileMethod(string assemblyIdentifier, uint methodToken);

    /// <summary>Save the assembly identified by ``identifier`` to ``destination`` (or its original path if null).</summary>
    void SaveAssembly(string identifier, string? destination);

    /// <summary>Get metadata about a loaded assembly.</summary>
    AssemblyInfoResult GetAssemblyInfo(string assemblyIdentifier);

    /// <summary>List types in an assembly, optionally filtered by namespace, with pagination.</summary>
    TypeListResult ListTypes(string assemblyIdentifier, string? @namespace, int offset, int limit);

    /// <summary>Get detailed info about a specific type (by full name or token).</summary>
    TypeInfoResult GetTypeInfo(string assemblyIdentifier, string typeIdentifier);

    /// <summary>Get fields of a type, optionally filtered by wildcard pattern, with pagination.</summary>
    FieldListResult GetTypeFields(string assemblyIdentifier, string typeIdentifier, string pattern, int offset, int limit);

    /// <summary>Get a single property by name from a type.</summary>
    PropertyResult GetTypeProperty(string assemblyIdentifier, string typeIdentifier, string propertyName);

    /// <summary>List methods of a type with pagination.</summary>
    MethodListResult ListMethods(string assemblyIdentifier, string typeIdentifier, int offset, int limit);

    /// <summary>Search for types by wildcard pattern across all loaded assemblies (or a specific one).</summary>
    TypeSearchResult SearchTypes(string pattern, string? scopeAssembly, int offset, int limit);

    /// <summary>Find chains of fields/properties walking from fromType to toType within the same module.</summary>
    PathSearchResult FindPathToType(string assemblyIdentifier, string fromType, string toType, int maxDepth);

    /// <summary>Get IL body of a method by token.</summary>
    MethodIlResult GetMethodIl(string assemblyIdentifier, uint methodToken);

    /// <summary>Apply a list of edits to the in-memory IL body of a method.</summary>
    PatchResult PatchMethodIl(string assemblyIdentifier, uint methodToken, System.Collections.Generic.IReadOnlyList<IlEdit> edits);

    /// <summary>Revert the snapshot taken by PatchMethodIl, restoring the original body.</summary>
    RevertResult RevertMethodIl(string assemblyIdentifier, uint methodToken);

    /// <summary>Revert every in-memory patch, optionally scoped to one assembly.</summary>
    RevertAllResult RevertAllPendingPatches(string? scopeAssembly);
}

public sealed record AssemblyHandle(string Path, string FullName, string Name);

// --- Result records for the 7 new tools ---

public sealed record AssemblyInfoResult(
    string Name, string FullName, string Path, string RuntimeVersion,
    int ModuleCount, int TypeCount);

public sealed record TypeSummary(
    string Namespace, string Name, string FullName, uint Token, string Kind);

public sealed record TypeListResult(IReadOnlyList<TypeSummary> Types, int Total, int NextOffset);

public sealed record TypeInfoResult(
    string Namespace, string Name, string FullName, uint Token, string Kind,
    string? BaseType, IReadOnlyList<string> Interfaces,
    int FieldCount, int MethodCount, int PropertyCount, int EventCount);

public sealed record FieldSummary(
    string Name, string FullName, uint Token, string FieldType, bool IsStatic);

public sealed record FieldListResult(IReadOnlyList<FieldSummary> Fields, int Total, int NextOffset);

public sealed record PropertyResult(
    string Name, string FullName, uint Token, string PropertyType,
    bool HasGetter, bool HasSetter, uint GetterToken, uint SetterToken);

public sealed record ParameterSummary(string Name, string TypeName);

public sealed record MethodSummary(
    string Name, string FullName, uint Token, string ReturnType,
    IReadOnlyList<ParameterSummary> Parameters, bool IsStatic, bool IsVirtual);

public sealed record MethodListResult(IReadOnlyList<MethodSummary> Methods, int Total, int NextOffset);

public sealed record TypeSearchHit(
    string Assembly, string Namespace, string Name, string FullName, uint Token);

public sealed record TypeSearchResult(IReadOnlyList<TypeSearchHit> Results, int Total, int NextOffset);

// --- Records for find_path_to_type ---
public sealed record PathStep(string Kind, string MemberName, uint MemberToken, string DeclaringType, string TargetType);
public sealed record TypePath(IReadOnlyList<PathStep> Steps);
public sealed record PathSearchResult(IReadOnlyList<TypePath> Paths);

// --- Records for get_method_il / patch_method_il / revert_method_il ---
public sealed record IlInstruction(int Offset, string OpCode, string? Operand);
public sealed record IlLocal(int Index, string TypeName);
public sealed record IlExceptionHandler(int TryStart, int TryEnd, int HandlerStart, int HandlerEnd, string Kind, string? CatchType);
public sealed record MethodIlResult(
    string MethodFullName, int MaxStack, bool InitLocals,
    IReadOnlyList<IlLocal> Locals, IReadOnlyList<IlInstruction> Instructions,
    IReadOnlyList<IlExceptionHandler> ExceptionHandlers);

public sealed record IlInstructionSpec(string OpCode, string? Operand);
public sealed record IlEdit(string Op, int? AtOffset, IReadOnlyList<IlInstructionSpec>? Instructions, int? Count, IReadOnlyList<string>? Locals, bool? InitLocals);
public sealed record PatchResult(bool Patched, int NewInstructionCount, bool SnapshotTaken);
public sealed record RevertResult(bool Reverted);
public sealed record RevertedMethod(string Assembly, uint Token, string MethodFullName);
public sealed record RevertAllResult(IReadOnlyList<RevertedMethod> Reverted);
