using System.Text;
using Silk.NET.Shaderc;

namespace BEngine.ProjectSystem.Editor;

internal static unsafe class CgShaderCompiler
{
    internal static byte[] Compile(
        string source,
        string sourceName,
        ShaderKind stage,
        string entryPoint = "main",
        bool warningsAsErrors = true,
        int optimizationLevel = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        var api = Shaderc.GetApi();
        var compiler = api.CompilerInitialize();
        if (compiler is null) throw new InvalidOperationException("Could not initialize the CG shader compiler.");

        var options = api.CompileOptionsInitialize();
        if (options is null)
        {
            api.CompilerRelease(compiler);
            throw new InvalidOperationException("Could not initialize CG shader compiler options.");
        }

        try
        {
            api.CompileOptionsSetSourceLanguage(options, SourceLanguage.Hlsl);
            api.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
            api.CompileOptionsSetOptimizationLevel(options, optimizationLevel switch
            {
                <= 0 => OptimizationLevel.Zero,
                1 => OptimizationLevel.Size,
                _ => OptimizationLevel.Performance
            });
            if (warningsAsErrors) api.CompileOptionsSetWarningsAsErrors(options);
            api.CompileOptionsSetAutoMapLocations(options, true);
            api.CompileOptionsSetHlslIoMapping(options, true);

            var byteCount = checked((nuint)Encoding.UTF8.GetByteCount(source));
            var result = api.CompileIntoSpv(compiler, source, byteCount, stage, sourceName, entryPoint, options);
            if (result is null) throw new InvalidDataException($"CG compiler returned no result for '{sourceName}'.");
            try
            {
                var status = api.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    var message = api.ResultGetErrorMessageS(result);
                    throw new InvalidDataException(string.IsNullOrWhiteSpace(message)
                        ? $"CG compilation failed with status {status}."
                        : message.Trim());
                }

                var length = checked((int)api.ResultGetLength(result));
                return new ReadOnlySpan<byte>(api.ResultGetBytes(result), length).ToArray();
            }
            finally
            {
                api.ResultRelease(result);
            }
        }
        finally
        {
            api.CompileOptionsRelease(options);
            api.CompilerRelease(compiler);
        }
    }
}
