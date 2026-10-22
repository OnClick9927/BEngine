using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

// Veldrid 4.9.0 compiles VulkanUtil.CheckResult calls out of release builds.
// Retain each VkResult on the evaluation stack and check it before any handle is used.
if (args.Length is < 1 or > 2) throw new ArgumentException("Expected a copied Veldrid.dll path and an optional dependency directory.");
var path = Path.GetFullPath(args[0]);
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
if (args.Length == 2) resolver.AddSearchDirectory(Path.GetFullPath(args[1]));
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
if (assembly.Name.Name != "Veldrid" || assembly.Name.Version != new Version(4, 9, 0, 0))
    throw new InvalidOperationException("The Vulkan safety patch must be reviewed for this Veldrid version.");
var module = assembly.MainModule;
var check = module.Types.Single(type => type.FullName == "Veldrid.Vk.VulkanUtil")
    .Methods.Single(method => method.Name == "CheckResult");
var patched = 0; var checkedCalls = 0;
var required = new HashSet<string> { "vkAllocateMemory", "vkCreateImage", "vkBindImageMemory" };
foreach (var type in AllTypes(module.Types))
foreach (var method in type.Methods.Where(method => method.HasBody))
{
    var processor = method.Body.GetILProcessor();
    method.Body.SimplifyMacros();
    foreach (var instruction in method.Body.Instructions.ToArray())
    {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
        if (instruction.Operand is not MethodReference target || target.ReturnType.FullName != "Vulkan.VkResult") continue;
        var operation = target.Name == "Invoke" ? target.DeclaringType.Name : target.Name;
        // Waits, enumeration and presentation intentionally handle Timeout/Incomplete/Suboptimal.
        if (!operation.Contains("vkCreate") && !operation.Contains("vkAllocate") && !operation.Contains("vkBind") &&
            !operation.Contains("vkMapMemory") && !operation.Contains("vkQueueSubmit") &&
            !operation.Contains("vkBeginCommandBuffer") && !operation.Contains("vkEndCommandBuffer")) continue;
        required.Remove(target.Name);
        checkedCalls++;
        if (instruction.Next?.OpCode == OpCodes.Dup && instruction.Next.Next?.Operand is MethodReference existing &&
            existing.FullName == check.FullName) continue;
        var duplicate = processor.Create(OpCodes.Dup);
        processor.InsertAfter(instruction, duplicate);
        processor.InsertAfter(duplicate, processor.Create(OpCodes.Call, check));
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize + 1, 16);
        patched++;
    }
    method.Body.OptimizeMacros();
}
if (checkedCalls < 20 || required.Count != 0)
    throw new InvalidOperationException($"Unexpected Veldrid Vulkan call surface ({checkedCalls} calls, missing {string.Join(",", required)}); safety patch aborted.");
