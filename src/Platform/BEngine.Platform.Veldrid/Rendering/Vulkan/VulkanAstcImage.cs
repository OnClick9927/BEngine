using System.Reflection;
using Veldrid;
using Vulkan;
using static Vulkan.VulkanNative;

namespace BEngine.Rendering.Rhi;

/// <summary>ASTC images use native Vulkan descriptors because Veldrid 4.9 has no ASTC PixelFormat.</summary>
internal sealed unsafe class VulkanAstcImage : IDisposable
{
    private readonly VkDevice _device;
    private VkImage _image;
    private VkDeviceMemory _memory;
    private VkImageView _view;
    private BEngine.Diagnostics.NativeMemoryTracker.Allocation? _allocation;
    internal static bool Supports(Veldrid.GraphicsDevice device)
    {
        if (device.BackendType != Veldrid.GraphicsBackend.Vulkan) return false;
        var info = device.GetVulkanInfo(); var physical = new VkPhysicalDevice(info.PhysicalDevice);
        vkGetPhysicalDeviceFeatures(physical, out var features);
        vkGetPhysicalDeviceFormatProperties(physical, VkFormat.Astc4x4UnormBlock, out var properties);
        return features.textureCompressionASTC_LDR && (properties.optimalTilingFeatures & VkFormatFeatureFlags.SampledImage) != 0;
    }
    internal VulkanAstcImage(VulkanGraphicsDevice owner, GraphicsTextureDescription description, ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty && bytes.Length != description.DataByteSize)
            throw new ArgumentException("Texture pixel data is incomplete.", nameof(bytes));
        var format = description.Format switch
        {
            GraphicsTextureFormat.Astc4x4Unorm => VkFormat.Astc4x4UnormBlock,
            GraphicsTextureFormat.Rgba8Unorm => VkFormat.R8g8b8a8Unorm,
            _ => throw new NotSupportedException("Unsupported native Vulkan image format.")
        };
        var info = owner.NativeDevice.GetVulkanInfo(); _device = new(info.Device);
        var physical = new VkPhysicalDevice(info.PhysicalDevice);
        VkBuffer staging = default; VkDeviceMemory stagingMemory = default; VkCommandPool pool = default; VkFence fence = default;
        try
        {
            var image = new VkImageCreateInfo { sType = VkStructureType.ImageCreateInfo, imageType = VkImageType.Image2D,
                format = format, extent = new() { width = (uint)description.Width, height = (uint)description.Height, depth = 1 },
                mipLevels = (uint)description.MipLevels, arrayLayers = 1, samples = VkSampleCountFlags.Count1,
                tiling = VkImageTiling.Optimal, usage = VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst,
                sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined };
            Check(vkCreateImage(_device, ref image, null, out _image)); vkGetImageMemoryRequirements(_device, _image, out var requirements);
            _memory = Allocate(physical, requirements, VkMemoryPropertyFlags.DeviceLocal); Check(vkBindImageMemory(_device, _image, _memory, 0));
            _allocation = BEngine.Diagnostics.NativeMemoryTracker.Register("GPU.Texture.Vulkan.ASTC", "ASTC 4x4", checked((long)requirements.size));
            var view = new VkImageViewCreateInfo { sType = VkStructureType.ImageViewCreateInfo, image = _image, format = format,
                viewType = VkImageViewType.Image2D, subresourceRange = new(VkImageAspectFlags.Color, 0, (uint)description.MipLevels, 0, 1) };
            Check(vkCreateImageView(_device, ref view, null, out _view));
            var buffer = new VkBufferCreateInfo { sType = VkStructureType.BufferCreateInfo, size = (ulong)description.DataByteSize, usage = VkBufferUsageFlags.TransferSrc, sharingMode = VkSharingMode.Exclusive };
            Check(vkCreateBuffer(_device, ref buffer, null, out staging)); vkGetBufferMemoryRequirements(_device, staging, out requirements);
            stagingMemory = Allocate(physical, requirements, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
            Check(vkBindBufferMemory(_device, staging, stagingMemory, 0)); void* mapped;
            Check(vkMapMemory(_device, stagingMemory, 0, (ulong)description.DataByteSize, 0, &mapped));
            try { if (bytes.IsEmpty) new Span<byte>(mapped, description.DataByteSize).Clear(); else bytes.CopyTo(new Span<byte>(mapped, description.DataByteSize)); }
            finally { vkUnmapMemory(_device, stagingMemory); }
            var poolInfo = new VkCommandPoolCreateInfo { sType = VkStructureType.CommandPoolCreateInfo, queueFamilyIndex = info.GraphicsQueueFamilyIndex };
            Check(vkCreateCommandPool(_device, ref poolInfo, null, out pool));
            var allocate = new VkCommandBufferAllocateInfo { sType = VkStructureType.CommandBufferAllocateInfo, commandPool = pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
            VkCommandBuffer commands; Check(vkAllocateCommandBuffers(_device, ref allocate, &commands));
            var begin = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo, flags = VkCommandBufferUsageFlags.OneTimeSubmit }; Check(vkBeginCommandBuffer(commands, ref begin));
            var barrier = new VkImageMemoryBarrier { sType = VkStructureType.ImageMemoryBarrier, image = _image,
                oldLayout = VkImageLayout.Undefined, newLayout = VkImageLayout.TransferDstOptimal, dstAccessMask = VkAccessFlags.TransferWrite,
                srcQueueFamilyIndex = uint.MaxValue, dstQueueFamilyIndex = uint.MaxValue, subresourceRange = view.subresourceRange };
            vkCmdPipelineBarrier(commands, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.Transfer, 0, 0, null, 0, null, 1, &barrier);
            var regions = new VkBufferImageCopy[description.MipLevels]; ulong offset = 0;
            for (var level = 0; level < regions.Length; level++)
            {
                regions[level] = new VkBufferImageCopy { bufferOffset = offset,
                    imageSubresource = new() { aspectMask = VkImageAspectFlags.Color, mipLevel = (uint)level, baseArrayLayer = 0, layerCount = 1 },
                    imageExtent = new() { width = (uint)Math.Max(1, description.Width >> level), height = (uint)Math.Max(1, description.Height >> level), depth = 1 } };
                offset += (ulong)description.LevelByteSize(level);
            }
            fixed (VkBufferImageCopy* region = regions) vkCmdCopyBufferToImage(commands, staging, _image, VkImageLayout.TransferDstOptimal, (uint)regions.Length, region);
            barrier.oldLayout = VkImageLayout.TransferDstOptimal; barrier.newLayout = VkImageLayout.ShaderReadOnlyOptimal; barrier.srcAccessMask = VkAccessFlags.TransferWrite; barrier.dstAccessMask = VkAccessFlags.ShaderRead;
            vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.FragmentShader, 0, 0, null, 0, null, 1, &barrier);
            Check(vkEndCommandBuffer(commands));
            var fenceInfo = new VkFenceCreateInfo { sType = VkStructureType.FenceCreateInfo }; Check(vkCreateFence(_device, ref fenceInfo, null, out fence));
            var submit = new VkSubmitInfo { sType = VkStructureType.SubmitInfo, commandBufferCount = 1, pCommandBuffers = &commands };
            Check(vkQueueSubmit(new(info.GraphicsQueue), 1, ref submit, fence));
            Check(vkWaitForFences(_device, 1, ref fence, true, ulong.MaxValue));
        }
        catch { Dispose(); throw; }
        finally
        {
            if (fence.Handle != 0) vkDestroyFence(_device, fence, null);
            if (pool.Handle != 0) vkDestroyCommandPool(_device, pool, null);
            if (staging.Handle != 0) vkDestroyBuffer(_device, staging, null);
            if (stagingMemory.Handle != 0) vkFreeMemory(_device, stagingMemory, null);
        }
    }
    private VkDeviceMemory Allocate(VkPhysicalDevice physical, VkMemoryRequirements requirements, VkMemoryPropertyFlags flags)
    {
        vkGetPhysicalDeviceMemoryProperties(physical, out var properties);
        VkMemoryType* types = &properties.memoryTypes_0;
        for (uint index = 0; index < properties.memoryTypeCount; index++)
            if ((requirements.memoryTypeBits & (1u << (int)index)) != 0 && (types[index].propertyFlags & flags) == flags)
            {
                var allocate = new VkMemoryAllocateInfo { sType = VkStructureType.MemoryAllocateInfo, allocationSize = requirements.size, memoryTypeIndex = index };
                Check(vkAllocateMemory(_device, ref allocate, null, out var memory)); return memory;
            }
        throw new NotSupportedException("No Vulkan memory type satisfies " + flags);
    }
    internal void Bind(ResourceSet resourceSet, uint binding)
    {
        var property = resourceSet.GetType().GetProperty("DescriptorSet", BindingFlags.Public | BindingFlags.Instance) ?? throw new NotSupportedException("Veldrid descriptor interop is unavailable.");
        var image = new VkDescriptorImageInfo { imageView = _view, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var write = new VkWriteDescriptorSet { sType = VkStructureType.WriteDescriptorSet, dstSet = (VkDescriptorSet)property.GetValue(resourceSet)!, dstBinding = binding, descriptorCount = 1, descriptorType = VkDescriptorType.SampledImage, pImageInfo = &image };
        vkUpdateDescriptorSets(_device, 1, &write, 0, null);
    }
    private static void Check(VkResult result) { if (result != VkResult.Success) throw new InvalidOperationException("Vulkan ASTC: " + result); }
    public void Dispose()
    {
        if (_view.Handle != 0) { vkDestroyImageView(_device, _view, null); _view = default; }
        if (_image.Handle != 0) { vkDestroyImage(_device, _image, null); _image = default; }
        if (_memory.Handle != 0) { vkFreeMemory(_device, _memory, null); _memory = default; }
        _allocation?.Dispose(); _allocation = null;
    }
}
