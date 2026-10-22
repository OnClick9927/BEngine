using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BEngine.Testing.Editor;

/// <summary>Instruments an unsigned assembly copy using portable PDB sequence points. Originals are never modified.</summary>
public static class PrecompiledCoverageInstrumenter
{
    public static int Instrument(string assemblyPath, string outputPath)
    {
        assemblyPath = Path.GetFullPath(assemblyPath); outputPath = Path.GetFullPath(outputPath);
        if (assemblyPath.Equals(outputPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an output copy for coverage.");
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath));
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(CodeCoverage).Assembly.Location)); resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = true, InMemory = true, AssemblyResolver = resolver });
        if (assembly.Name.HasPublicKey) throw new NotSupportedException("Coverage of signed assemblies requires re-signing with the publisher's key.");
        var module = assembly.MainModule;
        if (module.AssemblyReferences.Any(reference => reference.Name == typeof(CodeCoverage).Assembly.GetName().Name) && assembly.Name.Name.StartsWith("BEngine.Testing", StringComparison.Ordinal))
            throw new InvalidOperationException("Do not instrument the coverage framework itself.");
        var hit = module.ImportReference(typeof(CodeCoverage).GetMethod(nameof(CodeCoverage.Hit))!);
        var hitBranch = module.ImportReference(typeof(CodeCoverage).GetMethod(nameof(CodeCoverage.HitBranch))!);
        var sites = new List<(string File, int Line)>();
        var branches = new List<(string File, int Line, int Block, int Outcomes)>();
        foreach (var type in Types(module.Types)) foreach (var method in type.Methods.Where(method => method.HasBody))
        {
            var points = method.DebugInformation.SequencePoints.Where(point => !point.IsHidden && point.StartLine > 0)
                .GroupBy(point => point.Offset).ToDictionary(group => group.Key, group => group.First());
            var instructions = method.Body.Instructions.ToArray(); var il = method.Body.GetILProcessor();
            // Short relative branches may become too distant after probes are inserted.
            foreach (var instruction in instructions) ExpandBranch(instruction);
            foreach (var original in instructions)
            {
                if (!points.TryGetValue(original.Offset, out var point)) continue;
                var first = il.Create(OpCodes.Ldstr, point.Document.Url);
                Redirect(method, original, first);
                il.InsertBefore(original, first); il.InsertBefore(original, il.Create(OpCodes.Ldc_I4, point.StartLine)); il.InsertBefore(original, il.Create(OpCodes.Call, hit));
                sites.Add((point.Document.Url, point.StartLine));
            }
            foreach (var original in instructions.Where(instruction => instruction.OpCode.FlowControl == FlowControl.Cond_Branch))
            {
                var point = points.Values.Where(point => point.Offset <= original.Offset).OrderByDescending(point => point.Offset).FirstOrDefault();
                if (point is null || original.Next is null) continue;
                var next = original.Next;
                var targets = original.Operand is Instruction[] cases ? cases : original.Operand is Instruction target ? new[] { target } : [];
                if (targets.Length == 0) continue;
                var block = branches.Count; branches.Add((point.Document.Url, point.StartLine, block, targets.Length + 1));
                var taken = new Instruction[targets.Length];
                for (var outcome = 0; outcome <= targets.Length; outcome++)
                {
                    var first = il.Create(OpCodes.Ldstr, point.Document.Url);
                    il.InsertBefore(next, first); il.InsertBefore(next, il.Create(OpCodes.Ldc_I4, point.StartLine));
                    il.InsertBefore(next, il.Create(OpCodes.Ldc_I4, block)); il.InsertBefore(next, il.Create(OpCodes.Ldc_I4, outcome)); il.InsertBefore(next, il.Create(OpCodes.Call, hitBranch));
                    il.InsertBefore(next, il.Create(OpCodes.Br, outcome == 0 ? next : targets[outcome - 1]));
                    if (outcome > 0) taken[outcome - 1] = first;
                }
                original.Operand = original.Operand is Instruction[] ? taken : taken[0];
            }
            method.Body.MaxStackSize += 4;
        }
        if (sites.Count == 0) throw new InvalidDataException("Assembly has no executable portable PDB sequence points.");
        var moduleType = module.Types.Single(type => type.Name == "<Module>");
        var initializer = moduleType.Methods.FirstOrDefault(method => method.Name == ".cctor");
        if (initializer is null)
        {
            initializer = new(".cctor", Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            moduleType.Methods.Add(initializer); initializer.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        }
        var register = module.ImportReference(typeof(CodeCoverage).GetMethod(nameof(CodeCoverage.Register))!); var init = initializer.Body.GetILProcessor(); var originalStart = initializer.Body.Instructions[0];
        foreach (var group in sites.Distinct().GroupBy(site => site.File))
        {
            var lines = group.Select(site => site.Line).Distinct().ToArray();
            init.InsertBefore(originalStart, init.Create(OpCodes.Ldstr, group.Key)); init.InsertBefore(originalStart, init.Create(OpCodes.Ldc_I4, lines.Length));
            init.InsertBefore(originalStart, init.Create(OpCodes.Newarr, module.TypeSystem.Int32));
            for (var i = 0; i < lines.Length; i++)
            { init.InsertBefore(originalStart, init.Create(OpCodes.Dup)); init.InsertBefore(originalStart, init.Create(OpCodes.Ldc_I4, i)); init.InsertBefore(originalStart, init.Create(OpCodes.Ldc_I4, lines[i])); init.InsertBefore(originalStart, init.Create(OpCodes.Stelem_I4)); }
            init.InsertBefore(originalStart, init.Create(OpCodes.Call, register));
        }
        var registerBranch = module.ImportReference(typeof(CodeCoverage).GetMethod(nameof(CodeCoverage.RegisterBranch))!);
        foreach (var branch in branches)
        {
            init.InsertBefore(originalStart, init.Create(OpCodes.Ldstr, branch.File)); init.InsertBefore(originalStart, init.Create(OpCodes.Ldc_I4, branch.Line));
            init.InsertBefore(originalStart, init.Create(OpCodes.Ldc_I4, branch.Block)); init.InsertBefore(originalStart, init.Create(OpCodes.Ldc_I4, branch.Outcomes));
            init.InsertBefore(originalStart, init.Create(OpCodes.Call, registerBranch));
        }
        initializer.Body.MaxStackSize = Math.Max(initializer.Body.MaxStackSize, 6);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); assembly.Write(outputPath, new WriterParameters { WriteSymbols = true });
        return sites.Count;
    }
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; } }
    private static void Redirect(MethodDefinition method, Instruction original, Instruction replacement)
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand == original) instruction.Operand = replacement;
            else if (instruction.Operand is Instruction[] targets) for (var i = 0; i < targets.Length; i++) if (targets[i] == original) targets[i] = replacement;
        }
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            if (handler.TryStart == original) handler.TryStart = replacement; if (handler.TryEnd == original) handler.TryEnd = replacement;
            if (handler.HandlerStart == original) handler.HandlerStart = replacement; if (handler.HandlerEnd == original) handler.HandlerEnd = replacement;
            if (handler.FilterStart == original) handler.FilterStart = replacement;
        }
    }
    private static void ExpandBranch(Instruction instruction)
    {
        instruction.OpCode = instruction.OpCode.Code switch
        {
            Code.Br_S => OpCodes.Br, Code.Brfalse_S => OpCodes.Brfalse, Code.Brtrue_S => OpCodes.Brtrue, Code.Beq_S => OpCodes.Beq,
            Code.Bge_S => OpCodes.Bge, Code.Bge_Un_S => OpCodes.Bge_Un, Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un,
            Code.Ble_S => OpCodes.Ble, Code.Ble_Un_S => OpCodes.Ble_Un, Code.Blt_S => OpCodes.Blt, Code.Blt_Un_S => OpCodes.Blt_Un,
            Code.Bne_Un_S => OpCodes.Bne_Un, Code.Leave_S => OpCodes.Leave, _ => instruction.OpCode
        };
    }
}