// ClearColorTargetCore defers its clear until a render pass starts. CopyTextureCore
// only ends an existing pass, so reading an otherwise empty frame copies stale pixels.
// Commit clears immediately, including when no draw follows the clear.
var commandList = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkCommandList");
var ensurePass = commandList.Methods.Single(method => method.Name == "EnsureRenderPassActive");
var clearMethods = commandList.Methods.Where(method => method.Name is "ClearColorTargetCore" or "ClearDepthStencilCore").ToArray();
if (clearMethods.Length != 2) throw new InvalidOperationException("Unexpected Vulkan clear surface.");
// One RHI Clear(Color | Depth) is an atomic call with no draw/copy between the
// two attachments. Queue its two values together so BeginCurrentRenderPass can
// select loadOp=CLEAR for both. Ordinary Veldrid clears remain immediate below.
// Private method copies avoid a persistent "defer" flag that could leak after
// an exception, and the finally commits a completed color clear if depth fails.
const string combinedClearName = "BEngineClearColorDepth";
var combinedClear = commandList.Methods.SingleOrDefault(method => method.Name == combinedClearName);
if (combinedClear is null)
{
    var colorClear = clearMethods.Single(method => method.Name == "ClearColorTargetCore");
    var depthClear = clearMethods.Single(method => method.Name == "ClearDepthStencilCore");
    var deferredColor = CloneClearWithoutCommit(colorClear, "BEngineDeferredColorClear", ensurePass);
    var deferredDepth = CloneClearWithoutCommit(depthClear, "BEngineDeferredDepthClear", ensurePass);
    combinedClear = new MethodDefinition(combinedClearName, MethodAttributes.Public | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    foreach (var parameter in colorClear.Parameters.Concat(depthClear.Parameters))
        combinedClear.Parameters.Add(new(parameter.Name, parameter.Attributes, parameter.ParameterType));
    commandList.Methods.Add(combinedClear);
    var il = combinedClear.Body.GetILProcessor();
    var start = il.Create(OpCodes.Ldarg_0);
    var commit = il.Create(OpCodes.Ldarg_0);
    var done = il.Create(OpCodes.Ret);
    il.Append(start); il.Append(il.Create(OpCodes.Ldarg_1)); il.Append(il.Create(OpCodes.Ldarg_2));
    il.Append(il.Create(OpCodes.Call, deferredColor));
    il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldarg_3));
    il.Append(il.Create(OpCodes.Ldarg, combinedClear.Parameters[3]));
    il.Append(il.Create(OpCodes.Call, deferredDepth)); il.Append(il.Create(OpCodes.Leave, done));
    il.Append(commit); il.Append(il.Create(OpCodes.Call, ensurePass)); il.Append(il.Create(OpCodes.Endfinally));
    il.Append(done);
    combinedClear.Body.ExceptionHandlers.Add(new(ExceptionHandlerType.Finally)
    { TryStart = start, TryEnd = commit, HandlerStart = commit, HandlerEnd = done });
    combinedClear.Body.MaxStackSize = 8;
    combinedClear.Body.OptimizeMacros(); patched += 3;
}
else if (!combinedClear.IsPublic || combinedClear.Parameters.Count != 4 ||
         combinedClear.Parameters[0].ParameterType.MetadataType != MetadataType.UInt32 ||
         combinedClear.Parameters[1].ParameterType.FullName != "Veldrid.RgbaFloat" ||
         combinedClear.Parameters[2].ParameterType.MetadataType != MetadataType.Single ||
         combinedClear.Parameters[3].ParameterType.MetadataType != MetadataType.Byte ||
         combinedClear.Body.ExceptionHandlers.Count != 1 ||
         combinedClear.Body.ExceptionHandlers[0].HandlerType != ExceptionHandlerType.Finally ||
         !combinedClear.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method &&
             method.FullName == ensurePass.FullName) ||
         commandList.Methods.Count(method => method.Name is "BEngineDeferredColorClear" or "BEngineDeferredDepthClear") != 2)
    throw new InvalidOperationException("The Vulkan combined-clear patch is incomplete.");
foreach (var method in clearMethods)
{
    method.Body.SimplifyMacros();
    var processor = method.Body.GetILProcessor();
    foreach (var instruction in method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray())
    {
        if (instruction.Previous?.Operand is MethodReference existing && existing.FullName == ensurePass.FullName) continue;
        processor.InsertBefore(instruction, processor.Create(OpCodes.Ldarg_0));
        processor.InsertBefore(instruction, processor.Create(OpCodes.Call, ensurePass));
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 16);
        patched++;
    }
    method.Body.OptimizeMacros();
}
// Framebuffer attachments may keep DepthStencilAttachmentOptimal across
// passes. The upstream external dependency covers color only, so a preceding
// depth write is not made visible to the next pass's depth LOAD/test. Include
// both early and late tests (also attachment clears), including stencil access.
// This permits an attachment that is never sampled to stay in its native
// layout; sampled depth still retains its existing image-layout barriers.
var framebuffer = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkFramebuffer");
var framebufferConstructor = framebuffer.Methods.Single(method => method.IsConstructor && !method.IsStatic);
framebufferConstructor.Body.SimplifyMacros();
const int colorStage = 0x400, depthStages = 0x100 | 0x200;
const int colorAccess = 0x80 | 0x100, depthAccess = 0x200 | 0x400;
var framebufferIl = framebufferConstructor.Body.GetILProcessor();
var dependencyStores = framebufferConstructor.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Stfld &&
    instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Vulkan.VkSubpassDependency").ToArray();
