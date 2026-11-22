// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Hikari.ImGui.Demo;

/// <summary>
/// Silk.NET Vulkan rendering backend for Hikari.ImGui.
/// Manages Vulkan pipelines, descriptor sets, dynamic vertex/index buffers, and records
/// ImDrawData overlay commands into active command buffers with dynamic scissors.
/// </summary>
public sealed unsafe class VulkanRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Queue _queue;
    private readonly CommandPool _commandPool;

    private DescriptorSetLayout _descriptorSetLayoutTexture;
    private DescriptorSetLayout _descriptorSetLayoutSampler;
    private DescriptorPool _descriptorPool;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;
    private Sampler _fontSampler;
    private DescriptorSet _fontSamplerDescriptorSet;

    private Image _fontImage;
    private DeviceMemory _fontMemory;
    private ImageView _fontImageView;
    private DescriptorSet _fontDescriptorSet;

    /// <summary>Tracks Vulkan image and descriptor resources for textures managed via <see cref="ImTextureData"/>.</summary>
    private struct VulkanTextureResource
    {
        /// <summary>Backing Vulkan image.</summary>
        public Image Image;
        /// <summary>Device memory bound to <see cref="Image"/>.</summary>
        public DeviceMemory Memory;
        /// <summary>Shader-read image view for <see cref="Image"/>.</summary>
        public ImageView ImageView;
        /// <summary>Descriptor set bound at Set 0 for this texture.</summary>
        public DescriptorSet DescriptorSet;
    }

    /// <summary>Owned Vulkan texture resources keyed by their <see cref="DescriptorSet.Handle"/>.</summary>
    private readonly Dictionary<ulong, VulkanTextureResource> _textures = new();

    /// <summary>
    /// Number of frames that can be in flight at once. Matches <see cref="VulkanWindowContext.MaxFramesInFlight"/>.
    /// Each slot owns its own vertex and index memory so that a frame never writes memory that a previously
    /// submitted frame may still be reading.
    /// </summary>
    public const int FrameSlotCount = 2;

    /// <summary>Per-slot vertex buffer, memory and capacity in bytes.</summary>
    private readonly FrameBufferSlot[] _frameBuffers = new FrameBufferSlot[FrameSlotCount];

    private bool _isInitialized;

    /// <summary>
    /// One in-flight frame's vertex and index storage, with the memory that backs each buffer and its capacity
    /// in bytes. A slot's handles change only when that slot grows (see <c>UpdateBuffers</c>).
    /// </summary>
    private struct FrameBufferSlot
    {
        /// <summary>Vertex buffer bound for this slot.</summary>
        public Buffer VertexBuffer;

        /// <summary>Device memory backing <see cref="VertexBuffer"/>.</summary>
        public DeviceMemory VertexMemory;

        /// <summary>Capacity of <see cref="VertexBuffer"/> in bytes.</summary>
        public ulong VertexBufferSize;

        /// <summary>Index buffer bound for this slot.</summary>
        public Buffer IndexBuffer;

        /// <summary>Device memory backing <see cref="IndexBuffer"/>.</summary>
        public DeviceMemory IndexMemory;

        /// <summary>Capacity of <see cref="IndexBuffer"/> in bytes.</summary>
        public ulong IndexBufferSize;
    }

    #region Rendering API

    /// <summary>Creates the Vulkan backend and initializes its pipelines, descriptors and dynamic buffers.</summary>
    /// <param name="vk">Silk.NET Vulkan API entry point.</param>
    /// <param name="device">Logical device used for all created resources.</param>
    /// <param name="physicalDevice">Physical device selected by the host.</param>
    /// <param name="queue">Graphics queue used for uploads and buffer updates.</param>
    /// <param name="commandPool">Command pool used for one-shot resource uploads.</param>
    /// <param name="renderPass">Render pass the overlay pipeline is compatible with.</param>
    public VulkanRenderer(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        Queue queue,
        CommandPool commandPool,
        RenderPass renderPass)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _queue = queue;
        _commandPool = commandPool;

        InitResources(renderPass);
        if (ImGui.GetCurrentContext() != null)
            ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
    }

    /// <summary>Determines whether a Vulkan API instance can be loaded on the current host.</summary>
    /// <returns>True when the Vulkan API entry point resolves successfully; otherwise false.</returns>
    public static bool IsVulkanAvailable()
    {
        try
        {
            var vk = Vk.GetApi();
            return vk != null;
        }
        catch
        {
            return false;
        }
    }

    private void InitResources(RenderPass renderPass)
    {
        CreateDescriptorPool();
        CreateDescriptorSetLayout();
        CreatePipelineLayout();
        CreatePipeline(renderPass);
        CreateFontSampler();
        _isInitialized = true;
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new()
        {
            Type = DescriptorType.SampledImage,
            DescriptorCount = 16
        };
        poolSizes[1] = new()
        {
            Type = DescriptorType.Sampler,
            DescriptorCount = 16
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = 32,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes
        };

        _vk.CreateDescriptorPool(_device, &poolInfo, null, out _descriptorPool);
    }

    private void CreateDescriptorSetLayout()
    {
        // Set 0: Sampled Image (texture2D)
        DescriptorSetLayoutBinding texBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutCreateInfo texLayoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &texBinding
        };

        _vk.CreateDescriptorSetLayout(_device, &texLayoutInfo, null, out _descriptorSetLayoutTexture);

        // Set 1: Sampler
        DescriptorSetLayoutBinding samplerBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.Sampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        DescriptorSetLayoutCreateInfo samplerLayoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &samplerBinding
        };

        _vk.CreateDescriptorSetLayout(_device, &samplerLayoutInfo, null, out _descriptorSetLayoutSampler);
    }

    private void CreatePipelineLayout()
    {
        // Canonical push constant: vec2 uScale, vec2 uTranslate (16 bytes = 4 floats)
        PushConstantRange pushConstant = new()
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = sizeof(float) * 4
        };

        DescriptorSetLayout* setLayouts = stackalloc DescriptorSetLayout[2];
        setLayouts[0] = _descriptorSetLayoutTexture;
        setLayouts[1] = _descriptorSetLayoutSampler;

        PipelineLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 2,
            PSetLayouts = setLayouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstant
        };

        _vk.CreatePipelineLayout(_device, &layoutInfo, null, out _pipelineLayout);
    }

    private void CreatePipeline(RenderPass renderPass)
    {
        // Dynamic states: Viewport and Scissor
        DynamicState* dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
        PipelineDynamicStateCreateInfo dynamicStateInfo = new()
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        // Vertex Input: ImDrawVert (pos: float2, uv: float2, col: ubyte4)
        VertexInputBindingDescription bindingDesc = new()
        {
            Binding = 0,
            Stride = (uint)sizeof(ImDrawVert),
            InputRate = VertexInputRate.Vertex
        };

        VertexInputAttributeDescription* attribs = stackalloc VertexInputAttributeDescription[3];
        attribs[0] = new() { Location = 0, Binding = 0, Format = Format.R32G32Sfloat, Offset = 0 };
        attribs[1] = new() { Location = 1, Binding = 0, Format = Format.R32G32Sfloat, Offset = (uint)sizeof(ImVec2) };
        attribs[2] = new() { Location = 2, Binding = 0, Format = Format.R8G8B8A8Unorm, Offset = (uint)sizeof(ImVec2) * 2 };

        PipelineVertexInputStateCreateInfo vertexInputInfo = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &bindingDesc,
            VertexAttributeDescriptionCount = 3,
            PVertexAttributeDescriptions = attribs
        };

        PipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList
        };

        PipelineViewportStateCreateInfo viewportState = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        PipelineRasterizationStateCreateInfo rasterizer = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1.0f
        };

        PipelineMultisampleStateCreateInfo multisampling = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        PipelineColorBlendAttachmentState colorBlend = new()
        {
            BlendEnable = true,
            SrcColorBlendFactor = BlendFactor.SrcAlpha,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
        };

        PipelineColorBlendStateCreateInfo colorBlending = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorBlend
        };

        // Create embedded SPIR-V shader modules
        var vsModule = CreateShaderModule(GetEmbeddedVertexSpirV());
        var fsModule = CreateShaderModule(GetEmbeddedFragmentSpirV());

        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vsModule,
            PName = (byte*)SilkMarshal.StringToPtr("main")
        };
        stages[1] = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fsModule,
            PName = (byte*)SilkMarshal.StringToPtr("main")
        };

        GraphicsPipelineCreateInfo pipelineInfo = new()
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInputInfo,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizer,
            PMultisampleState = &multisampling,
            PColorBlendState = &colorBlending,
            PDynamicState = &dynamicStateInfo,
            Layout = _pipelineLayout,
            RenderPass = renderPass,
            Subpass = 0
        };

        _vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, out _pipeline);

        SilkMarshal.Free((nint)stages[0].PName);
        SilkMarshal.Free((nint)stages[1].PName);
        _vk.DestroyShaderModule(_device, vsModule, null);
        _vk.DestroyShaderModule(_device, fsModule, null);
    }

    private ShaderModule CreateShaderModule(byte[] spirv)
    {
        fixed (byte* ptr = spirv)
        {
            ShaderModuleCreateInfo info = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)ptr
            };
            _vk.CreateShaderModule(_device, &info, null, out var module);
            return module;
        }
    }

    private void CreateFontSampler()
    {
        SamplerCreateInfo samplerInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MaxAnisotropy = 1.0f
        };

        _vk.CreateSampler(_device, &samplerInfo, null, out _fontSampler);
    }

    /// <summary>Uploads the font atlas pixels, creates the font image view and records the atlas texture identifier.</summary>
    /// <param name="atlas">Font atlas whose texture data is uploaded.</param>
    public void CreateFontTexture(ImFontAtlas atlas)
    {
        atlas.GetTexDataAsRGBA32(out byte[] pixels, out int width, out int height);

        ulong uploadSize = (ulong)(width * height * 4);

        // Create staging buffer
        CreateBuffer(uploadSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var stagingBuffer, out var stagingMemory);

        void* data;
        _vk.MapMemory(_device, stagingMemory, 0, uploadSize, 0, &data);
        fixed (byte* pixelPtr = pixels)
        {
            System.Buffer.MemoryCopy(pixelPtr, data, uploadSize, uploadSize);
        }
        _vk.UnmapMemory(_device, stagingMemory);

        // Create target image
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            InitialLayout = ImageLayout.Undefined
        };

        _vk.CreateImage(_device, &imageInfo, null, out _fontImage);

        _vk.GetImageMemoryRequirements(_device, _fontImage, out var memReqs);
        uint memType = FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memType
        };

        _vk.AllocateMemory(_device, &allocInfo, null, out _fontMemory);
        _vk.BindImageMemory(_device, _fontImage, _fontMemory, 0);

        // Execute copy command buffer
        CommandBufferAllocateInfo cmdAlloc = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        _vk.AllocateCommandBuffers(_device, &cmdAlloc, out var cmd);

        CommandBufferBeginInfo beginInfo = new() { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        _vk.BeginCommandBuffer(cmd, &beginInfo);

        // Transition image layout to TransferDstOptimal
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _fontImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit
        };

        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &barrier);

        BufferImageCopy region = new()
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D((uint)width, (uint)height, 1)
        };

        _vk.CmdCopyBufferToImage(cmd, stagingBuffer, _fontImage, ImageLayout.TransferDstOptimal, 1, &region);

        // Transition image layout to ShaderReadOnlyOptimal
        barrier.OldLayout = ImageLayout.TransferDstOptimal;
        barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        barrier.DstAccessMask = AccessFlags.ShaderReadBit;

        _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier);

        _vk.EndCommandBuffer(cmd);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };

        _vk.QueueSubmit(_queue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(_queue);

        _vk.FreeCommandBuffers(_device, _commandPool, 1, &cmd);
        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        // Create image view
        ImageViewCreateInfo viewInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _fontImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };

        _vk.CreateImageView(_device, &viewInfo, null, out _fontImageView);

        // Allocate descriptor set for font texture (Set 0)
        var texLayout = _descriptorSetLayoutTexture;
        DescriptorSetAllocateInfo dsAllocTex = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &texLayout
        };

        _vk.AllocateDescriptorSets(_device, &dsAllocTex, out _fontDescriptorSet);

        // Allocate descriptor set for font sampler (Set 1)
        var samplerLayout = _descriptorSetLayoutSampler;
        DescriptorSetAllocateInfo dsAllocSampler = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &samplerLayout
        };

        _vk.AllocateDescriptorSets(_device, &dsAllocSampler, out _fontSamplerDescriptorSet);

        // Update Set 0 (Sampled Image)
        DescriptorImageInfo descImage = new()
        {
            ImageView = _fontImageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        WriteDescriptorSet writeTex = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _fontDescriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.SampledImage,
            PImageInfo = &descImage
        };

        // Update Set 1 (Sampler)
        DescriptorImageInfo descSampler = new()
        {
            Sampler = _fontSampler
        };

        WriteDescriptorSet writeSampler = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _fontSamplerDescriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.Sampler,
            PImageInfo = &descSampler
        };

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        writes[0] = writeTex;
        writes[1] = writeSampler;
        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);

        atlas.SetTexID((IntPtr)_fontDescriptorSet.Handle);
        if (atlas.TexData != null)
        {
            atlas.TexData.SetStatus(ImTextureStatus.OK);
            atlas.TexData.Updates.Clear();
        }
        _textures[_fontDescriptorSet.Handle] = new VulkanTextureResource
        {
            Image = _fontImage,
            Memory = _fontMemory,
            ImageView = _fontImageView,
            DescriptorSet = _fontDescriptorSet
        };
    }

    /// <summary>
    /// Processes a single <see cref="ImTextureData"/> create, partial update, or destroy request.
    /// </summary>
    /// <param name="tex">Texture data descriptor supplied via <see cref="ImDrawData.Textures"/>.</param>
    private void UpdateTexture(ImTextureData tex)
    {
        if (tex.Status == ImTextureStatus.WantCreate)
        {
            if (tex.Pixels == null || tex.Width <= 0 || tex.Height <= 0)
                return;

            ulong uploadSize = (ulong)(tex.Width * tex.Height * tex.BytesPerPixel);
            CreateBuffer(uploadSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var stagingBuffer, out var stagingMemory);

            void* data;
            _vk.MapMemory(_device, stagingMemory, 0, uploadSize, 0, &data);
            fixed (byte* pixelPtr = tex.Pixels)
            {
                System.Buffer.MemoryCopy(pixelPtr, data, uploadSize, uploadSize);
            }
            _vk.UnmapMemory(_device, stagingMemory);

            Format vkFormat = tex.Format == ImTextureFormat.Alpha8 ? Format.R8Unorm : Format.R8G8B8A8Unorm;
            ImageCreateInfo imageInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = vkFormat,
                Extent = new Extent3D((uint)tex.Width, (uint)tex.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                InitialLayout = ImageLayout.Undefined
            };

            _vk.CreateImage(_device, &imageInfo, null, out Image image);
            _vk.GetImageMemoryRequirements(_device, image, out var memReqs);
            uint memType = FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);
            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = memType
            };
            _vk.AllocateMemory(_device, &allocInfo, null, out DeviceMemory memory);
            _vk.BindImageMemory(_device, image, memory, 0);

            CommandBufferAllocateInfo cmdAlloc = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };
            _vk.AllocateCommandBuffers(_device, &cmdAlloc, out var cmd);
            CommandBufferBeginInfo beginInfo = new() { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            _vk.BeginCommandBuffer(cmd, &beginInfo);

            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit
            };
            _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &barrier);

            BufferImageCopy region = new()
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)tex.Width, (uint)tex.Height, 1)
            };
            _vk.CmdCopyBufferToImage(cmd, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);

            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier);
            _vk.EndCommandBuffer(cmd);

            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd
            };
            _vk.QueueSubmit(_queue, 1, &submitInfo, default);
            _vk.QueueWaitIdle(_queue);

            _vk.FreeCommandBuffers(_device, _commandPool, 1, &cmd);
            _vk.DestroyBuffer(_device, stagingBuffer, null);
            _vk.FreeMemory(_device, stagingMemory, null);

            ImageViewCreateInfo viewInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = vkFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            _vk.CreateImageView(_device, &viewInfo, null, out ImageView imageView);

            var texLayout = _descriptorSetLayoutTexture;
            DescriptorSetAllocateInfo dsAllocTex = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &texLayout
            };
            _vk.AllocateDescriptorSets(_device, &dsAllocTex, out DescriptorSet ds);

            DescriptorImageInfo descImage = new()
            {
                ImageView = imageView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal
            };
            WriteDescriptorSet writeTex = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = ds,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.SampledImage,
                PImageInfo = &descImage
            };
            _vk.UpdateDescriptorSets(_device, 1, &writeTex, 0, null);

            _textures[ds.Handle] = new VulkanTextureResource
            {
                Image = image,
                Memory = memory,
                ImageView = imageView,
                DescriptorSet = ds
            };
            if (_fontDescriptorSet.Handle == 0)
            {
                _fontImage = image;
                _fontMemory = memory;
                _fontImageView = imageView;
                _fontDescriptorSet = ds;
            }

            tex.SetTexID((IntPtr)ds.Handle);
            tex.SetStatus(ImTextureStatus.OK);
            tex.Updates.Clear();
        }
        else if (tex.Status == ImTextureStatus.WantUpdates)
        {
            if (tex.TexID == IntPtr.Zero || tex.Pixels == null || tex.Updates.Count == 0)
                return;
            if (!_textures.TryGetValue((ulong)tex.TexID, out VulkanTextureResource res))
                return;

            ulong totalBytes = 0;
            for (int i = 0; i < tex.Updates.Count; i++)
                totalBytes += (ulong)(tex.Updates[i].W * tex.Updates[i].H * tex.BytesPerPixel);
            if (totalBytes == 0)
            {
                tex.SetStatus(ImTextureStatus.OK);
                tex.Updates.Clear();
                return;
            }

            CreateBuffer(totalBytes, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var stagingBuffer, out var stagingMemory);
            void* mapped;
            _vk.MapMemory(_device, stagingMemory, 0, totalBytes, 0, &mapped);
            byte* dstPtr = (byte*)mapped;

            var regions = new BufferImageCopy[tex.Updates.Count];
            ulong bufferOffset = 0;
            fixed (byte* srcBase = tex.Pixels)
            {
                for (int i = 0; i < tex.Updates.Count; i++)
                {
                    ImTextureRect r = tex.Updates[i];
                    int rowBytes = r.W * tex.BytesPerPixel;
                    for (int y = 0; y < r.H; y++)
                    {
                        byte* srcRow = srcBase + ((r.Y + y) * tex.Width + r.X) * tex.BytesPerPixel;
                        System.Buffer.MemoryCopy(srcRow, dstPtr + bufferOffset + (ulong)(y * rowBytes), (ulong)rowBytes, (ulong)rowBytes);
                    }

                    regions[i] = new BufferImageCopy
                    {
                        BufferOffset = bufferOffset,
                        BufferRowLength = (uint)r.W,
                        BufferImageHeight = (uint)r.H,
                        ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                        ImageOffset = new Offset3D(r.X, r.Y, 0),
                        ImageExtent = new Extent3D(r.W, r.H, 1)
                    };
                    bufferOffset += (ulong)(rowBytes * r.H);
                }
            }
            _vk.UnmapMemory(_device, stagingMemory);

            CommandBufferAllocateInfo cmdAlloc = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1
            };
            _vk.AllocateCommandBuffers(_device, &cmdAlloc, out var cmd);
            CommandBufferBeginInfo beginInfo = new() { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            _vk.BeginCommandBuffer(cmd, &beginInfo);

            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.ShaderReadOnlyOptimal,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = res.Image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstAccessMask = AccessFlags.TransferWriteBit
            };
            _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.FragmentShaderBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &barrier);

            fixed (BufferImageCopy* pRegions = regions)
            {
                _vk.CmdCopyBufferToImage(cmd, stagingBuffer, res.Image, ImageLayout.TransferDstOptimal, (uint)regions.Length, pRegions);
            }

            barrier.OldLayout = ImageLayout.TransferDstOptimal;
            barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            _vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier);
            _vk.EndCommandBuffer(cmd);

            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd
            };
            _vk.QueueSubmit(_queue, 1, &submitInfo, default);
            _vk.QueueWaitIdle(_queue);

            _vk.FreeCommandBuffers(_device, _commandPool, 1, &cmd);
            _vk.DestroyBuffer(_device, stagingBuffer, null);
            _vk.FreeMemory(_device, stagingMemory, null);

            tex.SetStatus(ImTextureStatus.OK);
            tex.Updates.Clear();
        }
        else if (tex.Status == ImTextureStatus.WantDestroy)
        {
            ulong handle = (ulong)tex.TexID;
            if (handle != 0 && _textures.TryGetValue(handle, out VulkanTextureResource res))
            {
                _vk.DeviceWaitIdle(_device);
                if (res.ImageView.Handle != 0) _vk.DestroyImageView(_device, res.ImageView, null);
                if (res.Image.Handle != 0) { _vk.DestroyImage(_device, res.Image, null); _vk.FreeMemory(_device, res.Memory, null); }
                if (res.DescriptorSet.Handle != 0)
                {
                    var ds = res.DescriptorSet;
                    _vk.FreeDescriptorSets(_device, _descriptorPool, 1, &ds);
                }
                _textures.Remove(handle);
                if (_fontDescriptorSet.Handle == handle)
                {
                    _fontImage = default;
                    _fontMemory = default;
                    _fontImageView = default;
                    _fontDescriptorSet = default;
                }
            }
            tex.SetTexID(IntPtr.Zero);
            tex.SetStatus(ImTextureStatus.Destroyed);
        }
    }

    /// <summary>Records one frame of draw data into an active command buffer with dynamic scissors.</summary>
    /// <param name="cmdBuffer">Command buffer receiving the overlay draw commands.</param>
    /// <param name="drawData">Aggregated frame draw data produced by the ImGui port.</param>
    /// <param name="frameSlot">
    /// In-flight frame slot (0 to <see cref="FrameSlotCount"/> - 1) whose vertex and index memory this frame
    /// writes. The caller must have waited for that slot's previous submission to finish.
    /// </param>
    public void Render(CommandBuffer cmdBuffer, ImDrawData drawData, int frameSlot)
    {
        if (!_isInitialized)
            return;

        if (drawData.Textures != null)
        {
            for (int i = 0; i < drawData.Textures.Count; i++)
                UpdateTexture(drawData.Textures[i]);
        }

        if (drawData.CmdListsCount == 0 || drawData.DisplaySize.X <= 0f || drawData.DisplaySize.Y <= 0f)
            return;

        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0)
            return;

        // Ensure dynamic buffers are large enough
        FrameBufferSlot slot = _frameBuffers[frameSlot];
        UpdateBuffers(drawData, ref slot);
        _frameBuffers[frameSlot] = slot;

        _vk.CmdBindPipeline(cmdBuffer, PipelineBindPoint.Graphics, _pipeline);

        // Bind default sampler descriptor set (Set 1)
        var samplerDs = _fontSamplerDescriptorSet;
        _vk.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 1, 1, &samplerDs, 0, null);

        // Set viewport matching physical framebuffer dimensions
        Viewport viewport = new(0f, 0f, (float)fbWidth, (float)fbHeight, 0f, 1f);
        _vk.CmdSetViewport(cmdBuffer, 0, 1, &viewport);

        // Canonical scale and translation push constants (vec2 uScale, vec2 uTranslate)
        float* constants = stackalloc float[4];
        constants[0] = 2.0f / drawData.DisplaySize.X; // uScale.x
        constants[1] = 2.0f / drawData.DisplaySize.Y; // uScale.y
        constants[2] = -1.0f - drawData.DisplayPos.X * constants[0]; // uTranslate.x
        constants[3] = -1.0f - drawData.DisplayPos.Y * constants[1]; // uTranslate.y

        _vk.CmdPushConstants(cmdBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, 0, sizeof(float) * 4, constants);

        // Bind vertex and index buffers
        var vb = slot.VertexBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(cmdBuffer, 0, 1, &vb, &offset);
        _vk.CmdBindIndexBuffer(cmdBuffer, slot.IndexBuffer, 0, IndexType.Uint16);

        // Render command lists
        uint globalVtxOffset = 0;
        uint globalIdxOffset = 0;
        DescriptorSet lastTexture = default;

        ImVec2 clipOff = drawData.DisplayPos;
        ImVec2 clipScale = drawData.FramebufferScale;

        for (int i = 0; i < drawData.CmdLists.Count; i++)
        {
            var cmdList = drawData.CmdLists[i];
            for (int j = 0; j < cmdList.CmdBuffer.Count; j++)
            {
                var cmd = cmdList.CmdBuffer[j];

                // A user callback replaces the triangle submission for its command. Canonical checks the
                // callback first.
                if (cmd.UserCallback != null)
                {
                    cmd.UserCallback(cmdList, cmd);
                    continue;
                }

                if (cmd.ElemCount == 0) continue;

                // Scissor test (scaled by FramebufferScale and clamped to physical bounds)
                float clipMinX = (cmd.ClipRect.X - clipOff.X) * clipScale.X;
                float clipMinY = (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                float clipMaxX = (cmd.ClipRect.Z - clipOff.X) * clipScale.X;
                float clipMaxY = (cmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                if (clipMinX < 0f) clipMinX = 0f;
                if (clipMinY < 0f) clipMinY = 0f;
                if (clipMaxX > fbWidth) clipMaxX = fbWidth;
                if (clipMaxY > fbHeight) clipMaxY = fbHeight;
                if (clipMaxX <= clipMinX || clipMaxY <= clipMinY) continue;

                // Bind texture descriptor set if changed. The identifier is resolved through
                // ImDrawCmd.GetTexID() because the command holds only an ImTextureRef and an
                // atlas-backed reference may be updated by the texture pipeline. A zero handle means
                // the command carries no explicit texture, so the font descriptor set is bound.
                DescriptorSet texDs = new((ulong)cmd.GetTexID());
                if (texDs.Handle == 0) texDs = _fontDescriptorSet;
                if (texDs.Handle != lastTexture.Handle)
                {
                    _vk.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &texDs, 0, null);
                    lastTexture = texDs;
                }

                Rect2D scissor = new(new Offset2D((int)clipMinX, (int)clipMinY), new Extent2D((uint)(clipMaxX - clipMinX), (uint)(clipMaxY - clipMinY)));
                _vk.CmdSetScissor(cmdBuffer, 0, 1, &scissor);

                _vk.CmdDrawIndexed(cmdBuffer, cmd.ElemCount, 1, cmd.IdxOffset + globalIdxOffset, (int)(cmd.VtxOffset + globalVtxOffset), 0);
            }

            globalVtxOffset += (uint)cmdList.VtxBuffer.Count;
            globalIdxOffset += (uint)cmdList.IdxBuffer.Count;
        }
    }

    /// <summary>
    /// Grows the slot's buffers when needed and copies this frame's geometry into them. Growing destroys only
    /// the slot being replaced, so the caller must ensure that slot's previous submission has completed.
    /// </summary>
    /// <param name="drawData">Draw data whose vertices and indices are uploaded.</param>
    /// <param name="slot">Slot to update; its handles are replaced when the buffers grow.</param>
    private void UpdateBuffers(ImDrawData drawData, ref FrameBufferSlot slot)
    {
        ulong requiredVtxBytes = (ulong)(drawData.TotalVtxCount * sizeof(ImDrawVert));
        ulong requiredIdxBytes = (ulong)(drawData.TotalIdxCount * sizeof(ushort));

        if (slot.VertexBuffer.Handle == 0 || slot.VertexBufferSize < requiredVtxBytes)
        {
            if (slot.VertexBuffer.Handle != 0)
            {
                _vk.DestroyBuffer(_device, slot.VertexBuffer, null);
                _vk.FreeMemory(_device, slot.VertexMemory, null);
            }
            slot.VertexBufferSize = Math.Max(requiredVtxBytes, 65536);
            CreateBuffer(slot.VertexBufferSize, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out slot.VertexBuffer, out slot.VertexMemory);
        }

        if (slot.IndexBuffer.Handle == 0 || slot.IndexBufferSize < requiredIdxBytes)
        {
            if (slot.IndexBuffer.Handle != 0)
            {
                _vk.DestroyBuffer(_device, slot.IndexBuffer, null);
                _vk.FreeMemory(_device, slot.IndexMemory, null);
            }
            slot.IndexBufferSize = Math.Max(requiredIdxBytes, 65536);
            CreateBuffer(slot.IndexBufferSize, BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out slot.IndexBuffer, out slot.IndexMemory);
        }

        // Copy vertices
        void* vtxData;
        _vk.MapMemory(_device, slot.VertexMemory, 0, requiredVtxBytes, 0, &vtxData);
        byte* vtxDest = (byte*)vtxData;
        for (int i = 0; i < drawData.CmdLists.Count; i++)
        {
            var list = drawData.CmdLists[i];
            fixed (ImDrawVert* src = CollectionsMarshal.AsSpan(list.VtxBuffer))
            {
                ulong bytes = (ulong)(list.VtxBuffer.Count * sizeof(ImDrawVert));
                System.Buffer.MemoryCopy(src, vtxDest, bytes, bytes);
                vtxDest += bytes;
            }
        }
        _vk.UnmapMemory(_device, slot.VertexMemory);

        // Copy indices
        void* idxData;
        _vk.MapMemory(_device, slot.IndexMemory, 0, requiredIdxBytes, 0, &idxData);
        byte* idxDest = (byte*)idxData;
        for (int i = 0; i < drawData.CmdLists.Count; i++)
        {
            var list = drawData.CmdLists[i];
            fixed (ushort* src = CollectionsMarshal.AsSpan(list.IdxBuffer))
            {
                ulong bytes = (ulong)(list.IdxBuffer.Count * sizeof(ushort));
                System.Buffer.MemoryCopy(src, idxDest, bytes, bytes);
                idxDest += bytes;
            }
        }
        _vk.UnmapMemory(_device, slot.IndexMemory);
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory memory)
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        _vk.CreateBuffer(_device, &bufferInfo, null, out buffer);

        _vk.GetBufferMemoryRequirements(_device, buffer, out var memReqs);
        uint memType = FindMemoryType(memReqs.MemoryTypeBits, properties);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memType
        };

        _vk.AllocateMemory(_device, &allocInfo, null, out memory);
        _vk.BindBufferMemory(_device, buffer, memory, 0);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 && (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }
        throw new InvalidOperationException("Failed to find suitable Vulkan memory type!");
    }

    private static byte[] GetEmbeddedVertexSpirV()
    {
        // Minimal standard SPIR-V vertex shader compiled for ImGui
        // Reads location 0 (Pos: vec2), location 1 (UV: vec2), location 2 (Col: vec4)
        // Push constant mat4 uProj
        return EmbeddedShaderBytecode.VertexSpirV;
    }

    private static byte[] GetEmbeddedFragmentSpirV()
    {
        // Minimal standard SPIR-V fragment shader compiled for ImGui
        // Reads in UV, in Col, uniform sampler2D sTexture -> OutColor = Col * texture(sTexture, UV)
        return EmbeddedShaderBytecode.FragmentSpirV;
    }

    /// <summary>Releases the Vulkan buffers, images, samplers, pipeline and descriptor objects owned by this backend.</summary>
    public void Dispose()
    {
        if (!_isInitialized) return;

        for (int i = 0; i < FrameSlotCount; i++)
        {
            FrameBufferSlot slot = _frameBuffers[i];
            if (slot.VertexBuffer.Handle != 0) { _vk.DestroyBuffer(_device, slot.VertexBuffer, null); _vk.FreeMemory(_device, slot.VertexMemory, null); }
            if (slot.IndexBuffer.Handle != 0) { _vk.DestroyBuffer(_device, slot.IndexBuffer, null); _vk.FreeMemory(_device, slot.IndexMemory, null); }
            _frameBuffers[i] = default;
        }
        foreach (VulkanTextureResource res in _textures.Values)
        {
            if (res.ImageView.Handle != 0) _vk.DestroyImageView(_device, res.ImageView, null);
            if (res.Image.Handle != 0) { _vk.DestroyImage(_device, res.Image, null); _vk.FreeMemory(_device, res.Memory, null); }
        }
        _textures.Clear();
        _fontImageView = default;
        _fontImage = default;
        _fontMemory = default;
        _fontDescriptorSet = default;
        if (_fontSampler.Handle != 0) _vk.DestroySampler(_device, _fontSampler, null);
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(_device, _pipeline, null);
        if (_pipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        if (_descriptorSetLayoutTexture.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayoutTexture, null);
        if (_descriptorSetLayoutSampler.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayoutSampler, null);
        if (_descriptorPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _descriptorPool, null);

        _isInitialized = false;
    }

    #endregion
}
