using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using BEngine.Build;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Serialization;

namespace BEngine.Editor.Build;

internal sealed class PlayerPlatformBuildHooks(PlayerBuildContext context, FileUtility fileSystem, PathUtility pathService)
{
    internal PlatformBuildHooks Create(
        Func<string, IReadOnlyCollection<string>, IReadOnlyDictionary<string, PlatformPackagedAssemblySource>>?
            packagedAssemblySources = null) => new()
    {
        ConfigurePublish = ConfigurePublish,
        Published = Published,
        PrecompiledRegistrationStatement = image =>
            "BEngine.Player.HotUpdate.ManagedCodePrecompiledAssemblies." + (image.Shared ? "RegisterShared(" : "Register(") +
            JsonSerializer.Serialize(image.Name) + ", " + JsonSerializer.Serialize(image.Sha256) + ", " +
            JsonSerializer.Serialize(image.Mvid) + ");",
        PackagedAssemblySources = packagedAssemblySources
    };

    private void ConfigurePublish(PlatformBuildContext platformContext, string buildArtifactsDirectory, ProcessRunRequest process)
    {
        var fullTrimming = context.Request.ManagedStripping == PlayerManagedStrippingLevel.Full;
        var trimManagedCode = context.Request.SelfContained && context.Request.ManagedStripping != PlayerManagedStrippingLevel.Disabled;
        var linkDescriptors = PlayerStrippingSettings.GetLinkDescriptors(
            BEngine.ProjectSystem.ProjectWorkspace.Open(context.Request.ProjectPath));
        var includeTextBindings = !fullTrimming || PlayerHostContractRoots.RequiresTextBindings(
            context.RuntimeManagedCodeAssemblyPaths,
            new[] { context.ReflectionRootDescriptorPath, context.HostAssetRootDescriptorPath }.Concat(linkDescriptors));
        process.Arguments.Add($"-p:BEngineIncludeTextBindings={includeTextBindings.ToString().ToLowerInvariant()}");
        context.IncludeTextBindings = includeTextBindings;
        if (context.StrippingReport is { } featureReport) featureReport.TextBindingsIncluded = includeTextBindings;
        var hostBootstrap = PlayerHostAssemblyBootstrap.Write(
            context.RuntimeManagedCodeAssemblyPaths.Concat(PlayerSerializationRoots.HostAssemblyPaths),
            pathService.Combine(buildArtifactsDirectory, "BEngineStaticHostBootstrap.cs"), includeTextBindings,
            textPolicy: PlatformTextBuildPolicy.For(context.Target.Platform, context.Request.WebFontBackend),
            startupLogoPng: context.StartupLogoPng.ToArray(), splashBackground: context.Request.SplashBackgroundColor);
        process.Arguments.Add($"-p:BEngineHostAssemblyBootstrap={hostBootstrap}");
        process.Arguments.Add($"-p:PublishTrimmed={trimManagedCode.ToString().ToLowerInvariant()}");
        if (trimManagedCode)
        {
            process.Arguments.Add(fullTrimming ? "-p:TrimMode=full" : "-p:TrimMode=partial");
            process.Arguments.Add(fullTrimming ? "-p:SuppressTrimAnalysisWarnings=false" : "-p:SuppressTrimAnalysisWarnings=true");
            if (fullTrimming)
            {
                process.Arguments.Add("-p:TrimmerSingleWarn=false");
                process.Arguments.Add("-p:ILLinkTreatWarningsAsErrors=false");
                // Keep analyzer/linker diagnostics in the publish log; the engine's
                // reflection and native interop contracts are validated by the player smoke tests.
                process.Arguments.Add("-p:TreatWarningsAsErrors=false");
                // Core prefab graphs and existing reloadable game serializers use
                // reflection; their DTO members are described by PlayerSerializationRoots.
                process.Arguments.Add("-p:JsonSerializerIsReflectionEnabledByDefault=true");
            }
        }
        // WebAssembly and mobile targets may require trimming/AOT even when the project's
        // stripping option is disabled. Always describe the dynamic release boundary so a
        // platform-specific PublishTrimmed=true cannot remove framework contracts used only
        // by interpreted package and game assemblies.
        var preciseHostRootsReady = false;
        if (context.Request.SelfContained && context.RuntimeManagedCodeAssemblyPaths.Count > 0)
        {
            var descriptorPath = DynamicManagedCodeTrimmerDescriptor.Write(
                EditorCapabilities.GetRequiredService<PathUtility>().Combine(buildArtifactsDirectory, "bengine-dynamic-dependencies.xml"),
                context.RuntimeManagedCodeAssemblyPaths, fullTrimming);
            var descriptor = XDocument.Load(descriptorPath);
            PlayerLinkXml.MergeInto(descriptor.Root!, linkDescriptors,
                context.RuntimeManagedCodeAssemblyPaths.Concat(PlayerSerializationRoots.HostAssemblyPaths)
                    .Concat((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
                        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)));
            if (fullTrimming)
            {
                PlayerSerializationRoots.Add(descriptor.Root!, PlayerSerializationRoots.HostAssemblyPaths
                    .Concat(context.RuntimeManagedCodeAssemblyPaths));
                var dynamicNames = context.RuntimeManagedCodeAssemblyPaths.Select(Path.GetFileNameWithoutExtension)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var assembly in descriptor.Root!.Elements("assembly").ToArray())
                {
                    var name = ((string)assembly.Attribute("fullname")!).AsSpan().Before(',').ToString();
                    if (dynamicNames.Contains(name) && !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name)) assembly.Remove();
                }
                foreach (var additional in new[] { context.ReflectionRootDescriptorPath, context.HostAssetRootDescriptorPath })
                {
                    if (additional is null) continue;
                    foreach (var assembly in XDocument.Load(additional).Root!.Elements("assembly"))
                    {
                        var copy = new XElement(assembly);
                        var name = ((string)copy.Attribute("fullname")!).AsSpan().Before(',').ToString();
                        if (dynamicNames.Contains(name) && !DynamicManagedCodeTrimmerDescriptor.IsHostAssembly(name)) continue;
                        bool __queryResult6526;
                        {
                            var __querySource6526 = descriptor.Root!.Elements("assembly");
                            ArgumentNullException.ThrowIfNull(__querySource6526, "source");
                            __queryResult6526 = false;
                            var __querySource6526Type = __querySource6526.GetType();
                            if (__querySource6526Type == typeof(System.Xml.Linq.XElement[]) || __querySource6526Type == typeof(System.Collections.Generic.List<System.Xml.Linq.XElement>))
                            {
                                System.ReadOnlySpan<System.Xml.Linq.XElement> __querySource6526Span = __querySource6526Type == typeof(System.Xml.Linq.XElement[]) ? (System.Xml.Linq.XElement[])(object)__querySource6526 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<System.Xml.Linq.XElement>)(object)__querySource6526);
                                foreach (var __queryItem6526 in __querySource6526Span)
                                {
                                    if ((XNode.DeepEquals(__queryItem6526, copy)))
                                    {
                                        __queryResult6526 = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                foreach (var __queryItem6526 in __querySource6526)
                                {
                                    if ((XNode.DeepEquals(__queryItem6526, copy)))
                                    {
                                        __queryResult6526 = true;
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (!__queryResult6526)
                            descriptor.Root!.Add(copy);
                    }
                }
                descriptor.Save(descriptorPath);
                preciseHostRootsReady = true;
                if (context.StrippingReportPath is { } rootReportPath)
                    fileSystem.WriteAllText(Path.ChangeExtension(rootReportPath, ".host-roots.xml"),
                        fileSystem.ReadAllText(descriptorPath));
            }
            else
            {
                if (context.HostAssetRootDescriptorPath is { } hostAssetRoots)
                    foreach (var assembly in XDocument.Load(hostAssetRoots).Root!.Elements("assembly"))
                        descriptor.Root!.Add(new XElement(assembly));
                descriptor.Save(descriptorPath);
            }
            process.Arguments.Add($"-p:BEngineTrimmerRootDescriptor={descriptorPath}");
        }
        platformContext.IncludeTextBindings = context.IncludeTextBindings;
        platformContext.AdditionalSdkLinkerArguments = WebStaticHostContractFeature.SdkLinkerArguments(
            WebStaticHostContractFeature.IsHostPassEnabled(context, preciseHostRootsReady));
    }

    private void Published(PlatformBuildContext platformContext, string buildArtifactsDirectory, ProcessRunResult result)
    {
            if (context.StrippingReport is { } report && context.StrippingReportPath is { } reportPath)
            {
                report.HostPublishLogPath = Path.ChangeExtension(reportPath, ".publish.log");
                fileSystem.WriteAllText(report.HostPublishLogPath, result.StandardOutput + result.StandardError);
                var originalDirectory = pathService.Combine(buildArtifactsDirectory, "bin");
                var original = (Directory.Exists(originalDirectory)
                        ? Directory.EnumerateFiles(originalDirectory, "*.dll", SearchOption.AllDirectories) : [])
                    .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static group => group.Key!, static group => group.Max(path => new FileInfo(path).Length), StringComparer.OrdinalIgnoreCase);
                var linked = Directory.EnumerateFiles(buildArtifactsDirectory, "*.dll", SearchOption.AllDirectories)
                    .Where(static path => Path.GetDirectoryName(path)!.EndsWith("linked", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static group => group.Key!, static group => group.Max(path => new FileInfo(path).Length), StringComparer.OrdinalIgnoreCase);
                foreach (var entry in original.OrderBy(static p => p.Key, StringComparer.Ordinal))
                {
                    var retained = linked.TryGetValue(entry.Key, out var size);
                    report.HostAssemblies.Add(new PlayerStrippingReportEntry { Name = entry.Key,
                        OriginalBytes = entry.Value, PublishedBytes = size, Included = retained,
                        Reason = retained ? "Host call graph or dynamic-code contract" : "No reachable Host dependency" });
                }
                fileSystem.WriteAllText(reportPath, YamlUtility.Serialize(report));
            }
            if (result.Succeeded)
            {
                var contractAssemblies = Directory.EnumerateFiles(buildArtifactsDirectory, "*.dll", SearchOption.AllDirectories)
                    .Where(static path => Path.GetDirectoryName(path)!.EndsWith("linked", StringComparison.OrdinalIgnoreCase))
                    .Where(IsManagedAssembly).ToArray();
                if (contractAssemblies.Length == 0)
                    contractAssemblies = Directory.EnumerateFiles(Path.Combine(buildArtifactsDirectory, "bin"), "*.dll", SearchOption.AllDirectories)
                        .Where(IsManagedAssembly)
                        .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(static g => g.First()).ToArray();
                if (contractAssemblies.Length > 0)
                {
                    context.HostContractPath = PlayerHostContract.Write(context, contractAssemblies);
                    if (context.StrippingReport is { } contractReport && context.StrippingReportPath is { } contractReportPath)
                    {
                        contractReport.HostContractPath = context.HostContractPath;
                        fileSystem.WriteAllText(contractReportPath, YamlUtility.Serialize(contractReport));
                    }
                }
            }
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (IOException) { return false; }
        catch (BadImageFormatException) { return false; }
    }

}
