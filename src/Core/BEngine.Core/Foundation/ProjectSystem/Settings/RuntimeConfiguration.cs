namespace BEngine.ProjectSystem;

public static class RuntimeConfiguration
{
    /// <summary>
    /// Applies a host's application identity without changing playback, background
    /// execution, tags, layers, graphics or paths. Authoring hosts may apply draft
    /// values; runtime documents must still pass Validate before activation.
    /// </summary>
    public static void ApplyIdentity(string companyName, string productName)
    {
        ArgumentNullException.ThrowIfNull(companyName);
        ArgumentNullException.ThrowIfNull(productName);
        Application.companyName = companyName;
        Application.productName = productName;
    }
    public static void Validate(RuntimeProjectData document)
    {
        ArgumentNullException.ThrowIfNull(document);
        BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<IRuntimeConfigurationExtension>()?.Validate(document);
        _ = Fix64.Parse(document.FixedDeltaTime);
    }
    public static void Validate(RuntimeSettingsData document)
    {
        ArgumentNullException.ThrowIfNull(document);
        BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<IRuntimeConfigurationExtension>()?.Validate(document);
        if (string.IsNullOrWhiteSpace(document.CompanyName) || string.IsNullOrWhiteSpace(document.ProductName))
            throw new InvalidDataException("Company and product names cannot be empty.");
        if (document.Tags is null || document.Tags.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Project tags cannot contain empty values.");
        if (((((global::System.Collections.Generic.IEnumerable<string>)(document.Tags.Select(static tag => tag.Trim()))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems1024, distinctComparer1024) => distinctItems1024.ToHashSet<string>(distinctComparer1024).Count
        }) !=
            document.Tags.Count)
            throw new InvalidDataException("Project tags must be unique.");
        if (document.SortingLayers is null ||
            document.SortingLayers.Count is < SortingLayer.BuiltInLayerCount or > SortingLayer.MaximumIndex)
            throw new InvalidDataException("Project settings must define between 5 and 63 layers.");
        for (var index = 0; index < document.SortingLayers.Count; index++)
        {
            var layer = document.SortingLayers[index];
            if (layer is null) throw new InvalidDataException("Project sorting layers cannot contain null values.");
            SortingLayer.Validate(layer.Value);
            if (layer.Value != (ulong)(index + SortingLayer.MinimumIndex))
                throw new InvalidDataException("Project layer values must be contiguous one-based indices.");
        }
        var expectedBuiltIns = LayerRegistry.CreateDefaults()
            .Select(static layer => layer.BuiltInId).Order(StringComparer.Ordinal).ToArray();
        var actualBuiltIns = document.SortingLayers.Where(static layer => layer.BuiltIn)
            .Select(static layer => layer.BuiltInId).Order(StringComparer.Ordinal).ToArray();
        if (!actualBuiltIns.SequenceEqual(expectedBuiltIns, StringComparer.Ordinal) || Matches2436())
            throw new InvalidDataException("The five built-in Layer identities cannot be removed or duplicated.");
        [global::BEngine.SkipLoopGuard]
        bool Matches2436()
        {
            var matchSource2436 = document.SortingLayers;
            ArgumentNullException.ThrowIfNull(matchSource2436, "source");
            if (matchSource2436.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData>))
            {
                foreach (var layer in (global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.SortingLayerData>)matchSource2436)
                {
                    if (!layer.BuiltIn && !string.IsNullOrEmpty(layer.BuiltInId))
                        return true;
                }
        
                return false;
            }
        
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.SortingLayerData> matchValues2436 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource2436);
            foreach (var layer in matchValues2436)
            {
                if (!layer.BuiltIn && !string.IsNullOrEmpty(layer.BuiltInId))
                    return true;
            }
        
            return false;
        }
        if ((((global::System.Collections.Generic.IEnumerable<ulong>)(document.SortingLayers.Select(item => item.Value))) switch
        {
            ulong[] { Length: 0 } => 0,
            var distinctItems3724 => distinctItems3724.ToHashSet<ulong>().Count
        }) !=
            document.SortingLayers.Count)
            throw new InvalidDataException("Project layer values must be unique.");
        if (Query4437(document.SortingLayers) || ((((global::System.Collections.Generic.IEnumerable<string>)(document.SortingLayers.Select(item => item.Name.Trim()))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => 0,
            var (distinctItems4033, distinctComparer4033) => distinctItems4033.ToHashSet<string>(distinctComparer4033).Count
        }) != document.SortingLayers.Count)
            throw new InvalidDataException("Project sorting layer names must be non-empty and unique.");
        [global::BEngine.SkipLoopGuard]
        bool Query4437(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.SortingLayerData> querySource4437)
        {
            ArgumentNullException.ThrowIfNull(querySource4437, "source");
            bool Matches4437(global::BEngine.ProjectSystem.SortingLayerData item) => string.IsNullOrWhiteSpace(item.Name);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.SortingLayerData> queryValues4437;
            if (querySource4437.GetType() == typeof(global::BEngine.ProjectSystem.SortingLayerData[]))
                queryValues4437 = (global::BEngine.ProjectSystem.SortingLayerData[])querySource4437;
            else if (querySource4437.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData>))
                queryValues4437 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ProjectSystem.SortingLayerData>)querySource4437);
            else
            {
                foreach (var item in querySource4437)
                    if (Matches4437(item))
                        return true;
                return false;
            }
        
            foreach (var item in queryValues4437)
                if (Matches4437(item))
                    return true;
            return false;
        }
    }
    public static RuntimeSettingsData Apply(RuntimeWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var settings = workspace.Settings;
        Validate(settings);
        ApplyIdentity(settings.CompanyName, settings.ProductName);
        Application.runInBackground = settings.RunInBackground;
        TagManager.Configure(settings.Tags);
        LayerRegistry.Configure(settings.SortingLayers.Select(static item => item.ToDefinition()));
        return settings;
    }
}
