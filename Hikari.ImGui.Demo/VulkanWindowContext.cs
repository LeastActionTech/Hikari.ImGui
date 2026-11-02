using System;
using System.Collections.Generic;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Hikari.ImGui.Demo;

/// <summary>
/// Minimal Vulkan window context managing instance, surface, device, swapchain, render pass,
/// framebuffers, command buffers, and per-frame presentation semaphores/fences.
/// Provides an active command buffer and render pass for VulkanRenderer to draw into.
/// </summary>
public sealed unsafe class VulkanWindowContext : IDisposable
{
    #region Handles & Device State

    private readonly IWindow _window;
    /// <summary>Gets the Silk.NET Vulkan entry point used for every call made by this context.</summary>
    public Vk Vk { get; }
    /// <summary>Gets the Vulkan instance created for the window's API surface and validation layers.</summary>
    public Instance Instance { get; private set; }
    /// <summary>Gets the presentation surface that binds the swapchain to the native window.</summary>
    public SurfaceKHR Surface { get; private set; }
    /// <summary>Gets the surface-extension dispatch table, or <see langword="null"/> before instance creation.</summary>
    public KhrSurface? KhrSurface { get; private set; }
    /// <summary>Gets the swapchain-extension dispatch table, or <see langword="null"/> before device creation.</summary>
    public KhrSwapchain? KhrSwapchain { get; private set; }

    /// <summary>Gets the physical device selected for rendering and presentation.</summary>
    public PhysicalDevice PhysicalDevice { get; private set; }
    /// <summary>Gets the logical device that owns every resource this context creates.</summary>
    public Device Device { get; private set; }
    /// <summary>Gets the queue that receives the recorded frame command buffers.</summary>
    public Queue GraphicsQueue { get; private set; }
    /// <summary>Gets the queue that presents completed swapchain images.</summary>
    public Queue PresentQueue { get; private set; }
    /// <summary>Gets the queue family index that supplies <see cref="GraphicsQueue"/>.</summary>
    public uint GraphicsQueueFamilyIndex { get; private set; }
    /// <summary>Gets the queue family index that supplies <see cref="PresentQueue"/>.</summary>
    public uint PresentQueueFamilyIndex { get; private set; }

    /// <summary>Gets the swapchain that owns the presentable images; recreated when the window resizes.</summary>
    public SwapchainKHR Swapchain { get; private set; }
    /// <summary>Gets the pixel format of the swapchain images.</summary>
    public Format SwapchainFormat { get; private set; }
    /// <summary>Gets the pixel size of the swapchain images.</summary>
    public Extent2D SwapchainExtent { get; private set; }
    /// <summary>Gets the presentation mode selected at swapchain creation.</summary>
    public PresentModeKHR PresentMode { get; private set; }
    /// <summary>Gets the swapchain images, one per presentable frame.</summary>
    public Image[] SwapchainImages { get; private set; } = Array.Empty<Image>();
    /// <summary>Gets one view per swapchain image, used as a colour attachment.</summary>
    public ImageView[] SwapchainImageViews { get; private set; } = Array.Empty<ImageView>();
    /// <summary>Gets one framebuffer per swapchain image, bound to <see cref="RenderPass"/>.</summary>
    public Framebuffer[] Framebuffers { get; private set; } = Array.Empty<Framebuffer>();

    private bool _currentVSync;

    /// <summary>Gets the render pass that clears and draws the swapchain images.</summary>
    public RenderPass RenderPass { get; private set; }
    /// <summary>Gets the pool from which the per-frame command buffers are allocated.</summary>
    public CommandPool CommandPool { get; private set; }
    /// <summary>Gets one command buffer per in-flight frame slot.</summary>
    public CommandBuffer[] CommandBuffers { get; private set; } = Array.Empty<CommandBuffer>();

