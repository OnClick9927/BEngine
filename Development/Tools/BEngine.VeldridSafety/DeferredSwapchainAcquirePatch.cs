using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

// The acquire fence remains mandatory: Veldrid submits without an acquire
// semaphore. Only opt-in callers may overlap CPU update with that fence; every
// framebuffer use and swapchain lifecycle boundary still drains it first.
internal static class DeferredSwapchainAcquirePatch
{
    internal const string SwitchName = "BEngine.Vulkan.DeferSwapchainAcquireWait";
    private const string MarkerName = "BEngineDeferredImageAcquireVersion";
    private const string EnabledName = "_bengineDeferredImageAcquire";
    private const string PendingName = "_bengineImageAcquirePending";
    private const string CompleteName = "BEngineCompleteImageAcquire";
    private const string FinishName = "BEngineFinishImageAcquire";
    private const string ConfigureName = "BEngineConfigureImageAcquire";

    internal static int Apply(ModuleDefinition module)
    {
        var swapchain = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkSwapchain");
        var device = module.Types.Single(type => type.FullName == "Veldrid.Vk.VkGraphicsDevice");
        var swap = device.Methods.Single(method => method.Name == "SwapBuffersCore");
        var marker = swapchain.Fields.SingleOrDefault(field => field.Name == MarkerName);
        if (marker is not null)
        {
            Validate(swapchain, device, swap);
            return 0;
        }
        if (swapchain.Fields.Any(field => field.Name is EnabledName or PendingName) ||
            swapchain.Methods.Any(method => method.Name is CompleteName or FinishName or ConfigureName))
            throw new InvalidOperationException("The Vulkan deferred-acquire patch is incomplete.");

        swap.Body.SimplifyMacros();
        var acquireCall = swap.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference method && method.DeclaringType.FullName == swapchain.FullName &&
            method.Name == "AcquireNextImage");
        var skipWait = acquireCall.Next;
        if (skipWait?.OpCode != OpCodes.Brfalse || skipWait.Operand is not Instruction afterWait)
            throw new InvalidOperationException("Unexpected Vulkan acquired-image wait branch.");
        var waitBlock = swap.Body.Instructions.SkipWhile(instruction => instruction != skipWait.Next)
            .TakeWhile(instruction => instruction != afterWait).ToArray();
        var wait = NativeCall(waitBlock, "vkWaitForFences");
        var reset = NativeCall(waitBlock, "vkResetFences");
        var bool32 = waitBlock.Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Single(method => method.DeclaringType.FullName == "Vulkan.VkBool32" && method.Name == "op_Implicit");
        if (waitBlock.Length < 10 || waitBlock[^1].OpCode != OpCodes.Pop ||
            wait.Parameters.Count != 5 || reset.Parameters.Count != 3)
            throw new InvalidOperationException("Unexpected Vulkan acquire-fence wait/reset surface.");
        var nativeDevice = swapchain.Fields.Single(field => field.Name == "_gd");
        var fence = swapchain.Fields.Single(field => field.Name == "_imageAvailableFence");
        var deviceGetter = device.Methods.Single(method => method.Name == "get_Device");
        var check = module.Types.Single(type => type.FullName == "Veldrid.Vk.VulkanUtil")
            .Methods.Single(method => method.Name == "CheckResult");
        var swapLocal = swap.Body.Variables.Single(variable => variable.VariableType.FullName == swapchain.FullName);
        var enabled = new FieldDefinition(EnabledName, FieldAttributes.Private, module.TypeSystem.Boolean);
        var pending = new FieldDefinition(PendingName, FieldAttributes.Private, module.TypeSystem.Boolean);
        swapchain.Fields.Add(enabled); swapchain.Fields.Add(pending);

