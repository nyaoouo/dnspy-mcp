using System.Collections.Generic;
using System.IO;

#if DNSPY
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs;
#endif

namespace DnspyMcp.Util;

#if DNSPY
[Export]
public sealed class DnSpyServices : IDnSpyServices
{
    private readonly IDocumentTabService _tabService;

    [Import(AllowDefault = true)]
#pragma warning disable CS0649
    private IDecompilerService? _decompilerService;
#pragma warning restore CS0649

    [ImportingConstructor]
    public DnSpyServices(IDocumentTabService tabService)
    {
        _tabService = tabService;
    }

    public IEnumerable<AssemblyHandle> EnumerateAssemblies()
    {
        return UiThread.Invoke(() =>
        {
            var docService = _tabService.DocumentTreeView.DocumentService;
            var result = new List<AssemblyHandle>();
            foreach (var doc in docService.GetDocuments())
            {
                var path = doc.Filename ?? string.Empty;
                var asm = doc.AssemblyDef;
                var fullName = asm?.FullName ?? path;
                var name = asm?.Name?.String ?? Path.GetFileNameWithoutExtension(path);
                result.Add(new AssemblyHandle(path, fullName, name));
            }
            return (IEnumerable<AssemblyHandle>)result;
        });
    }

    public AssemblyHandle OpenAssembly(string path)
    {
        return UiThread.Invoke(() =>
        {
            var info = DsDocumentInfo.CreateDocument(path);
            var doc = _tabService.DocumentTreeView.DocumentService.TryGetOrCreate(info, isAutoLoaded: false);
            if (doc is null)
                throw new InvalidOperationException($"Could not open document: {path}");
            var asm = doc.AssemblyDef;
            var fullName = asm?.FullName ?? path;
            var name = asm?.Name?.String ?? Path.GetFileNameWithoutExtension(path);
            return new AssemblyHandle(doc.Filename ?? path, fullName, name);
        });
    }

    public bool CloseAssembly(string identifier)
    {
        return UiThread.Invoke(() =>
        {
            var match = FindDocument(identifier);
            if (match is null) return false;
            _tabService.DocumentTreeView.DocumentService.Remove(match.Key);
            return true;
        });
    }