    public const int MaxFramesInFlight = 2;
    private readonly Semaphore[] _imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
    private readonly Semaphore[] _renderFinishedSemaphores = new Semaphore[MaxFramesInFlight];
    private readonly Fence[] _inFlightFences = new Fence[MaxFramesInFlight];
    private readonly Fence[] _imagesInFlight = new Fence[8];

    private uint _currentFrame = 0;

    /// <summary>
    /// Gets the in-flight frame slot whose command buffer and synchronization objects the current frame uses.
    /// The slot is advanced by <c>EndFrameAndPresent</c> and always lies in
    /// <c>0 &lt;= value &lt; MaxFramesInFlight</c>.
    /// </summary>
    public int CurrentFrame => (int)_currentFrame;
    private uint _imageIndex = 0;
    private bool _frameActive = false;

    #endregion

    #region Construction

    public VulkanWindowContext(IWindow window)
    {
        _window = window;
        Vk = Vk.GetApi();

        CreateInstance();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapchain();
        CreateImageViews();
        CreateRenderPass();
        CreateFramebuffers();
        CreateCommandPool();
        CreateCommandBuffers();
        CreateSyncObjects();
    }

    private IVkSurface GetVkSurface()
    {
        if (_window.VkSurface != null)
            return _window.VkSurface;

        // Fallback for Silk.NET GlfwWindow where CoreVkSurface implements IVkSurface
        var prop = _window.GetType().GetProperty("CoreVkSurface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (prop?.GetValue(_window) is IVkSurface coreVk)
            return coreVk;

        throw new NotSupportedException("Could not obtain IVkSurface from the Silk.NET window.");
    }

    private void CreateInstance()
    {
        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr("Hikari ImGui Demo"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)SilkMarshal.StringToPtr("Hikari"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        var vkSurface = GetVkSurface();
        var surfaceExts = vkSurface.GetRequiredExtensions(out var count);
        List<string> extensions = new();
        for (var i = 0; i < count; i++)
        {
            var ext = SilkMarshal.PtrToString((nint)surfaceExts[i]);
            if (!string.IsNullOrEmpty(ext))
            {
                extensions.Add(ext);
            }
        }

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensions.Count,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions)
        };

        Instance localInstance;
        Result res = Vk.CreateInstance(in createInfo, null, &localInstance);
        SilkMarshal.Free((nint)appInfo.PApplicationName);
        SilkMarshal.Free((nint)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

        if (res != Result.Success)
            throw new InvalidOperationException($"Failed to create Vulkan instance: {res}");

        Instance = localInstance;
    }

    private void CreateSurface()
    {
        if (!Vk.TryGetInstanceExtension(Instance, out KhrSurface khrSurface))
            throw new NotSupportedException("VK_KHR_surface extension not available.");

        KhrSurface = khrSurface;
        Surface = GetVkSurface().Create<AllocationCallbacks>(Instance.ToHandle(), null).ToSurface();
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        Vk.EnumeratePhysicalDevices(Instance, &deviceCount, null);
        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan physical devices found.");

        PhysicalDevice* devices = stackalloc PhysicalDevice[(int)deviceCount];
        Vk.EnumeratePhysicalDevices(Instance, &deviceCount, devices);

        // Pick discrete GPU if available, else first
        PhysicalDevice chosen = devices[0];
        for (int i = 0; i < deviceCount; i++)
        {
            PhysicalDeviceProperties props;
            Vk.GetPhysicalDeviceProperties(devices[i], &props);
            if (props.DeviceType == PhysicalDeviceType.DiscreteGpu)
            {
                chosen = devices[i];
                break;
            }
        }

        PhysicalDevice = chosen;
    }

    private void CreateLogicalDevice()
    {
        FindQueueFamilies(PhysicalDevice, out uint graphicsFamily, out uint presentFamily);
        GraphicsQueueFamilyIndex = graphicsFamily;
        PresentQueueFamilyIndex = presentFamily;

        HashSet<uint> uniqueFamilies = [graphicsFamily, presentFamily];
        DeviceQueueCreateInfo* queueCreateInfos = stackalloc DeviceQueueCreateInfo[uniqueFamilies.Count];
        float priority = 1.0f;

        int qIdx = 0;
        foreach (uint family in uniqueFamilies)
        {
            queueCreateInfos[qIdx++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = family,
                QueueCount = 1,
                PQueuePriorities = &priority
            };
        }

        string[] extensions = [KhrSwapchain.ExtensionName];

        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = (uint)uniqueFamilies.Count,
            PQueueCreateInfos = queueCreateInfos,
            EnabledExtensionCount = (uint)extensions.Length,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions)
        };