foreach (var (name, original, desired) in new[]
{
    ("srcStageMask", colorStage, colorStage | depthStages),
    ("dstStageMask", colorStage, colorStage | depthStages),
    ("dstAccessMask", colorAccess, colorAccess | depthAccess)
})
{
    var store = dependencyStores.Single(instruction => ((FieldReference)instruction.Operand).Name == name);
    if (store.Previous?.OpCode != OpCodes.Ldc_I4 || store.Previous.Operand is not int value ||
        value != original && value != desired)
        throw new InvalidOperationException($"Unexpected Vulkan framebuffer dependency {name}.");
    if (value != desired) { store.Previous.Operand = desired; patched++; }
}
var destinationAccessStore = dependencyStores.Single(instruction => ((FieldReference)instruction.Operand).Name == "dstAccessMask");
var sourceAccessStore = dependencyStores.SingleOrDefault(instruction => ((FieldReference)instruction.Operand).Name == "srcAccessMask");
if (sourceAccessStore is null)
{
    if (destinationAccessStore.Previous?.Previous?.OpCode != OpCodes.Ldloca ||
        destinationAccessStore.Previous.Previous.Operand is not VariableDefinition dependency)
        throw new InvalidOperationException("Unexpected Vulkan framebuffer dependency local.");
    var destinationAccess = (FieldReference)destinationAccessStore.Operand;
    var sourceAccess = new FieldReference("srcAccessMask", destinationAccess.FieldType, destinationAccess.DeclaringType);
    var loadDependency = framebufferIl.Create(OpCodes.Ldloca, dependency);
    var loadAccess = framebufferIl.Create(OpCodes.Ldc_I4, 0x400);
    framebufferIl.InsertAfter(destinationAccessStore, loadDependency);
    framebufferIl.InsertAfter(loadDependency, loadAccess);
    framebufferIl.InsertAfter(loadAccess, framebufferIl.Create(OpCodes.Stfld, sourceAccess));
    patched++;
}
else if (sourceAccessStore.Previous?.OpCode != OpCodes.Ldc_I4 ||
         sourceAccessStore.Previous.Operand is not int access || access != 0x400)
    throw new InvalidOperationException("Unexpected Vulkan framebuffer source dependency access.");
framebufferConstructor.Body.MaxStackSize = Math.Max(framebufferConstructor.Body.MaxStackSize, 16);
framebufferConstructor.Body.OptimizeMacros();
var dependencyDevice = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkGraphicsDevice");
const string depthDependencyMarkerName = "BEngineAttachmentDepthDependency";
var depthDependencyMarker = dependencyDevice.Fields.SingleOrDefault(field => field.Name == depthDependencyMarkerName);
if (depthDependencyMarker is null)
{
    dependencyDevice.Fields.Add(new FieldDefinition(depthDependencyMarkerName,
        FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
        module.TypeSystem.Int32) { Constant = 1 });
    patched++;
}
else if (!depthDependencyMarker.IsLiteral || depthDependencyMarker.Constant is not int marker || marker != 1)
    throw new InvalidOperationException("Unexpected Vulkan attachment-depth dependency marker.");