        var complete = new MethodDefinition(CompleteName, MethodAttributes.Assembly | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        swapchain.Methods.Add(complete);
        complete.Body.InitLocals = true;
        var availableFence = new VariableDefinition(fence.FieldType);
        var acquireLockTaken = new VariableDefinition(module.TypeSystem.Boolean);
        complete.Body.Variables.Add(availableFence); complete.Body.Variables.Add(acquireLockTaken);
        var monitorEnter = module.ImportReference(typeof(System.Threading.Monitor).GetMethod(nameof(System.Threading.Monitor.Enter),
            [typeof(object), typeof(bool).MakeByRefType()])!);
        var monitorExit = module.ImportReference(typeof(System.Threading.Monitor).GetMethod(nameof(System.Threading.Monitor.Exit),
            [typeof(object)])!);
        var il = complete.Body.GetILProcessor();
        var done = il.Create(OpCodes.Ret);
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldfld, pending));
        il.Append(il.Create(OpCodes.Brfalse, done));
        var acquireTry = il.Create(OpCodes.Ldarg_0); il.Append(acquireTry);
        il.Append(il.Create(OpCodes.Ldloca, acquireLockTaken)); il.Append(il.Create(OpCodes.Call, monitorEnter));
        var acquiredDone = il.Create(OpCodes.Leave, done);
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldfld, pending));
        il.Append(il.Create(OpCodes.Brfalse, acquiredDone));
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldfld, fence));
        il.Append(il.Create(OpCodes.Stloc, availableFence));
        LoadDevice(); il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Ldloca, availableFence));
        il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Call, bool32));
        il.Append(il.Create(OpCodes.Ldc_I4_M1)); il.Append(il.Create(OpCodes.Conv_I8));
        il.Append(il.Create(OpCodes.Call, wait)); il.Append(il.Create(OpCodes.Call, check));
        LoadDevice(); il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Ldloca, availableFence));
        il.Append(il.Create(OpCodes.Call, reset)); il.Append(il.Create(OpCodes.Call, check));
        // Retain pending on either native failure: never reuse an uncompleted or
        // unreset acquire fence after a recoverable exception/retry.
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stfld, pending)); il.Append(acquiredDone);
        var acquireFinally = il.Create(OpCodes.Ldloc, acquireLockTaken); il.Append(acquireFinally);
        var acquireEndFinally = il.Create(OpCodes.Endfinally);
        il.Append(il.Create(OpCodes.Brfalse, acquireEndFinally));
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Call, monitorExit));
        il.Append(acquireEndFinally); il.Append(done);
        complete.Body.ExceptionHandlers.Add(new(ExceptionHandlerType.Finally)
        { TryStart = acquireTry, TryEnd = acquireFinally, HandlerStart = acquireFinally, HandlerEnd = done });
        complete.Body.MaxStackSize = 8; complete.Body.OptimizeMacros();
        void LoadDevice()
        {
            il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldfld, nativeDevice));
            il.Append(il.Create(OpCodes.Callvirt, deviceGetter));
        }

        var finish = new MethodDefinition(FinishName, MethodAttributes.Assembly | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        swapchain.Methods.Add(finish);
        il = finish.Body.GetILProcessor(); done = il.Create(OpCodes.Ret);
        var eager = il.Create(OpCodes.Ldarg_0);
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldc_I4_1)); il.Append(il.Create(OpCodes.Stfld, pending));
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldfld, enabled));
        il.Append(il.Create(OpCodes.Brfalse, eager));
        // BEngine coordinates BeginFrame only for MainSwapchain. Secondary
        // windows and construction before MainSwapchain assignment stay eager.
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldfld, nativeDevice));
        il.Append(il.Create(OpCodes.Callvirt, device.Methods.Single(method => method.Name == "get_MainSwapchain")));
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Beq, done));
        il.Append(eager);
        il.Append(il.Create(OpCodes.Call, complete)); il.Append(done);
        finish.Body.MaxStackSize = 2; finish.Body.OptimizeMacros();

        var configure = new MethodDefinition(ConfigureName, MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        swapchain.Methods.Add(configure); configure.Body.InitLocals = true;
        var switchValue = new VariableDefinition(module.TypeSystem.Boolean); configure.Body.Variables.Add(switchValue);
        var tryGetSwitch = module.ImportReference(typeof(AppContext).GetMethod(nameof(AppContext.TryGetSwitch),
            [typeof(string), typeof(bool).MakeByRefType()])!);
        il = configure.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldstr, SwitchName));
        il.Append(il.Create(OpCodes.Ldloca, switchValue)); il.Append(il.Create(OpCodes.Call, tryGetSwitch));
        il.Append(il.Create(OpCodes.Ldloc, switchValue)); il.Append(il.Create(OpCodes.And));
        il.Append(il.Create(OpCodes.Stfld, enabled)); il.Append(il.Create(OpCodes.Ret));
        configure.Body.MaxStackSize = 3; configure.Body.OptimizeMacros();

        foreach (var constructor in swapchain.Methods.Where(method => method.IsConstructor && !method.IsStatic))
        {
            constructor.Body.SimplifyMacros(); var cil = constructor.Body.GetILProcessor();
            foreach (var ret in constructor.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray())
            {
                // Mutating the existing ret preserves branches targeting it.
                ret.OpCode = OpCodes.Ldarg_0;
                var call = cil.Create(OpCodes.Call, configure); cil.InsertAfter(ret, call);
                cil.InsertAfter(call, cil.Create(OpCodes.Ret));
            }
            constructor.Body.MaxStackSize = Math.Max(constructor.Body.MaxStackSize, 8);
            constructor.Body.OptimizeMacros();
        }

        // Keep all existing branch targets/exception regions. Only replace the
        // successful acquire's old immediate wait block with the stateful helper.
        foreach (var instruction in waitBlock) { instruction.OpCode = OpCodes.Nop; instruction.Operand = null; }
        waitBlock[0].OpCode = OpCodes.Ldloc; waitBlock[0].Operand = swapLocal;
        swap.Body.GetILProcessor().InsertAfter(waitBlock[0], Instruction.Create(OpCodes.Call, finish));
        var swapStore = swap.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Stloc &&
            ReferenceEquals(instruction.Operand, swapLocal));
        var sil = swap.Body.GetILProcessor();
        var loadSwap = sil.Create(OpCodes.Ldloc, swapLocal); sil.InsertAfter(swapStore, loadSwap);
        sil.InsertAfter(loadSwap, sil.Create(OpCodes.Call, complete));
        swap.Body.MaxStackSize = Math.Max(swap.Body.MaxStackSize, 8); swap.Body.OptimizeMacros();

        // A caller may cache a framebuffer, skip a draw, resize, toggle VSync,
        // or exit immediately after Present. Each boundary owns a mandatory drain.
        foreach (var name in new[] { "get_Framebuffer", "AcquireNextImage", "CreateSwapchain", "RecreateAndReacquire", "DisposeCore" })
            PrependDrain(swapchain.Methods.Single(method => method.Name == name), complete);
        // Also protect clients which cache MainSwapchain.Framebuffer across
        // frames. Check inside the existing queue lock, not before it: another
        // thread may publish a pending acquire while this submit waits for it.
        var completeMain = new MethodDefinition("BEngineCompleteMainImageAcquire", MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        device.Methods.Add(completeMain); completeMain.Body.InitLocals = true;
        var mainSwapchain = new VariableDefinition(swapchain); completeMain.Body.Variables.Add(mainSwapchain);
        il = completeMain.Body.GetILProcessor(); done = il.Create(OpCodes.Ret);
        il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Callvirt,
            device.Methods.Single(method => method.Name == "get_MainSwapchain")));
        il.Append(il.Create(OpCodes.Isinst, swapchain)); il.Append(il.Create(OpCodes.Stloc, mainSwapchain));
        il.Append(il.Create(OpCodes.Ldloc, mainSwapchain)); il.Append(il.Create(OpCodes.Brfalse, done));
        il.Append(il.Create(OpCodes.Ldloc, mainSwapchain)); il.Append(il.Create(OpCodes.Call, complete)); il.Append(done);
        completeMain.Body.MaxStackSize = 2; completeMain.Body.OptimizeMacros();
        var submit = device.Methods.Single(method => method.Name == "SubmitCommandBuffer");
        submit.Body.SimplifyMacros();
        var firstSubmit = submit.Body.Instructions.First(instruction => instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == "Vulkan.VulkanNative" && method.Name == "vkQueueSubmit");
        var submitEnter = submit.Body.Instructions.TakeWhile(instruction => instruction != firstSubmit).Last(instruction =>
            instruction.Operand is MethodReference method && method.DeclaringType.FullName == "System.Threading.Monitor" && method.Name == "Enter");
        var submitIl = submit.Body.GetILProcessor();
        var loadDevice = submitIl.Create(OpCodes.Ldarg_0); submitIl.InsertAfter(submitEnter, loadDevice);
        submitIl.InsertAfter(loadDevice, submitIl.Create(OpCodes.Call, completeMain));
        submit.Body.MaxStackSize = Math.Max(submit.Body.MaxStackSize, 8); submit.Body.OptimizeMacros();
        // A separate present queue uses the swapchain's own existing lock.
        // Retain that native queue lock and add the graphics queue as the outer
        // owner, so publishing pending is atomic with every guarded submit.
        WrapQueueLock(swap, device.Fields.Single(field => field.Name == "_graphicsQueueLock"), monitorEnter, monitorExit);
        swapchain.Fields.Add(new FieldDefinition(MarkerName, FieldAttributes.Public | FieldAttributes.Static |
            FieldAttributes.Literal | FieldAttributes.HasDefault, module.TypeSystem.Int32) { Constant = 1 });
        Validate(swapchain, device, swap);
        return 1;
    }

    private static MethodReference NativeCall(IEnumerable<Instruction> instructions, string name) =>
        instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().Single(method =>
            method.DeclaringType.FullName == "Vulkan.VulkanNative" && method.Name == name);

    private static void PrependDrain(MethodDefinition method, MethodDefinition complete)
    {
        method.Body.SimplifyMacros(); var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
        // Existing internal branches keep their original targets. Entry is the
        // only added edge; constructor-time drains return before accessing _gd.
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(first, il.Create(OpCodes.Call, complete));
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8); method.Body.OptimizeMacros();
    }

    private static void WrapQueueLock(MethodDefinition method, FieldDefinition queueLock, MethodReference enter, MethodReference exit)
    {
        method.Body.SimplifyMacros(); method.Body.InitLocals = true;
        var gate = new VariableDefinition(method.Module.TypeSystem.Object);
        var taken = new VariableDefinition(method.Module.TypeSystem.Boolean);
        method.Body.Variables.Add(gate); method.Body.Variables.Add(taken);
        var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
        var done = il.Create(OpCodes.Ret);
        // Older EH regions ending at the old body end must not capture the new
        // outer finally. Veldrid's reviewed method has explicit handler ends.
        if (method.Body.ExceptionHandlers.Any(handler => handler.TryEnd is null || handler.HandlerEnd is null))
            throw new InvalidOperationException("Unexpected open Vulkan present exception region.");
        foreach (var ret in method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray())
        { ret.OpCode = OpCodes.Leave; ret.Operand = done; }
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0)); il.InsertBefore(first, il.Create(OpCodes.Ldfld, queueLock));
        il.InsertBefore(first, il.Create(OpCodes.Stloc, gate));
        var start = il.Create(OpCodes.Ldloc, gate); il.InsertBefore(first, start);
        il.InsertBefore(first, il.Create(OpCodes.Ldloca, taken)); il.InsertBefore(first, il.Create(OpCodes.Call, enter));
        var finallyStart = il.Create(OpCodes.Ldloc, taken); var endFinally = il.Create(OpCodes.Endfinally);
        il.Append(finallyStart); il.Append(il.Create(OpCodes.Brfalse, endFinally));
        il.Append(il.Create(OpCodes.Ldloc, gate)); il.Append(il.Create(OpCodes.Call, exit));
        il.Append(endFinally); il.Append(done);
        method.Body.ExceptionHandlers.Add(new(ExceptionHandlerType.Finally)
        { TryStart = start, TryEnd = finallyStart, HandlerStart = finallyStart, HandlerEnd = done });
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8); method.Body.OptimizeMacros();
    }

    private static void Validate(TypeDefinition swapchain, TypeDefinition device, MethodDefinition swap)
    {
        if (swapchain.Fields.SingleOrDefault(field => field.Name == MarkerName) is not { IsLiteral: true, Constant: int version } || version != 1)
            throw new InvalidOperationException("Unexpected Vulkan deferred-acquire patch version.");
        var complete = swapchain.Methods.Single(method => method.Name == CompleteName);
        var finish = swapchain.Methods.Single(method => method.Name == FinishName);
        var configure = swapchain.Methods.Single(method => method.Name == ConfigureName);
        foreach (var name in new[] { "get_Framebuffer", "AcquireNextImage", "CreateSwapchain", "RecreateAndReacquire", "DisposeCore" })
        {
            var drainInstructions = swapchain.Methods.Single(method => method.Name == name).Body.Instructions;
            if (drainInstructions[0].OpCode != OpCodes.Ldarg_0 || drainInstructions[1].Operand is not MethodReference call || call.FullName != complete.FullName)
                throw new InvalidOperationException("Missing Vulkan acquire drain: " + name);
        }
        int Calls(MethodDefinition method, string name) => method.Body.Instructions.Count(instruction =>
            instruction.Operand is MethodReference call && call.Name == name);
        if (Calls(complete, "vkWaitForFences") != 1 || Calls(complete, "vkResetFences") != 1 || Calls(complete, "CheckResult") != 2 ||
            Calls(complete, "Enter") != 1 || Calls(complete, "Exit") != 1 || complete.Body.ExceptionHandlers.Count != 1 ||
            Calls(finish, CompleteName) != 1 || Calls(finish, "get_MainSwapchain") != 1 ||
            Calls(swap, FinishName) != 1 || Calls(swap, CompleteName) != 1 ||
            Calls(swap, "vkWaitForFences") != 0 || Calls(swap, "vkResetFences") != 0 ||
            Calls(configure, nameof(AppContext.TryGetSwitch)) != 1 ||
            swapchain.Methods.Where(method => method.IsConstructor && !method.IsStatic).Any(method => Calls(method, ConfigureName) == 0))
            throw new InvalidOperationException("The Vulkan deferred-acquire synchronization surface is incomplete.");
        // Reinitialization must retain its original synchronous acquire wait.
        var recreate = swapchain.Methods.Single(method => method.Name == "RecreateAndReacquire");
        if (Calls(recreate, "vkWaitForFences") != 1 || Calls(recreate, "vkResetFences") != 1)
            throw new InvalidOperationException("Vulkan swapchain recreation lost its synchronous acquire fence.");
        var submit = device.Methods.Single(method => method.Name == "SubmitCommandBuffer");
        var instructions = submit.Body.Instructions;
        var guard = instructions.Single(instruction => instruction.Operand is MethodReference call && call.Name == "BEngineCompleteMainImageAcquire");
        var previousEnter = instructions.TakeWhile(instruction => instruction != guard).LastOrDefault(instruction =>
            instruction.Operand is MethodReference call && call.DeclaringType.FullName == "System.Threading.Monitor" && call.Name == "Enter");
        var submitCall = instructions.First(instruction => instruction.Operand is MethodReference call && call.Name == "vkQueueSubmit");
        if (previousEnter?.Next?.OpCode != OpCodes.Ldarg_0 || previousEnter.Next.Next != guard ||
            instructions.IndexOf(guard) >= instructions.IndexOf(submitCall) ||
            !submit.Body.ExceptionHandlers.Any(handler => handler.HandlerType == ExceptionHandlerType.Finally &&
                instructions.IndexOf(handler.TryStart) <= instructions.IndexOf(previousEnter) &&
                instructions.IndexOf(handler.TryEnd) > instructions.IndexOf(guard)) ||
            Calls(swap, "Enter") != 2 || Calls(swap, "Exit") != 2 || swap.Body.ExceptionHandlers.Count != 2)
            throw new InvalidOperationException("Vulkan submit/present lost its shared queue-lock acquire guard.");
    }
}