        Device localDevice;
        Result res = Vk.CreateDevice(PhysicalDevice, in createInfo, null, &localDevice);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

        if (res != Result.Success)
            throw new InvalidOperationException($"Failed to create Vulkan logical device: {res}");

        Device = localDevice;

        Vk.GetDeviceQueue(Device, GraphicsQueueFamilyIndex, 0, out Queue gQueue);
        Vk.GetDeviceQueue(Device, PresentQueueFamilyIndex, 0, out Queue pQueue);
        GraphicsQueue = gQueue;
        PresentQueue = pQueue;

        if (!Vk.TryGetDeviceExtension(Instance, Device, out KhrSwapchain khrSwapchain))
            throw new NotSupportedException("VK_KHR_swapchain not available.");

        KhrSwapchain = khrSwapchain;
    }

    private void FindQueueFamilies(PhysicalDevice device, out uint graphicsFamily, out uint presentFamily)
    {
        uint queueFamilyCount = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);
        var queueFamilies = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
        Vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamilies);

        uint? gFam = null;
        uint? pFam = null;

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                gFam ??= i;
            }

            Bool32 presentSupport = false;
            KhrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, Surface, &presentSupport);
            if (presentSupport)
            {
                pFam ??= i;
            }

            if (gFam.HasValue && pFam.HasValue && gFam.Value == pFam.Value)
                break;
        }

        graphicsFamily = gFam ?? 0;
        presentFamily = pFam ?? graphicsFamily;
    }

    private void CreateSwapchain()
    {
        SurfaceCapabilitiesKHR capabilities;
        KhrSurface!.GetPhysicalDeviceSurfaceCapabilities(PhysicalDevice, Surface, &capabilities);

        uint formatCount = 0;
        KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, &formatCount, null);
        var surfaceFormats = stackalloc SurfaceFormatKHR[(int)formatCount];
        KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, &formatCount, surfaceFormats);

        SurfaceFormatKHR selectedFormat = surfaceFormats[0];
        for (int i = 0; i < formatCount; i++)
        {
            if (surfaceFormats[i].Format == Format.B8G8R8A8Unorm || surfaceFormats[i].Format == Format.R8G8B8A8Unorm)
            {
                selectedFormat = surfaceFormats[i];
                break;
            }
        }
        SwapchainFormat = selectedFormat.Format;

        var fbSize = _window.FramebufferSize;
        SwapchainExtent = new Extent2D
        {
            Width = (uint)Math.Clamp(fbSize.X, (int)capabilities.MinImageExtent.Width, (int)capabilities.MaxImageExtent.Width),
            Height = (uint)Math.Clamp(fbSize.Y, (int)capabilities.MinImageExtent.Height, (int)capabilities.MaxImageExtent.Height)
        };

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        // Query supported presentation modes from the physical device surface
        uint presentModeCount = 0;
        KhrSurface!.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Surface, &presentModeCount, null);
        var presentModes = stackalloc PresentModeKHR[(int)presentModeCount];
        if (presentModeCount > 0)
        {
            KhrSurface.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Surface, &presentModeCount, presentModes);
        }

        // When VSync is off, prefer Immediate (unconstrained frame pacing matching OpenGL swap interval 0)
        // or Mailbox (triple buffering without tearing). If VSync is on, use FIFO.
        PresentModeKHR selectedPresentMode = PresentModeKHR.FifoKhr;
        if (!_window.VSync)
        {
            bool immediateAvailable = false;
            bool mailboxAvailable = false;
            for (int i = 0; i < presentModeCount; i++)
            {
                if (presentModes[i] == PresentModeKHR.ImmediateKhr) immediateAvailable = true;
                if (presentModes[i] == PresentModeKHR.MailboxKhr) mailboxAvailable = true;
            }

            if (immediateAvailable)
            {
                selectedPresentMode = PresentModeKHR.ImmediateKhr;
            }
            else if (mailboxAvailable)
            {
                selectedPresentMode = PresentModeKHR.MailboxKhr;
            }
        }

        _currentVSync = _window.VSync;
        PresentMode = selectedPresentMode;

        SwapchainCreateInfoKHR createInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = Surface,
            MinImageCount = imageCount,
            ImageFormat = SwapchainFormat,
            ImageColorSpace = selectedFormat.ColorSpace,
            ImageExtent = SwapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = selectedPresentMode,
            Clipped = true,
            OldSwapchain = default
        };

        if (GraphicsQueueFamilyIndex != PresentQueueFamilyIndex)
        {
            uint* qfiPtr = stackalloc uint[2] { GraphicsQueueFamilyIndex, PresentQueueFamilyIndex };
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;
            createInfo.PQueueFamilyIndices = qfiPtr;
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        SwapchainKHR localSwapchain;
        Result res = KhrSwapchain!.CreateSwapchain(Device, in createInfo, null, &localSwapchain);
        if (res != Result.Success)
            throw new InvalidOperationException($"Failed to create swapchain: {res}");

        Swapchain = localSwapchain;

        uint actualCount = 0;
        KhrSwapchain.GetSwapchainImages(Device, Swapchain, &actualCount, null);
        SwapchainImages = new Image[actualCount];
        fixed (Image* pImages = SwapchainImages)
        {
            KhrSwapchain.GetSwapchainImages(Device, Swapchain, &actualCount, pImages);
        }
    }

    private void CreateImageViews()
    {
        SwapchainImageViews = new ImageView[SwapchainImages.Length];
        for (int i = 0; i < SwapchainImages.Length; i++)
        {
            ImageViewCreateInfo viewInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = SwapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = SwapchainFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };

            ImageView localView;
            if (Vk.CreateImageView(Device, in viewInfo, null, &localView) != Result.Success)
                throw new InvalidOperationException("Failed to create swapchain image view.");

            SwapchainImageViews[i] = localView;
        }
    }

    private void CreateRenderPass()
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = SwapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentReference colorRef = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef
        };

        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        RenderPassCreateInfo passInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        RenderPass localPass;
        if (Vk.CreateRenderPass(Device, in passInfo, null, &localPass) != Result.Success)
            throw new InvalidOperationException("Failed to create Vulkan renderpass.");

        RenderPass = localPass;
    }

    private void CreateFramebuffers()
    {
        Framebuffers = new Framebuffer[SwapchainImageViews.Length];
        for (int i = 0; i < SwapchainImageViews.Length; i++)
        {
            var view = SwapchainImageViews[i];
            FramebufferCreateInfo fbInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = RenderPass,
                AttachmentCount = 1,
                PAttachments = &view,
                Width = SwapchainExtent.Width,
                Height = SwapchainExtent.Height,
                Layers = 1
            };

            Framebuffer localFb;
            if (Vk.CreateFramebuffer(Device, in fbInfo, null, &localFb) != Result.Success)
                throw new InvalidOperationException("Failed to create framebuffer.");

            Framebuffers[i] = localFb;
        }
    }

    private void CreateCommandPool()
    {
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = GraphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        CommandPool localPool;
        if (Vk.CreateCommandPool(Device, in poolInfo, null, &localPool) != Result.Success)
            throw new InvalidOperationException("Failed to create command pool.");

        CommandPool = localPool;
    }

    private void CreateCommandBuffers()
    {
        CommandBuffers = new CommandBuffer[MaxFramesInFlight];
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)MaxFramesInFlight
        };

        fixed (CommandBuffer* pCmds = CommandBuffers)
        {
            Vk.AllocateCommandBuffers(Device, in allocInfo, pCmds);
        }
    }

    private void CreateSyncObjects()
    {
        SemaphoreCreateInfo semInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            Semaphore imgSem, renSem;
            Fence fence;
            Vk.CreateSemaphore(Device, in semInfo, null, &imgSem);
            Vk.CreateSemaphore(Device, in semInfo, null, &renSem);
            Vk.CreateFence(Device, in fenceInfo, null, &fence);

            _imageAvailableSemaphores[i] = imgSem;
            _renderFinishedSemaphores[i] = renSem;
            _inFlightFences[i] = fence;
        }
    }

    #endregion

    #region Swapchain Lifecycle

    /// <summary>
    /// Recreates the swapchain, image views, and framebuffers when window size or display scaling changes.
    /// </summary>
    public void RecreateSwapchain()
    {
        var fbSize = _window.FramebufferSize;
        if (fbSize.X <= 0 || fbSize.Y <= 0)
            return;

        WaitIdle();

        if (Framebuffers != null)
        {
            for (int i = 0; i < Framebuffers.Length; i++)
            {
                if (Framebuffers[i].Handle != 0) Vk.DestroyFramebuffer(Device, Framebuffers[i], null);
            }
        }

        if (SwapchainImageViews != null)
        {
            for (int i = 0; i < SwapchainImageViews.Length; i++)
            {
                if (SwapchainImageViews[i].Handle != 0) Vk.DestroyImageView(Device, SwapchainImageViews[i], null);
            }
        }

        if (Swapchain.Handle != 0)
        {
            KhrSwapchain!.DestroySwapchain(Device, Swapchain, null);
            Swapchain = default;
        }

        CreateSwapchain();
        CreateImageViews();
        CreateFramebuffers();

        for (int i = 0; i < _imagesInFlight.Length; i++)
        {
            _imagesInFlight[i] = default;
        }
    }

    /// <summary>
    /// Begins a frame by waiting for the in-flight fence, acquiring the next swapchain image,
    /// and beginning command buffer and render pass.
    /// </summary>
    public bool BeginFrame(out CommandBuffer cmdBuffer, out uint imageIndex)
    {
        cmdBuffer = default;
        imageIndex = 0;

        // Proactively detect window framebuffer resize or VSync mode toggle
        var fbSize = _window.FramebufferSize;
        if ((fbSize.X > 0 && fbSize.Y > 0 &&
            ((uint)fbSize.X != SwapchainExtent.Width || (uint)fbSize.Y != SwapchainExtent.Height)) ||
            _window.VSync != _currentVSync)
        {
            RecreateSwapchain();
        }

        Vk.WaitForFences(Device, 1, in _inFlightFences[_currentFrame], true, ulong.MaxValue);

        uint imgIdx = 0;
        Result acq = KhrSwapchain!.AcquireNextImage(Device, Swapchain, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, &imgIdx);
        if (acq == Result.ErrorOutOfDateKhr || acq == Result.SuboptimalKhr)
        {
            RecreateSwapchain();
            return false;
        }
        else if (acq != Result.Success)
        {
            return false;
        }

        _imageIndex = imgIdx;
        imageIndex = imgIdx;

        if (_imagesInFlight[_imageIndex].Handle != 0)
        {
            Vk.WaitForFences(Device, 1, in _imagesInFlight[_imageIndex], true, ulong.MaxValue);
        }
        _imagesInFlight[_imageIndex] = _inFlightFences[_currentFrame];

        Vk.ResetFences(Device, 1, in _inFlightFences[_currentFrame]);

        cmdBuffer = CommandBuffers[_currentFrame];
        Vk.ResetCommandBuffer(cmdBuffer, 0);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        Vk.BeginCommandBuffer(cmdBuffer, in beginInfo);

        ClearValue clearColor = new()
        {
            Color = new ClearColorValue(0.12f, 0.14f, 0.18f, 1.0f)
        };

        RenderPassBeginInfo passBegin = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = RenderPass,
            Framebuffer = Framebuffers[_imageIndex],
            RenderArea = new Rect2D(default, SwapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearColor
        };

        Vk.CmdBeginRenderPass(cmdBuffer, in passBegin, SubpassContents.Inline);
        _frameActive = true;
        return true;
    }

    /// <summary>
    /// Ends render pass and command buffer, submits to graphics queue, and presents to swapchain.
    /// </summary>
    public void EndFrameAndPresent()
    {
        if (!_frameActive) return;

        var cmdBuffer = CommandBuffers[_currentFrame];
        Vk.CmdEndRenderPass(cmdBuffer);
        Vk.EndCommandBuffer(cmdBuffer);

        var waitSem = _imageAvailableSemaphores[_currentFrame];
        var sigSem = _renderFinishedSemaphores[_currentFrame];
        PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSem,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmdBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &sigSem
        };

        Vk.QueueSubmit(GraphicsQueue, 1, in submitInfo, _inFlightFences[_currentFrame]);

        var swapchain = Swapchain;
        var imgIdx = _imageIndex;

        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &sigSem,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imgIdx
        };

        Result pres = KhrSwapchain!.QueuePresent(PresentQueue, in presentInfo);
        if (pres == Result.ErrorOutOfDateKhr || pres == Result.SuboptimalKhr)
        {
            RecreateSwapchain();
        }

        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
        _frameActive = false;
    }

    #endregion

    #region Frame Loop

    /// <summary>Blocks until the device has finished all submitted work. Does nothing before the device exists.</summary>
    public void WaitIdle()
    {
        if (Device.Handle != 0)
        {
            Vk.DeviceWaitIdle(Device);
        }
    }

    /// <summary>
    /// Waits for the device to idle and then destroys the synchronization objects, swapchain resources, and
    /// device. Safe to call once; the owning window must not be used afterward.
    /// </summary>
    public void Dispose()
    {
        WaitIdle();

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            if (_imageAvailableSemaphores[i].Handle != 0) Vk.DestroySemaphore(Device, _imageAvailableSemaphores[i], null);
            if (_renderFinishedSemaphores[i].Handle != 0) Vk.DestroySemaphore(Device, _renderFinishedSemaphores[i], null);
            if (_inFlightFences[i].Handle != 0) Vk.DestroyFence(Device, _inFlightFences[i], null);
        }

        if (CommandPool.Handle != 0) Vk.DestroyCommandPool(Device, CommandPool, null);

        for (int i = 0; i < Framebuffers.Length; i++)
        {
            if (Framebuffers[i].Handle != 0) Vk.DestroyFramebuffer(Device, Framebuffers[i], null);
        }

        if (RenderPass.Handle != 0) Vk.DestroyRenderPass(Device, RenderPass, null);

        for (int i = 0; i < SwapchainImageViews.Length; i++)
        {
            if (SwapchainImageViews[i].Handle != 0) Vk.DestroyImageView(Device, SwapchainImageViews[i], null);
        }

        if (Swapchain.Handle != 0) KhrSwapchain?.DestroySwapchain(Device, Swapchain, null);
        if (Device.Handle != 0) Vk.DestroyDevice(Device, null);
        if (Surface.Handle != 0) KhrSurface?.DestroySurface(Instance, Surface, null);
        if (Instance.Handle != 0) Vk.DestroyInstance(Instance, null);
    }

    #endregion
}