// Veldrid 4.9.0 unconditionally chooses physicalDevices[0]. Expose a small,
// optional platform callback without making the dependency reference BEngine.
// Only output copies are changed; an unset callback retains its original policy.
var graphicsDevice = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkGraphicsDevice");
const string selectorFieldName = "BEnginePhysicalDeviceSelector";
const string selectorMethodName = "BEngineSelectPhysicalDevice";
var physicalDeviceMethod = graphicsDevice.Methods.Single(method => method.Name == "CreatePhysicalDevice");
if (!graphicsDevice.Fields.Any(field => field.Name == selectorFieldName))
{
    physicalDeviceMethod.Body.SimplifyMacros();
    var physicalArray = physicalDeviceMethod.Body.Variables.Single(variable =>
        variable.VariableType is ArrayType array && array.ElementType.FullName == "Vulkan.VkPhysicalDevice").VariableType;
    var surfaceType = module.GetTypeReferences().Single(type => type.FullName == "Vulkan.VkSurfaceKHR");
    var callback = new TypeDefinition("Veldrid", "BEngineVulkanPhysicalDeviceSelector",
        TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
        module.ImportReference(typeof(MulticastDelegate)));
    var callbackConstructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
        MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void)
    { ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed };
    callbackConstructor.Parameters.Add(new("target", ParameterAttributes.None, module.TypeSystem.Object));
    callbackConstructor.Parameters.Add(new("method", ParameterAttributes.None, module.TypeSystem.IntPtr));
    callback.Methods.Add(callbackConstructor);
    var invoke = new MethodDefinition("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig |
        MethodAttributes.NewSlot | MethodAttributes.Virtual, module.TypeSystem.Int32)
    { ImplAttributes = MethodImplAttributes.Runtime | MethodImplAttributes.Managed };
    invoke.Parameters.Add(new("devices", ParameterAttributes.None, physicalArray));
    invoke.Parameters.Add(new("surface", ParameterAttributes.None, surfaceType));
    callback.Methods.Add(invoke); module.Types.Add(callback);
    var selectorField = new FieldDefinition(selectorFieldName, FieldAttributes.Public | FieldAttributes.Static, callback);
    graphicsDevice.Fields.Add(selectorField);
    var select = new MethodDefinition(selectorMethodName, MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
        module.TypeSystem.Int32);
    select.Parameters.Add(new("devices", ParameterAttributes.None, physicalArray));
    select.Parameters.Add(new("surface", ParameterAttributes.None, surfaceType));
    select.Body.InitLocals = true;
    select.Body.Variables.Add(new(callback)); select.Body.Variables.Add(new(module.TypeSystem.Int32));
    var il = select.Body.GetILProcessor();
    var fallback = il.Create(OpCodes.Ldc_I4_0);
    il.Append(il.Create(OpCodes.Ldsfld, selectorField)); il.Append(il.Create(OpCodes.Stloc_0));
    il.Append(il.Create(OpCodes.Ldloc_0)); il.Append(il.Create(OpCodes.Brfalse, fallback));
    il.Append(il.Create(OpCodes.Ldloc_0)); il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Callvirt, invoke)); il.Append(il.Create(OpCodes.Stloc_1));
    il.Append(il.Create(OpCodes.Ldloc_1)); il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Blt, fallback));
    il.Append(il.Create(OpCodes.Ldloc_1)); il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldlen));
    il.Append(il.Create(OpCodes.Conv_I4)); il.Append(il.Create(OpCodes.Bge, fallback));
    il.Append(il.Create(OpCodes.Ldloc_1)); il.Append(il.Create(OpCodes.Ret)); il.Append(fallback); il.Append(il.Create(OpCodes.Ret));
    select.Body.MaxStackSize = 8; graphicsDevice.Methods.Add(select);

    var physicalField = graphicsDevice.Fields.Single(field => field.Name == "_physicalDevice");
    var store = physicalDeviceMethod.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Stfld &&
        instruction.Operand is FieldReference field && field.FullName == physicalField.FullName);
    var loadElement = store.Previous;
    var originalIndex = loadElement?.Previous;
    var originalArray = originalIndex?.Previous;
    if (loadElement?.OpCode != OpCodes.Ldelem_Any || originalIndex?.OpCode != OpCodes.Ldc_I4 ||
        originalIndex.Operand is not int originalDeviceIndex || originalDeviceIndex != 0 ||
        originalArray?.OpCode != OpCodes.Ldloc || originalArray.Operand is not VariableDefinition devicesVariable ||
        devicesVariable.VariableType.FullName != physicalArray.FullName || physicalDeviceMethod.Parameters.Count != 0)
        throw new InvalidOperationException($"Unexpected Vulkan physical-device selection sequence: {originalArray}; {originalIndex}; {loadElement}; parameters={physicalDeviceMethod.Parameters.Count}.");
    physicalDeviceMethod.Parameters.Add(new("presentationSurface", ParameterAttributes.None, surfaceType));
    var physicalIl = physicalDeviceMethod.Body.GetILProcessor();
    originalIndex.OpCode = OpCodes.Ldloc; originalIndex.Operand = devicesVariable;
    var loadSurface = physicalIl.Create(OpCodes.Ldarg_1);
    physicalIl.InsertAfter(originalIndex, loadSurface);
    physicalIl.InsertAfter(loadSurface, physicalIl.Create(OpCodes.Call, select));
    physicalDeviceMethod.Body.MaxStackSize = Math.Max(physicalDeviceMethod.Body.MaxStackSize, 8);
    physicalDeviceMethod.Body.OptimizeMacros();

    var callers = graphicsDevice.Methods.Where(method => method.HasBody).SelectMany(method =>
        method.Body.Instructions.Where(instruction => ReferenceEquals(instruction.Operand, physicalDeviceMethod))
            .Select(instruction => (Method: method, Call: instruction))).ToArray();
    if (callers.Length != 1 || !callers[0].Method.IsConstructor)
        throw new InvalidOperationException("Unexpected Vulkan physical-device constructor calls.");
    var caller = callers[0]; caller.Method.Body.SimplifyMacros();
    var surface = caller.Method.Body.Variables.Single(variable => variable.VariableType.FullName == surfaceType.FullName);
    caller.Method.Body.GetILProcessor().InsertBefore(caller.Call, Instruction.Create(OpCodes.Ldloc, surface));
    caller.Method.Body.MaxStackSize = Math.Max(caller.Method.Body.MaxStackSize, 8);
    caller.Method.Body.OptimizeMacros(); patched++;
}
else if (physicalDeviceMethod.Parameters.Count != 1 ||
         !physicalDeviceMethod.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.Name == selectorMethodName))
    throw new InvalidOperationException("The Vulkan device-selection patch is incomplete.");
