using System.Text;
using Vulkan;
using static Vulkan.VulkanNative;

namespace BEngine.Rendering.Rhi;

/// <summary>Startup-only Vulkan adapter policy. No operating-system GPU preferences are changed.</summary>
internal static unsafe class VulkanPhysicalDeviceSelection
{
    internal readonly record struct Candidate(int Index, string Name, VkPhysicalDeviceType Type,
        ulong LocalMemoryBytes, string? Rejection)
    {
        internal bool Compatible => Rejection is null;
    }

    internal static int Select(VkPhysicalDevice[] devices, VkSurfaceKHR surface, bool requireDepth, string? preference)
    {
        var candidates = new Candidate[devices.Length];
        for (var index = 0; index < devices.Length; index++)
        {
            var device = devices[index];
            vkGetPhysicalDeviceProperties(device, out var properties);
            vkGetPhysicalDeviceMemoryProperties(device, out var memory);
            var heaps = &memory.memoryHeaps_0;
            ulong localBytes = 0;
            for (var heap = 0; heap < Math.Min(memory.memoryHeapCount, 16u); heap++)
                if ((heaps[heap].flags & VkMemoryHeapFlags.DeviceLocal) != 0)
                    localBytes += heaps[heap].size;
            candidates[index] = new(index, ReadUtf8(properties.deviceName, 256), properties.deviceType,
                localBytes, CheckCompatibility(device, surface, requireDepth));
        }
        var selected = SelectCandidate(candidates, preference);
        if (selected < 0)
            throw new NotSupportedException("No Vulkan physical device can render and present to this window: " +
                string.Join("; ", candidates.Select(candidate => candidate.Name + ": " + candidate.Rejection)));
        var chosen = candidates[selected];
        if (!string.IsNullOrWhiteSpace(preference) && !IsAutomatic(preference) &&
            !preference.Trim().Equals("first", StringComparison.OrdinalIgnoreCase) &&
            !chosen.Name.Contains(preference.Trim(), StringComparison.OrdinalIgnoreCase))
            Debug.LogWarning($"Vulkan device override '{preference}' was not found or is incompatible; falling back to {chosen.Name}.");
        Debug.Log($"Vulkan physical-device selection: {chosen.Name} ({chosen.Type}); policy={preference ?? "high-performance"}; compatible={candidates.Count(candidate => candidate.Compatible)}/{candidates.Length}.");
        return selected;
    }

    /// <summary>Returns the candidate's enumeration index, or -1 when no compatible device exists.</summary>
    internal static int SelectCandidate(IReadOnlyList<Candidate> candidates, string? preference)
    {
        var requested = preference?.Trim();
        var useFirst = string.Equals(requested, "first", StringComparison.OrdinalIgnoreCase);
        var matchName = !string.IsNullOrEmpty(requested) && !IsAutomatic(requested) && !useFirst;
        var best = -1; var matched = -1;
        Candidate bestCandidate = default, matchedCandidate = default;
        foreach (var candidate in candidates)
        {
            if (!candidate.Compatible) continue;
            if (useFirst) return candidate.Index;
            if (best < 0 || IsFaster(candidate, bestCandidate)) { best = candidate.Index; bestCandidate = candidate; }
            if (matchName && candidate.Name.Contains(requested!, StringComparison.OrdinalIgnoreCase) &&
                (matched < 0 || IsFaster(candidate, matchedCandidate)))
            { matched = candidate.Index; matchedCandidate = candidate; }
        }
        return matched >= 0 ? matched : best;
    }

    private static bool IsAutomatic(string preference) =>
        preference.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) ||
        preference.Trim().Equals("high-performance", StringComparison.OrdinalIgnoreCase);

    private static bool IsFaster(Candidate left, Candidate right)
    {
        static int Rank(VkPhysicalDeviceType type) => type switch
        {
            VkPhysicalDeviceType.DiscreteGpu => 4,
            VkPhysicalDeviceType.IntegratedGpu => 3,
            VkPhysicalDeviceType.VirtualGpu => 2,
            VkPhysicalDeviceType.Other => 1,
            _ => 0
        };
        var rank = Rank(left.Type).CompareTo(Rank(right.Type));
        return rank > 0 || rank == 0 && left.LocalMemoryBytes > right.LocalMemoryBytes;
    }

    private static string? CheckCompatibility(VkPhysicalDevice device, VkSurfaceKHR surface, bool requireDepth)
    {
        uint count = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(device, ref count, (VkQueueFamilyProperties*)null);
        if (count is 0 or > 256) return "no usable queue families";
        var families = new VkQueueFamilyProperties[count];
        var graphics = false; var present = surface.Handle == 0;
        fixed (VkQueueFamilyProperties* data = families)
        {
            vkGetPhysicalDeviceQueueFamilyProperties(device, ref count, data);
            for (uint index = 0; index < count; index++)
            {
                if (data[index].queueCount == 0) continue;
                graphics |= (data[index].queueFlags & VkQueueFlags.Graphics) != 0;
                if (!present && vkGetPhysicalDeviceSurfaceSupportKHR(device, index, surface, out var supported) == VkResult.Success)
                    present = supported;
            }
        }
        if (!graphics || !present) return "no graphics/presentation queue";
        if (requireDepth)
        {
            vkGetPhysicalDeviceFormatProperties(device, VkFormat.D24UnormS8Uint, out var format);
            if ((format.optimalTilingFeatures & VkFormatFeatureFlags.DepthStencilAttachment) == 0)
                return "D24S8 depth/stencil is unsupported";
        }
        if (surface.Handle == 0) return null;
        count = 0;
        if (vkEnumerateDeviceExtensionProperties(device, (byte*)null, ref count, (VkExtensionProperties*)null) != VkResult.Success ||
            count is 0 or > 4096) return "device extensions unavailable";
        var extensions = new VkExtensionProperties[count];
        var swapchain = false;
        fixed (VkExtensionProperties* data = extensions)
        {
            if (vkEnumerateDeviceExtensionProperties(device, (byte*)null, ref count, data) != VkResult.Success)
                return "device extensions unavailable";
            for (var index = 0; index < count; index++)
                if (ReadUtf8(data[index].extensionName, 256) == "VK_KHR_swapchain") { swapchain = true; break; }
        }
        if (!swapchain) return "VK_KHR_swapchain is unsupported";
        count = 0;
        if (vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface, ref count, (VkSurfaceFormatKHR*)null) != VkResult.Success || count == 0)
            return "no compatible presentation formats";
        count = 0;
        if (vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface, ref count, (VkPresentModeKHR*)null) != VkResult.Success || count == 0)
            return "no compatible presentation modes";
        return null;
    }

    private static string ReadUtf8(byte* value, int maximum)
    {
        var bytes = new ReadOnlySpan<byte>(value, maximum);
        var length = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(length >= 0 ? bytes[..length] : bytes);
    }
}