    public string DecompileMethod(string assemblyIdentifier, uint methodToken)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException($"Assembly has no ModuleDef: {assemblyIdentifier}");
        var resolved = module.ResolveToken(methodToken);
        var svc = _decompilerService
            ?? throw new InvalidOperationException("IDecompilerService is not available in this dnSpy build.");
        var decompiler = svc.Decompiler;
        var output = new StringBuilderDecompilerOutput();
        var ctx = new DecompilationContext();
        switch (resolved)
        {
            case MethodDef method:
                decompiler.Decompile(method, output, ctx);
                break;
            case PropertyDef prop:
                decompiler.Decompile(prop, output, ctx);
                break;
            case FieldDef field:
                decompiler.Decompile(field, output, ctx);
                break;
            case EventDef evt:
                decompiler.Decompile(evt, output, ctx);
                break;
            case TypeDef type:
                decompiler.Decompile(type, output, ctx);
                break;
            default:
                throw new InvalidOperationException(
                    $"Token 0x{methodToken:X8} did not resolve to a decompilable member (got {resolved?.GetType().Name ?? "null"}).");
        }
        return output.GetText();
    }

    public void SaveAssembly(string identifier, string? destination)
    {
        UiThread.Invoke(() =>
        {
            var doc = FindDocument(identifier)
                ?? throw new InvalidOperationException($"No matching assembly: {identifier}");
            var module = doc.ModuleDef
                ?? throw new InvalidOperationException($"Assembly has no ModuleDef: {identifier}");
            var path = destination ?? doc.Filename
                ?? throw new InvalidOperationException("No filename and no destination provided.");
            module.Write(path);
        });
    }

    public AssemblyInfoResult GetAssemblyInfo(string identifier)
    {
        var doc = FindDocument(identifier)
            ?? throw new InvalidOperationException($"No matching assembly: {identifier}");
        var asm = doc.AssemblyDef;
        var module = doc.ModuleDef;
        var moduleCount = asm?.Modules?.Count ?? (module is null ? 0 : 1);
        var typeCount = 0;
        if (module is not null)
            foreach (var _ in module.GetTypes()) typeCount++;
        return new AssemblyInfoResult(
            asm?.Name?.String ?? Path.GetFileNameWithoutExtension(doc.Filename ?? ""),
            asm?.FullName ?? doc.Filename ?? "",
            doc.Filename ?? "",
            module?.RuntimeVersion ?? "",
            moduleCount,
            typeCount);
    }

    public TypeListResult ListTypes(string assemblyIdentifier, string? @namespace, int offset, int limit)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException("No ModuleDef");
        var all = module.GetTypes()
            .Where(t => @namespace is null || t.Namespace == @namespace)
            .ToList();
        var slice = all.Skip(offset).Take(limit)
            .Select(t => new TypeSummary(t.Namespace, t.Name, t.FullName, t.MDToken.Raw, TypeKind(t)))
            .ToList();
        var next = offset + slice.Count < all.Count ? offset + slice.Count : -1;
        return new TypeListResult(slice, all.Count, next);
    }

    public TypeInfoResult GetTypeInfo(string assemblyIdentifier, string typeIdentifier)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException("No ModuleDef");
        var t = FindType(module, typeIdentifier)
            ?? throw new InvalidOperationException($"Type not found: {typeIdentifier}");
        var interfaces = t.Interfaces.Select(i => i.Interface?.FullName ?? "").ToList();
        return new TypeInfoResult(
            t.Namespace,
            t.Name,
            t.FullName,
            t.MDToken.Raw,
            TypeKind(t),
            t.BaseType?.FullName,
            interfaces,
            t.Fields.Count,
            t.Methods.Count,
            t.Properties.Count,
            t.Events.Count);
    }

    public FieldListResult GetTypeFields(string assemblyIdentifier, string typeIdentifier, string pattern, int offset, int limit)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException("No ModuleDef");
        var t = FindType(module, typeIdentifier)
            ?? throw new InvalidOperationException($"Type not found: {typeIdentifier}");
        var all = t.Fields
            .Where(f => WildcardMatch(f.Name, pattern))
            .ToList();
        var slice = all.Skip(offset).Take(limit)
            .Select(f => new FieldSummary(
                f.Name,
                $"{t.FullName}::{f.Name}",
                f.MDToken.Raw,
                f.FieldType?.FullName ?? "",
                f.IsStatic))
            .ToList();
        var next = offset + slice.Count < all.Count ? offset + slice.Count : -1;
        return new FieldListResult(slice, all.Count, next);
    }

    public PropertyResult GetTypeProperty(string assemblyIdentifier, string typeIdentifier, string propertyName)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException("No ModuleDef");
        var t = FindType(module, typeIdentifier)
            ?? throw new InvalidOperationException($"Type not found: {typeIdentifier}");
        var prop = t.Properties.FirstOrDefault(p => p.Name == propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on type '{t.FullName}'");
        var getterToken = prop.GetMethod?.MDToken.Raw ?? 0u;
        var setterToken = prop.SetMethod?.MDToken.Raw ?? 0u;
        return new PropertyResult(
            prop.Name,
            $"{t.FullName}::{prop.Name}",
            prop.MDToken.Raw,
            prop.PropertySig?.RetType?.FullName ?? "",
            prop.GetMethod is not null,
            prop.SetMethod is not null,
            getterToken,
            setterToken);
    }

    public MethodListResult ListMethods(string assemblyIdentifier, string typeIdentifier, int offset, int limit)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException("No ModuleDef");
        var t = FindType(module, typeIdentifier)
            ?? throw new InvalidOperationException($"Type not found: {typeIdentifier}");
        var all = t.Methods.ToList();
        var slice = all.Skip(offset).Take(limit)
            .Select(m =>
            {
                var parms = m.Parameters
                    .Where(p => p.IsNormalMethodParameter)
                    .Select(p => new ParameterSummary(p.Name ?? "", p.Type?.FullName ?? ""))
                    .ToList();
                return new MethodSummary(
                    m.Name,
                    $"{t.FullName}::{m.Name}",
                    m.MDToken.Raw,
                    m.ReturnType?.FullName ?? "",
                    parms,
                    m.IsStatic,
                    m.IsVirtual);
            })
            .ToList();
        var next = offset + slice.Count < all.Count ? offset + slice.Count : -1;
        return new MethodListResult(slice, all.Count, next);
    }

    public TypeSearchResult SearchTypes(string pattern, string? scopeAssembly, int offset, int limit)
    {
        return UiThread.Invoke(() =>
        {
            var docService = _tabService.DocumentTreeView.DocumentService;
            var docs = docService.GetDocuments();
            if (scopeAssembly is not null)
            {
                var scoped = FindDocument(scopeAssembly);
                docs = scoped is null ? Array.Empty<IDsDocument>() : new[] { scoped };
            }

            var all = new List<TypeSearchHit>();
            foreach (var doc in docs)
            {
                var module = doc.ModuleDef;
                if (module is null) continue;
                var asmName = doc.AssemblyDef?.Name?.String ?? Path.GetFileNameWithoutExtension(doc.Filename ?? "");
                foreach (var t in module.GetTypes())
                {
                    if (WildcardMatch(t.FullName, pattern) || WildcardMatch(t.Name, pattern))
                    {
                        all.Add(new TypeSearchHit(asmName, t.Namespace, t.Name, t.FullName, t.MDToken.Raw));
                    }
                }
            }

            var slice = all.Skip(offset).Take(limit).ToList();
            var next = offset + slice.Count < all.Count ? offset + slice.Count : -1;
            return new TypeSearchResult(slice, all.Count, next);
        });
    }

    private IDsDocument? FindDocument(string identifier)
    {
        var docService = _tabService.DocumentTreeView.DocumentService;
        foreach (var doc in docService.GetDocuments())
        {
            if (string.Equals(doc.Filename, identifier, StringComparison.OrdinalIgnoreCase)) return doc;
            var asm = doc.AssemblyDef;
            if (asm is null) continue;
            if (string.Equals(asm.FullName, identifier, StringComparison.Ordinal)) return doc;
            if (string.Equals(asm.Name?.String, identifier, StringComparison.Ordinal)) return doc;
        }
        return null;
    }

    private static TypeDef? FindType(ModuleDef module, string identifier)
    {
        // Try as token (decimal or 0x-prefixed hex)
        if (TryParseToken(identifier, out var token))
        {
            if (module.ResolveToken(token) is TypeDef td) return td;
        }
        // Try as full name (e.g. "System.String")
        foreach (var t in module.GetTypes())
        {
            if (t.FullName == identifier) return t;
        }
        // Short name fallback
        foreach (var t in module.GetTypes())
        {
            if (t.Name == identifier) return t;
        }
        return null;
    }

    private static bool TryParseToken(string s, out uint token)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out token);
        return uint.TryParse(s, out token);
    }

    private static string TypeKind(TypeDef t)
    {
        if (t.IsInterface) return "interface";
        if (t.IsEnum) return "enum";
        if (t.IsValueType) return "struct";
        if (t.BaseType?.FullName == "System.MulticastDelegate" || t.BaseType?.FullName == "System.Delegate")
            return "delegate";
        return "class";
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        if (pattern == "*") return true;
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase);
    }

    // --- Snapshot store for patch/revert ---
    private readonly object _snapLock = new object();
    private readonly Dictionary<(string asm, uint token), (List<Instruction> ins, List<Local> locs, bool init)> _snapshots
        = new Dictionary<(string asm, uint token), (List<Instruction>, List<Local>, bool)>();

    public PathSearchResult FindPathToType(string assemblyIdentifier, string fromType, string toType, int maxDepth)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException($"Assembly has no ModuleDef: {assemblyIdentifier}");
        var startType = FindType(module, fromType)
            ?? throw new InvalidOperationException($"No type '{fromType}' in {assemblyIdentifier}");
        var goalType = FindType(module, toType);
        var goal = goalType?.FullName ?? toType;

        var paths = new List<TypePath>();
        // Queue entries: (currentType, steps so far, visited type names in this branch)
        var queue = new Queue<(TypeDef cur, List<PathStep> steps, HashSet<string> visited)>();
        queue.Enqueue((startType, new List<PathStep>(), new HashSet<string> { startType.FullName }));

        while (queue.Count > 0 && paths.Count < 10)
        {
            var (cur, steps, visited) = queue.Dequeue();
            if (steps.Count >= maxDepth) continue;

            // Walk fields
            foreach (var f in cur.Fields)
            {
                if (paths.Count >= 10) break;
                var sig = f.FieldType;
                if (sig is null) continue;
                var elemType = sig.GetElementType();
                if (elemType != ElementType.Class && elemType != ElementType.ValueType) continue;
                var targetName = sig.FullName ?? "";
                var step = new PathStep("field", f.Name, f.MDToken.Raw, cur.FullName, targetName);
                var newSteps = new List<PathStep>(steps) { step };
                if (targetName == goal) { paths.Add(new TypePath(newSteps)); continue; }
                if (!visited.Contains(targetName))
                {
                    var nextType = FindType(module, targetName);
                    if (nextType is not null)
                    {
                        var newVisited = new HashSet<string>(visited) { targetName };
                        queue.Enqueue((nextType, newSteps, newVisited));
                    }
                }
            }

            // Walk properties
            foreach (var p in cur.Properties)
            {
                if (paths.Count >= 10) break;
                var retType = p.PropertySig?.RetType;
                if (retType is null) continue;
                var targetName = retType.FullName ?? "";
                var step = new PathStep("property", p.Name, p.MDToken.Raw, cur.FullName, targetName);
                var newSteps = new List<PathStep>(steps) { step };
                if (targetName == goal) { paths.Add(new TypePath(newSteps)); continue; }
                if (!visited.Contains(targetName))
                {
                    var nextType = FindType(module, targetName);
                    if (nextType is not null)
                    {
                        var newVisited = new HashSet<string>(visited) { targetName };
                        queue.Enqueue((nextType, newSteps, newVisited));
                    }
                }
            }
        }

        return new PathSearchResult(paths);
    }

    public MethodIlResult GetMethodIl(string assemblyIdentifier, uint methodToken)
    {
        var doc = FindDocument(assemblyIdentifier)
            ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
        var module = doc.ModuleDef
            ?? throw new InvalidOperationException($"Assembly has no ModuleDef: {assemblyIdentifier}");
        var method = module.ResolveToken(methodToken) as MethodDef
            ?? throw new InvalidOperationException($"Token 0x{methodToken:X8} is not a MethodDef");
        var body = method.Body
            ?? throw new InvalidOperationException("Method has no IL body (abstract/extern).");

        var locals = body.Variables
            .Select((v, i) => new IlLocal(i, v.Type?.FullName ?? "?"))
            .ToList();
        var instructions = body.Instructions
            .Select(ins => new IlInstruction((int)ins.Offset, ins.OpCode.Name, FormatOperand(ins)))
            .ToList();
        var handlers = body.ExceptionHandlers
            .Select(eh => new IlExceptionHandler(
                (int)(eh.TryStart?.Offset ?? 0),
                (int)(eh.TryEnd?.Offset ?? 0),
                (int)(eh.HandlerStart?.Offset ?? 0),
                (int)(eh.HandlerEnd?.Offset ?? 0),
                eh.HandlerType.ToString(),
                eh.CatchType?.FullName))
            .ToList();
        return new MethodIlResult(method.FullName, body.MaxStack, body.InitLocals, locals, instructions, handlers);
    }

    private static string? FormatOperand(Instruction ins)
    {
        switch (ins.Operand)
        {
            case null:
                return null;
            case string s:
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            case int i:
                return i.ToString();
            case long l:
                return l.ToString();
            case sbyte sb:
                return sb.ToString();
            case float f:
                return f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case double d:
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case Instruction target:
                return $"@{target.Offset}";
            case Instruction[] targets:
                return "[" + string.Join(", ", targets.Select(t => $"@{t.Offset}")) + "]";
            case Local local:
                return $"local:{local.Index}";
            case Parameter param:
                return $"arg:{param.MethodSigIndex}";
            case MethodSig sig:
                return $"sig:{sig}";
            case IMDTokenProvider t:
                return $"0x{t.MDToken.Raw:X8}";
            default:
                return ins.Operand.ToString();
        }
    }

    public PatchResult PatchMethodIl(string assemblyIdentifier, uint methodToken, IReadOnlyList<IlEdit> edits)
    {
        return UiThread.Invoke(() =>
        {
            var doc = FindDocument(assemblyIdentifier)
                ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
            var module = doc.ModuleDef
                ?? throw new InvalidOperationException($"Assembly has no ModuleDef: {assemblyIdentifier}");
            var method = module.ResolveToken(methodToken) as MethodDef
                ?? throw new InvalidOperationException($"Token 0x{methodToken:X8} is not a MethodDef");
            var body = method.Body
                ?? throw new InvalidOperationException("Method has no IL body (abstract/extern).");

            bool tookSnap;
            lock (_snapLock)
            {
                var key = (assemblyIdentifier, methodToken);
                if (!_snapshots.ContainsKey(key))
                {
                    _snapshots[key] = (
                        body.Instructions.ToList(),
                        body.Variables.ToList(),
                        body.InitLocals);
                    tookSnap = true;
                }
                else
                {
                    tookSnap = false;
                }
            }

            foreach (var edit in edits)
                ApplyEdit(module, method, body, edit);

            body.UpdateInstructionOffsets();
            return new PatchResult(true, body.Instructions.Count, tookSnap);
        });
    }

    public RevertResult RevertMethodIl(string assemblyIdentifier, uint methodToken)
    {
        return UiThread.Invoke(() =>
        {
            var doc = FindDocument(assemblyIdentifier)
                ?? throw new InvalidOperationException($"No matching assembly: {assemblyIdentifier}");
            var module = doc.ModuleDef
                ?? throw new InvalidOperationException($"Assembly has no ModuleDef: {assemblyIdentifier}");
            var method = module.ResolveToken(methodToken) as MethodDef
                ?? throw new InvalidOperationException($"Token 0x{methodToken:X8} is not a MethodDef");
            var body = method.Body
                ?? throw new InvalidOperationException("Method has no IL body.");

            lock (_snapLock)
            {
                var key = (assemblyIdentifier, methodToken);
                if (!_snapshots.TryGetValue(key, out var snap))
                    throw new InvalidOperationException("no patch to revert for this method");
                body.Instructions.Clear();
                foreach (var i in snap.ins) body.Instructions.Add(i);
                body.Variables.Clear();
                foreach (var v in snap.locs) body.Variables.Add(v);
                body.InitLocals = snap.init;
                _snapshots.Remove(key);
            }
            body.UpdateInstructionOffsets();
            return new RevertResult(true);
        });
    }

    private static void ApplyEdit(ModuleDef module, MethodDef method, CilBody body, IlEdit edit)
    {
        switch (edit.Op)
        {
            case "insert":
            {
                var idx = FindInstructionIndex(body, edit.AtOffset
                    ?? throw new InvalidOperationException("insert requires at_offset"));
                var parsed = ParseInstructions(module, method, body, edit.Instructions ?? Array.Empty<IlInstructionSpec>());
                for (int i = 0; i < parsed.Count; i++)
                    body.Instructions.Insert(idx + i, parsed[i]);
                break;
            }
            case "replace":
            {
                var idx = FindInstructionIndex(body, edit.AtOffset
                    ?? throw new InvalidOperationException("replace requires at_offset"));
                var count = edit.Count ?? 1;
                for (int i = 0; i < count && idx < body.Instructions.Count; i++)
                    body.Instructions.RemoveAt(idx);
                var parsed = ParseInstructions(module, method, body, edit.Instructions ?? Array.Empty<IlInstructionSpec>());
                for (int i = 0; i < parsed.Count; i++)
                    body.Instructions.Insert(idx + i, parsed[i]);
                break;
            }
            case "remove":
            {
                var idx = FindInstructionIndex(body, edit.AtOffset
                    ?? throw new InvalidOperationException("remove requires at_offset"));
                var count = edit.Count ?? 1;
                for (int i = 0; i < count && idx < body.Instructions.Count; i++)
                    body.Instructions.RemoveAt(idx);
                break;
            }
            case "set_locals":
            {
                var locs = edit.Locals ?? throw new InvalidOperationException("set_locals requires locals");
                body.Variables.Clear();
                foreach (var typeName in locs)
                {
                    var sig = ResolveTypeSig(module, typeName)
                        ?? throw new InvalidOperationException($"Cannot resolve type '{typeName}' for local variable");
                    body.Variables.Add(new Local(sig));
                }
                break;
            }
            case "set_init_locals":
            {
                body.InitLocals = edit.InitLocals
                    ?? throw new InvalidOperationException("set_init_locals requires init_locals");
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown edit op: {edit.Op}");
        }
    }

    private static int FindInstructionIndex(CilBody body, int offset)
    {
        for (int i = 0; i < body.Instructions.Count; i++)
        {
            if ((int)body.Instructions[i].Offset == offset) return i;
        }
        throw new InvalidOperationException($"No instruction at offset {offset}");
    }

    private static List<Instruction> ParseInstructions(ModuleDef module, MethodDef method, CilBody body, IReadOnlyList<IlInstructionSpec> specs)
    {
        var result = new List<Instruction>(specs.Count);
        foreach (var spec in specs)
            result.Add(ParseInstruction(module, method, body, spec));
        return result;
    }

    private static readonly Dictionary<string, OpCode> _opCodeMap = BuildOpCodeMap();

    private static Dictionary<string, OpCode> BuildOpCodeMap()
    {
        var map = new Dictionary<string, OpCode>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode op)
                map[op.Name] = op;
        }
        return map;
    }

    private static Instruction ParseInstruction(ModuleDef module, MethodDef method, CilBody body, IlInstructionSpec spec)
    {
        if (!_opCodeMap.TryGetValue(spec.OpCode, out var opCode))
            throw new InvalidOperationException($"Unknown opcode: {spec.OpCode}");

        var operandType = opCode.OperandType;
        var operandStr = spec.Operand;

        switch (operandType)
        {
            case OperandType.InlineNone:
                return new Instruction(opCode);

            case OperandType.InlineI:
            {
                if (!int.TryParse(operandStr, out var n))
                    throw new InvalidOperationException($"Expected integer operand for {spec.OpCode}, got: {operandStr}");
                return new Instruction(opCode, n);
            }
            case OperandType.ShortInlineI:
            {
                if (!sbyte.TryParse(operandStr, out var n))
                    throw new InvalidOperationException($"Expected sbyte operand for {spec.OpCode}, got: {operandStr}");
                return new Instruction(opCode, n);
            }
            case OperandType.InlineI8:
            {
                if (!long.TryParse(operandStr, out var n))
                    throw new InvalidOperationException($"Expected long operand for {spec.OpCode}, got: {operandStr}");
                return new Instruction(opCode, n);
            }
            case OperandType.InlineR:
            {
                if (!double.TryParse(operandStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    throw new InvalidOperationException($"Expected double operand for {spec.OpCode}, got: {operandStr}");
                return new Instruction(opCode, n);
            }
            case OperandType.ShortInlineR:
            {
                if (!float.TryParse(operandStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    throw new InvalidOperationException($"Expected float operand for {spec.OpCode}, got: {operandStr}");
                return new Instruction(opCode, n);
            }
            case OperandType.InlineString:
            {
                // Strip surrounding quotes if present
                var s = operandStr ?? "";
                if (s.StartsWith("\"") && s.EndsWith("\""))
                    s = s.Substring(1, s.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                return new Instruction(opCode, s);
            }
            case OperandType.InlineBrTarget:
            case OperandType.ShortInlineBrTarget:
            {
                var targetOffset = ParseOffsetOperand(operandStr, spec.OpCode);
                var target = body.Instructions.FirstOrDefault(i => (int)i.Offset == targetOffset);
                if (target is null)
                    throw new InvalidOperationException($"No instruction at offset {targetOffset} for branch target in {spec.OpCode}");
                return new Instruction(opCode, target);
            }
            case OperandType.InlineSwitch:
            {
                // Operand is a JSON-style array of "@offset" strings, e.g. "[@0, @10, @20]"
                var targets = ParseSwitchTargets(operandStr, spec.OpCode, body);
                return new Instruction(opCode, targets);
            }
            case OperandType.InlineVar:
            case OperandType.ShortInlineVar:
            {
                // Operand is "local:N" or "arg:N"
                return ParseVarInstruction(opCode, operandStr, spec.OpCode, body, method);
            }
            case OperandType.InlineSig:
                throw new InvalidOperationException("calli/InlineSig instruction patching not supported");
            case OperandType.InlineMethod:
            case OperandType.InlineField:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            {
                var token = ParseTokenOperand(operandStr, spec.OpCode);
                var resolved = module.ResolveToken(token)
                    ?? throw new InvalidOperationException($"Token 0x{token:X8} not found in module");
                return resolved switch
                {
                    IMethod m => new Instruction(opCode, m),
                    IField f => new Instruction(opCode, f),
                    ITypeDefOrRef t => new Instruction(opCode, t),
                    _ => throw new InvalidOperationException($"Token 0x{token:X8} resolved to unsupported type: {resolved.GetType().Name}"),
                };
            }
            default:
                throw new InvalidOperationException($"unsupported opcode/operand combination: {spec.OpCode} (OperandType={operandType})");
        }
    }

    private static Instruction[] ParseSwitchTargets(string? operandStr, string opCodeName, CilBody body)
    {
        if (operandStr is null)
            throw new InvalidOperationException($"switch opcode requires an operand");
        // Accept "[@off1, @off2, ...]" or "@off1,@off2,..."
        var s = operandStr.Trim();
        if (s.StartsWith("[")) s = s.Substring(1);
        if (s.EndsWith("]")) s = s.Substring(0, s.Length - 1);
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<Instruction>();
        var parts = s.Split(',');
        var targets = new Instruction[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var offset = ParseOffsetOperand(parts[i].Trim(), opCodeName);
            var target = body.Instructions.FirstOrDefault(ins => (int)ins.Offset == offset);
            if (target is null)
                throw new InvalidOperationException($"No instruction at offset {offset} for switch target in {opCodeName}");
            targets[i] = target;
        }
        return targets;
    }

    private static Instruction ParseVarInstruction(OpCode opCode, string? operandStr, string opCodeName, CilBody body, MethodDef method)
    {
        if (operandStr is null)
            throw new InvalidOperationException($"Opcode {opCodeName} requires an operand like 'local:N' or 'arg:N'");
        if (operandStr.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(operandStr.Substring(6), out var idx))
                throw new InvalidOperationException($"Invalid local index in operand: {operandStr}");
            if (idx < 0 || idx >= body.Variables.Count)
                throw new InvalidOperationException($"Local index {idx} out of range (have {body.Variables.Count} locals)");
            return new Instruction(opCode, body.Variables[idx]);
        }
        if (operandStr.StartsWith("arg:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(operandStr.Substring(4), out var idx))
                throw new InvalidOperationException($"Invalid arg index in operand: {operandStr}");
            // Parameters includes the implicit 'this' for instance methods; MethodSigIndex is the 0-based sig index
            var param = method.Parameters.FirstOrDefault(p => p.MethodSigIndex == idx)
                ?? throw new InvalidOperationException($"Arg index {idx} out of range for method {method.FullName}");
            return new Instruction(opCode, param);
        }
        throw new InvalidOperationException($"Opcode {opCodeName} requires operand like 'local:N' or 'arg:N', got: {operandStr}");
    }

    private static int ParseOffsetOperand(string? operandStr, string opCodeName)
    {
        if (operandStr is null)
            throw new InvalidOperationException($"Branch opcode {opCodeName} requires an operand");
        // Accept "@N" or plain integer
        var s = operandStr.StartsWith("@") ? operandStr.Substring(1) : operandStr;
        if (!int.TryParse(s, out var offset))
            throw new InvalidOperationException($"Expected offset operand for {opCodeName}, got: {operandStr}");
        return offset;
    }

    private static uint ParseTokenOperand(string? operandStr, string opCodeName)
    {
        if (operandStr is null)
            throw new InvalidOperationException($"Token opcode {opCodeName} requires an operand");
        if (operandStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(operandStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var tok))
                return tok;
        }
        if (uint.TryParse(operandStr, out var tok2)) return tok2;
        throw new InvalidOperationException($"Expected hex token operand for {opCodeName}, got: {operandStr}");
    }

    private static TypeSig? ResolveTypeSig(ModuleDef module, string typeName)
    {
        // Try corlib types first
        var corlib = module.CorLibTypes;
        switch (typeName)
        {
            case "System.Boolean": return corlib.Boolean;
            case "System.Byte": return corlib.Byte;
            case "System.SByte": return corlib.SByte;
            case "System.Int16": return corlib.Int16;
            case "System.UInt16": return corlib.UInt16;
            case "System.Int32": return corlib.Int32;
            case "System.UInt32": return corlib.UInt32;
            case "System.Int64": return corlib.Int64;
            case "System.UInt64": return corlib.UInt64;
            case "System.Single": return corlib.Single;
            case "System.Double": return corlib.Double;
            case "System.Char": return corlib.Char;
            case "System.String": return corlib.String;
            case "System.Object": return corlib.Object;
            case "System.Void": return corlib.Void;
            case "System.IntPtr": return corlib.IntPtr;
            case "System.UIntPtr": return corlib.UIntPtr;
        }
        // Try to find in module
        var typeDef = FindType(module, typeName);
        if (typeDef is not null)
            return typeDef.ToTypeSig();
        return null;
    }

    public RevertAllResult RevertAllPendingPatches(string? scopeAssembly)
    {
        return UiThread.Invoke(() =>
        {
            var reverted = new List<RevertedMethod>();
            lock (_snapLock)
            {
                var keys = _snapshots.Keys.ToList();
                foreach (var (asm, token) in keys)
                {
                    if (scopeAssembly is not null && !asm.Equals(scopeAssembly, StringComparison.Ordinal))
                        continue;
                    try
                    {
                        var doc = FindDocument(asm);
                        var method = doc?.ModuleDef?.ResolveToken(token) as MethodDef;
                        if (doc is null || method?.Body is null) { _snapshots.Remove((asm, token)); continue; }
                        var body = method.Body;
                        var snap = _snapshots[(asm, token)];
                        body.Instructions.Clear();
                        foreach (var i in snap.ins) body.Instructions.Add(i);
                        body.Variables.Clear();
                        foreach (var v in snap.locs) body.Variables.Add(v);
                        body.InitLocals = snap.init;
                        body.UpdateInstructionOffsets();
                        _snapshots.Remove((asm, token));
                        reverted.Add(new RevertedMethod(asm, token, method.FullName));
                    }
                    catch
                    {
                        // Skip individual failures; continue draining the snapshot list.
                    }
                }
            }
            return new RevertAllResult(reverted);
        });
    }
}
#else
public sealed class DnSpyServices : IDnSpyServices
{
    public IEnumerable<AssemblyHandle> EnumerateAssemblies() =>
        throw new System.NotImplementedException(
            "DnSpyServices.EnumerateAssemblies requires wiring to dnSpy's IDocumentTreeView.");

    public AssemblyHandle OpenAssembly(string path) =>
        throw new System.NotImplementedException(
            "DnSpyServices.OpenAssembly requires wiring to dnSpy's IDocumentTabService.");

    public bool CloseAssembly(string identifier) =>
        throw new System.NotImplementedException(
            "DnSpyServices.CloseAssembly requires wiring to dnSpy's IDocumentTreeView.");

    public string DecompileMethod(string assemblyIdentifier, uint methodToken) =>
        throw new System.NotImplementedException(
            "DnSpyServices.DecompileMethod requires wiring to dnSpy's IDecompilerService.");

    public void SaveAssembly(string identifier, string? destination) =>
        throw new System.NotImplementedException(
            "DnSpyServices.SaveAssembly requires wiring to dnSpy's module write API.");

    public AssemblyInfoResult GetAssemblyInfo(string assemblyIdentifier) =>
        throw new System.NotImplementedException(
            "DnSpyServices.GetAssemblyInfo requires wiring to dnSpy's document service.");

    public TypeListResult ListTypes(string assemblyIdentifier, string? @namespace, int offset, int limit) =>
        throw new System.NotImplementedException(
            "DnSpyServices.ListTypes requires wiring to dnSpy's document service.");

    public TypeInfoResult GetTypeInfo(string assemblyIdentifier, string typeIdentifier) =>
        throw new System.NotImplementedException(
            "DnSpyServices.GetTypeInfo requires wiring to dnSpy's document service.");

    public FieldListResult GetTypeFields(string assemblyIdentifier, string typeIdentifier, string pattern, int offset, int limit) =>
        throw new System.NotImplementedException(
            "DnSpyServices.GetTypeFields requires wiring to dnSpy's document service.");

    public PropertyResult GetTypeProperty(string assemblyIdentifier, string typeIdentifier, string propertyName) =>
        throw new System.NotImplementedException(
            "DnSpyServices.GetTypeProperty requires wiring to dnSpy's document service.");

    public MethodListResult ListMethods(string assemblyIdentifier, string typeIdentifier, int offset, int limit) =>
        throw new System.NotImplementedException(
            "DnSpyServices.ListMethods requires wiring to dnSpy's document service.");

    public TypeSearchResult SearchTypes(string pattern, string? scopeAssembly, int offset, int limit) =>
        throw new System.NotImplementedException(
            "DnSpyServices.SearchTypes requires wiring to dnSpy's document service.");

    public PathSearchResult FindPathToType(string assemblyIdentifier, string fromType, string toType, int maxDepth) =>
        throw new System.NotImplementedException(
            "DnSpyServices.FindPathToType requires wiring to dnSpy's document service.");

    public MethodIlResult GetMethodIl(string assemblyIdentifier, uint methodToken) =>
        throw new System.NotImplementedException(
            "DnSpyServices.GetMethodIl requires wiring to dnSpy's document service.");

    public PatchResult PatchMethodIl(string assemblyIdentifier, uint methodToken, System.Collections.Generic.IReadOnlyList<IlEdit> edits) =>
        throw new System.NotImplementedException(
            "DnSpyServices.PatchMethodIl requires wiring to dnSpy's document service.");

    public RevertResult RevertMethodIl(string assemblyIdentifier, uint methodToken) =>
        throw new System.NotImplementedException(
            "DnSpyServices.RevertMethodIl requires wiring to dnSpy's document service.");

    public RevertAllResult RevertAllPendingPatches(string? scopeAssembly) =>
        throw new System.NotImplementedException(
            "DnSpyServices.RevertAllPendingPatches requires wiring to dnSpy's document service.");
}
#endif