// An unsynchronized swapchain must prefer the non-vblank Immediate mode when
// available. The upstream Mailbox-first policy can wait on a compositor-owned
// image on hybrid GPUs. Retain Mailbox/FIFO fallbacks and the synchronized path.
var swapchainType = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkSwapchain");
var createSwapchain = swapchainType.Methods.Single(method => method.Name == "CreateSwapchain");
createSwapchain.Body.SimplifyMacros();
var syncLoad = createSwapchain.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldfld &&
    instruction.Operand is FieldReference field && field.Name == "_syncToVBlank");
var unsynchronizedStart = syncLoad.Next?.Operand as Instruction;
var modeStore = createSwapchain.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Stfld &&
    instruction.Operand is FieldReference field && field.FullName == "Vulkan.VkPresentModeKHR Vulkan.VkSwapchainCreateInfoKHR::presentMode");
if (syncLoad.Next?.OpCode != OpCodes.Brfalse || unsynchronizedStart is null ||
    modeStore.Previous?.Operand is not VariableDefinition selectedMode ||
    selectedMode.VariableType.FullName != "Vulkan.VkPresentModeKHR")
    throw new InvalidOperationException("Unexpected Vulkan present-mode selection surface.");
var unsynchronizedInstructions = createSwapchain.Body.Instructions.SkipWhile(instruction => instruction != unsynchronizedStart)
    .TakeWhile(instruction => instruction != modeStore.Previous).ToArray();
var modeSelectionEnd = unsynchronizedInstructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Br)?.Operand as Instruction
    ?? throw new InvalidOperationException("Unexpected Vulkan present-mode selection join.");
var unsynchronizedConstants = unsynchronizedInstructions.TakeWhile(instruction => instruction != modeSelectionEnd)
    .Where(instruction => instruction.OpCode == OpCodes.Ldc_I4).ToArray();
const string presentModeFieldName = "BEnginePresentMode";
var presentModeField = swapchainType.Fields.SingleOrDefault(field => field.Name == presentModeFieldName);
var expectedModes = presentModeField is null ? new[] { 1, 1, 0, 0 } : new[] { 0, 0, 1, 1 };
if (unsynchronizedConstants.Length != 4 ||
    !unsynchronizedConstants.Select(instruction => (int)instruction.Operand).SequenceEqual(expectedModes))
    throw new InvalidOperationException("Unexpected Vulkan unsynchronized present-mode priority.");
if (presentModeField is null)
{
    for (var index = 0; index < unsynchronizedConstants.Length; index++)
        unsynchronizedConstants[index].Operand = index < 2 ? 0 : 1;
    presentModeField = new FieldDefinition(presentModeFieldName, FieldAttributes.Public, selectedMode.VariableType);
    swapchainType.Fields.Add(presentModeField);
    var modeIl = createSwapchain.Body.GetILProcessor();
    var loadSwapchain = modeIl.Create(OpCodes.Ldarg_0);
    var loadMode = modeIl.Create(OpCodes.Ldloc, selectedMode);
    modeIl.InsertAfter(modeStore, loadSwapchain); modeIl.InsertAfter(loadSwapchain, loadMode);
    modeIl.InsertAfter(loadMode, modeIl.Create(OpCodes.Stfld, presentModeField));
    createSwapchain.Body.MaxStackSize = Math.Max(createSwapchain.Body.MaxStackSize, 16);
    patched++;
}
else if (!createSwapchain.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Stfld &&
         instruction.Operand is FieldReference field && field.FullName == presentModeField.FullName))
    throw new InvalidOperationException("The Vulkan present-mode diagnostic patch is incomplete.");
createSwapchain.Body.OptimizeMacros();
patched += DeferredSwapchainAcquirePatch.Apply(module);
if (patched > 0)
{
    var temporary = path + ".vkchecked";
    try { assembly.Write(temporary); File.Move(temporary, path, overwrite: true); }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
}
Console.WriteLine($"VELDRID_VK_RESULTS_CHECKED|calls={checkedCalls}|patched={patched}|{path}");

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots)
    {
        yield return type;
        foreach (var child in AllTypes(type.NestedTypes)) yield return child;
    }
}

static MethodDefinition CloneClearWithoutCommit(MethodDefinition source, string name, MethodDefinition ensurePass)
{
    source.Body.SimplifyMacros();
    var copy = new MethodDefinition(name, MethodAttributes.Private | MethodAttributes.HideBySig, source.ReturnType);
    source.DeclaringType.Methods.Add(copy);
    foreach (var parameter in source.Parameters)
        copy.Parameters.Add(new(parameter.Name, parameter.Attributes, parameter.ParameterType));
    copy.Body.InitLocals = source.Body.InitLocals;
    copy.Body.MaxStackSize = source.Body.MaxStackSize;
    foreach (var variable in source.Body.Variables) copy.Body.Variables.Add(new(variable.VariableType));
    var instructions = new Dictionary<Instruction, Instruction>();
    foreach (var instruction in source.Body.Instructions)
    {
        var cloned = Instruction.Create(OpCodes.Nop);
        cloned.OpCode = instruction.OpCode; cloned.Operand = instruction.Operand;
        instructions.Add(instruction, cloned); copy.Body.Instructions.Add(cloned);
    }
    foreach (var instruction in copy.Body.Instructions)
        instruction.Operand = instruction.Operand switch
        {
            Instruction branch => instructions[branch],
            Instruction[] branches => branches.Select(branch => instructions[branch]).ToArray(),
            VariableDefinition variable => copy.Body.Variables[variable.Index],
            ParameterDefinition parameter => parameter.Index < 0 ? copy.Body.ThisParameter : copy.Parameters[parameter.Index],
            _ => instruction.Operand
        };
    foreach (var handler in source.Body.ExceptionHandlers)
        copy.Body.ExceptionHandlers.Add(new(handler.HandlerType)
        {
            TryStart = instructions[handler.TryStart], TryEnd = handler.TryEnd is null ? null : instructions[handler.TryEnd],
            HandlerStart = instructions[handler.HandlerStart], HandlerEnd = handler.HandlerEnd is null ? null : instructions[handler.HandlerEnd],
            FilterStart = handler.FilterStart is null ? null : instructions[handler.FilterStart], CatchType = handler.CatchType
        });
    // Input may already contain our immediate-clear fix from an earlier build.
    // Keep every original instruction/branch target and neutralize only its
    // injected commit pair; the new wrapper owns that commit in its finally.
    foreach (var instruction in copy.Body.Instructions)
        if (instruction.Operand is MethodReference method && method.FullName == ensurePass.FullName &&
            instruction.OpCode == OpCodes.Call && (instruction.Previous?.OpCode == OpCodes.Ldarg_0 ||
                instruction.Previous?.OpCode == OpCodes.Ldarg && instruction.Previous.Operand is ParameterDefinition { Index: < 0 }))
        {
            instruction.Previous.OpCode = OpCodes.Nop; instruction.Previous.Operand = null;
            instruction.OpCode = OpCodes.Nop; instruction.Operand = null;
        }
    copy.Body.OptimizeMacros();
    return copy;
}
