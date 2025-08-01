namespace HelixToolkit.Nex.Graphics.Vulkan;

internal sealed class CommandBuffer(VulkanContext context) : ICommandBuffer
{
    static readonly ILogger logger = LogManager.Create<CommandBuffer>();
    readonly VulkanContext vkContext = context;
    Framebuffer framebuffer = Framebuffer.Null;
    bool isRendering = false;
    RenderPipelineHandle currentPipelineGraphics = RenderPipelineHandle.Null;
    ComputePipelineHandle currentPipelineCompute = ComputePipelineHandle.Null;
    VkPipeline lastPipelineBound = VkPipeline.Null;

    public IContext Context => vkContext;
    public readonly VulkanImmediateCommands.CommandBufferWrapper Wrapper = context.Immediate!.Acquire();
    public VkCommandBuffer CmdBuffer => Wrapper.Instance;

    public Framebuffer Framebuffer => framebuffer;

    public SubmitHandle LastSubmitHandle { set; get; } = SubmitHandle.Null;

    public VkPipeline LastPipelineBound => lastPipelineBound;

    public bool IsRendering => isRendering;

    public uint32_t ViewMask { private set; get; } = 0;

    public RenderPipelineHandle CurrentPipelineGraphics => currentPipelineGraphics;
    public ComputePipelineHandle CurrentPipelineCompute => currentPipelineCompute;

    void UseComputeTexture(TextureHandle handle, VkPipelineStageFlags2 dstStage)
    {
        HxDebug.Assert(handle);
        var tex = vkContext.TexturesPool.Get(handle);
        HxDebug.Assert(tex != null, "Texture is null. Make sure the texture is created before using it in compute shader.");
        if (tex is null || !tex.Valid)
        {
            logger.LogError($"Texture {handle} is null or invalid. Make sure the texture is created before using it in compute shader.");
            return;
        }

        // (void)dstStage; // TODO: add extra dstStage

        if (!tex.IsStorageImage && !tex.IsSampledImage)
        {
            HxDebug.Assert(false, "Did you forget to specify TextureUsageBits::Storage or TextureUsageBits::Sampled on your texture?");
            return;
        }

        tex.TransitionLayout(CmdBuffer,
                             tex.IsStorageImage ? VK.VK_IMAGE_LAYOUT_GENERAL : VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                             new VkImageSubresourceRange(tex.GetImageAspectFlags(), 0, VK.VK_REMAINING_MIP_LEVELS, 0, VK.VK_REMAINING_ARRAY_LAYERS));
    }

    public unsafe void BeginRendering(in RenderPass renderPass, in Framebuffer fb, in Dependencies deps)
    {
        HxDebug.Assert(!isRendering);

        isRendering = true;
        ViewMask = renderPass.ViewMask;

        for (uint32_t i = 0; i != Dependencies.MAX_SUBMIT_DEPENDENCIES && deps.Textures[i]; i++)
        {
            TransitionToShaderReadOnly(deps.Textures[i]);
        }
        for (uint32_t i = 0; i != Dependencies.MAX_SUBMIT_DEPENDENCIES && deps.Buffers[i]; i++)
        {
            VkPipelineStageFlags2 dstStageFlags = VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader;
            var buf = vkContext.BuffersPool.Get(deps.Buffers[i]);
            HxDebug.Assert(buf);
            if (buf!.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDEX_BUFFER_BIT) || buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT))
            {
                dstStageFlags |= VkPipelineStageFlags2.VertexInput;
            }
            if (buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT))
            {
                dstStageFlags |= VkPipelineStageFlags2.DrawIndirect;
            }
            var buffer = vkContext.BuffersPool.Get(deps.Buffers[i]);
            HxDebug.Assert(buffer, "Buffer is null. Make sure the buffer is created before binding it to the command buffer.");
            if (buffer is null || !buffer.Valid)
            {
                logger.LogError("Buffer {INDEX} is null or invalid. Make sure the buffer is created before binding it to the command buffer.", i);
                continue;
            }
            CmdBuffer.BufferBarrier2(buffer, VkPipelineStageFlags2.ComputeShader, dstStageFlags);
        }

        uint32_t numFbColorAttachments = fb.GetNumColorAttachments();
        uint32_t numPassColorAttachments = renderPass.GetNumColorAttachments();

        HxDebug.Assert(numPassColorAttachments == numFbColorAttachments);

        framebuffer = fb;

        // transition all the color attachments
        for (uint32_t i = 0; i != numFbColorAttachments; i++)
        {
            var handle = fb.Colors[i].Texture;
            if (handle)
            {
                var colorTex = vkContext.TexturesPool.Get(handle);
                HxDebug.Assert(colorTex != null && colorTex.Valid, "Colors texture is null. Make sure the texture is created before binding it to the framebuffer.");
                if (colorTex is null)
                {
                    continue;
                }
                CmdBuffer.TransitionToColorAttachment(colorTex);
            }
            // handle MSAA
            handle = fb.Colors[i].ResolveTexture;
            if (handle)
            {
                var colorResolveTex = vkContext.TexturesPool.Get(handle);
                HxDebug.Assert(colorResolveTex != null, "Colors resolve texture is null. Make sure the texture is created before binding it to the framebuffer.");
                if (colorResolveTex is null)
                {
                    logger.LogError($"Colors resolve texture {handle} is null. Make sure the texture is created before binding it to the framebuffer.");
                    continue;
                }
                colorResolveTex.IsResolveAttachment = true;
                CmdBuffer.TransitionToColorAttachment(colorResolveTex);
            }
        }

        var depthTex = fb.DepthStencil.Texture;
        {
            // transition depth-stencil attachment          
            if (depthTex)
            {
                var depthImg = vkContext.TexturesPool.Get(depthTex);
                HxDebug.Assert(depthImg != null, "Depth attachment texture is null. Make sure the texture is created before binding it to the framebuffer.");
                HxDebug.Assert(depthImg!.ImageFormat != VkFormat.Undefined, "Invalid depth attachment format");
                HxDebug.Assert(depthImg!.IsDepthFormat, "Invalid depth attachment format");
                var flags = depthImg.GetImageAspectFlags();
                depthImg.TransitionLayout(Wrapper.Instance,
                                          VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
                                          new VkImageSubresourceRange(flags, 0, VK.VK_REMAINING_MIP_LEVELS, 0, VK.VK_REMAINING_ARRAY_LAYERS));
            }
            // handle depth MSAA
            var handle = fb.DepthStencil.ResolveTexture;
            if (handle)
            {
                var depthResolveImg = vkContext.TexturesPool.Get(handle);
                HxDebug.Assert(depthResolveImg != null, "Depth resolve texture is null. Make sure the texture is created before binding it to the framebuffer.");
                HxDebug.Assert(depthResolveImg!.IsDepthFormat, "Invalid resolve depth attachment format");
                depthResolveImg.IsResolveAttachment = true;
                var flags = depthResolveImg.GetImageAspectFlags();
                depthResolveImg.TransitionLayout(Wrapper.Instance,
                                                 VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
                                                 new VkImageSubresourceRange(flags, 0, VK.VK_REMAINING_MIP_LEVELS, 0, VK.VK_REMAINING_ARRAY_LAYERS));
            }
        }


        VkSampleCountFlags samples = VkSampleCountFlags.Count1;
        uint32_t mipLevel = 0;
        uint32_t fbWidth = 0;
        uint32_t fbHeight = 0;

        var colorAttachments = stackalloc VkRenderingAttachmentInfo[Constants.MAX_COLOR_ATTACHMENTS];

        for (uint32_t i = 0; i < numFbColorAttachments; i++)
        {
            ref var attachment = ref fb.Colors[i];
            HxDebug.Assert(!attachment.Texture.Empty);

            var colorTexture = vkContext.TexturesPool.Get(attachment.Texture);
            HxDebug.Assert(colorTexture != null, "Colors texture is null. Make sure the texture is created before binding it to the framebuffer.");
            if (colorTexture is null)
            {
                logger.LogError("Colors texture {HANDLE} is null. Make sure the texture is created before binding it to the framebuffer.", attachment.Texture);
                continue; // skip if texture is not found
            }
            ref var descColor = ref renderPass.Colors[i];
            if (mipLevel > 0 && descColor.Level > 0)
            {
                HxDebug.Assert(descColor.Level == mipLevel, "All color attachments should have the same mip-level");
            }
            var dim = colorTexture.Extent;
            if (fbWidth > 0)
            {
                HxDebug.Assert(dim.width == fbWidth, "All attachments should have the same width");
            }
            if (fbHeight > 0)
            {
                HxDebug.Assert(dim.height == fbHeight, "All attachments should have the same height");
            }
            mipLevel = descColor.Level;
            fbWidth = dim.width;
            fbHeight = dim.height;
            samples = colorTexture.SampleCount;
            colorAttachments[i] = new()
            {
                pNext = null,
                imageView = colorTexture.GetOrCreateVkImageViewForFramebuffer(vkContext, descColor.Level, descColor.Layer),
                imageLayout = VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                resolveMode = samples > VkSampleCountFlags.Count1 ? descColor.ResolveMode.ResolveModeToVkResolveModeFlagBits(VkResolveModeFlags.Max)
                                             : VkResolveModeFlags.None,
                resolveImageView = VkImageView.Null,
                resolveImageLayout = VkImageLayout.Undefined,
                loadOp = descColor.LoadOp.ToVk(),
                storeOp = descColor.StoreOp.ToVk(),
            };

            colorAttachments[i].clearValue.color = descColor.ClearColor.ToVk();
            // handle MSAA
            if (descColor.StoreOp == StoreOp.MsaaResolve)
            {
                HxDebug.Assert(samples != VkSampleCountFlags.None);
                HxDebug.Assert(!attachment.ResolveTexture.Empty, "Framebuffer attachment should contain a resolve texture");
                var colorResolveTexture = vkContext.TexturesPool.Get(attachment.ResolveTexture);
                HxDebug.Assert(colorResolveTexture != null, "Colors resolve texture is null. Make sure the texture is created before binding it to the framebuffer.");
                if (colorResolveTexture is null)
                {
                    logger.LogError("Colors resolve texture {TEXTURE} is null. Make sure the texture is created before binding it to the framebuffer.", attachment.ResolveTexture);
                    continue; // skip if texture is not found
                }
                colorAttachments[i].resolveImageView =
                    colorResolveTexture.GetOrCreateVkImageViewForFramebuffer(vkContext, descColor.Level, descColor.Layer);
                colorAttachments[i].resolveImageLayout = VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
            }
        }

        VkRenderingAttachmentInfo depthAttachment = new();

        if (fb.DepthStencil.Texture)
        {
            var depthTexture = vkContext.TexturesPool.Get(fb.DepthStencil.Texture);
            HxDebug.Assert(depthTexture != null, "Depth attachment texture is null. Make sure the texture is created before binding it to the framebuffer.");
            if (depthTexture is null)
            {
                logger.LogError("Depth attachment texture {TEXTURE} is null. Make sure the texture is created before binding it to the framebuffer.", fb.DepthStencil.Texture);
                return; // skip if texture is not found
            }
            ref readonly var descDepth = ref renderPass.Depth;
            HxDebug.Assert(descDepth.Level == mipLevel, "Depth attachment should have the same mip-level as color attachments");
            depthAttachment = new()
            {
                imageView = depthTexture.GetOrCreateVkImageViewForFramebuffer(vkContext, descDepth.Level, descDepth.Layer),
                imageLayout = VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
                resolveMode = VK.VK_RESOLVE_MODE_NONE,
                resolveImageView = VkImageView.Null,
                resolveImageLayout = VK.VK_IMAGE_LAYOUT_UNDEFINED,
                loadOp = descDepth.LoadOp.ToVk(),
                storeOp = descDepth.StoreOp.ToVk(),
                clearValue = new() { depthStencil = new(descDepth.ClearDepth, descDepth.ClearStencil) },
            };
            // handle depth MSAA
            if (descDepth.StoreOp == StoreOp.MsaaResolve)
            {
                HxDebug.Assert(depthTexture.SampleCount == samples);
                ref readonly var attachment = ref fb.DepthStencil;
                HxDebug.Assert(!attachment.ResolveTexture.Empty, "Framebuffer depth attachment should contain a resolve texture");
                var depthResolveTexture = vkContext.TexturesPool.Get(attachment.ResolveTexture);
                HxDebug.Assert(depthResolveTexture != null, "Depth resolve texture is null. Make sure the texture is created before binding it to the framebuffer.");
                if (depthResolveTexture is null)
                {
                    logger.LogError("Depth resolve texture {TEXTURE} is null. Make sure the texture is created before binding it to the framebuffer.", attachment.ResolveTexture);
                    return; // skip if texture is not found
                }
                depthAttachment.resolveImageView = depthResolveTexture.GetOrCreateVkImageViewForFramebuffer(vkContext, descDepth.Level, descDepth.Layer);
                depthAttachment.resolveImageLayout = VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
                depthAttachment.resolveMode = descDepth.ResolveMode.ResolveModeToVkResolveModeFlagBits(vkContext.VkPhysicalDeviceVulkan12Properties.supportedDepthResolveModes);
            }
            var dim = depthTexture.Extent;

            HxDebug.Assert(fbWidth > 0 && dim.width == fbWidth, "All attachments should have the same width");
            HxDebug.Assert(fbHeight > 0 && dim.height == fbHeight, "All attachments should have the same height");

            mipLevel = descDepth.Level;
            fbWidth = dim.width;
            fbHeight = dim.height;
        }

        uint32_t width = Math.Max(fbWidth >> (int)mipLevel, 1u);
        uint32_t height = Math.Max(fbHeight >> (int)mipLevel, 1u);
        var viewport = new ViewportF(0.0f, 0.0f, (float)width, (float)height, 0.0f, +1.0f);
        var scissor = new ScissorRect(0, 0, width, height);

        var stencilAttachment = depthAttachment;

        bool isStencilFormat = renderPass.Stencil.LoadOp != LoadOp.Invalid;
        unsafe
        {
            VkRenderingInfo renderingInfo = new()
            {
                renderArea = new VkRect2D(new VkOffset2D((int32_t)scissor.X, (int32_t)scissor.Y), new VkExtent2D((int32_t)scissor.W, (int32_t)scissor.H)),
                layerCount = renderPass.LayerCount,
                viewMask = renderPass.ViewMask,
                colorAttachmentCount = numFbColorAttachments,
                pColorAttachments = colorAttachments,
                pDepthAttachment = depthTex ? &depthAttachment : null,
                pStencilAttachment = isStencilFormat ? &stencilAttachment : null,
            };

            BindViewport(viewport);
            BindScissorRect(scissor);
            BindDepthState(new DepthState());

            vkContext.CheckAndUpdateDescriptorSets();

            VK.vkCmdSetDepthCompareOp(Wrapper.Instance, VkCompareOp.Always);
            VK.vkCmdSetDepthBiasEnable(Wrapper.Instance, VK_BOOL.False);

            VK.vkCmdBeginRendering(Wrapper.Instance, &renderingInfo);
        }
    }

    public void BindComputePipeline(ComputePipelineHandle handle)
    {
        if (handle.Empty)
        {
            logger.LogError("Cannot bind empty compute pipeline handle.");
            return;
        }

        currentPipelineGraphics = RenderPipelineHandle.Null;
        currentPipelineCompute = handle;

        var pipeline = vkContext.GetVkPipeline(handle);

        var cps = vkContext.ComputePipelinesPool.Get(handle);

        HxDebug.Assert(cps);
        HxDebug.Assert(pipeline != VkPipeline.Null);

        if (lastPipelineBound != pipeline)
        {
            lastPipelineBound = pipeline;
            VK.vkCmdBindPipeline(Wrapper.Instance, VK.VK_PIPELINE_BIND_POINT_COMPUTE, pipeline);
            vkContext.CheckAndUpdateDescriptorSets();
            vkContext.BindDefaultDescriptorSets(Wrapper.Instance, VK.VK_PIPELINE_BIND_POINT_COMPUTE, cps!.PipelineLayout);
        }
    }

    public void BindDepthState(in DepthState desc)
    {
        var op = desc.CompareOp.ToVk();
        VK.vkCmdSetDepthWriteEnable(Wrapper.Instance, desc.IsDepthWriteEnabled ? VK_BOOL.True : VK_BOOL.False);
        VK.vkCmdSetDepthTestEnable(Wrapper.Instance, (op != VK.VK_COMPARE_OP_ALWAYS || desc.IsDepthWriteEnabled) ? VK_BOOL.True : VK_BOOL.False);

#if ANDROID
      // This is a workaround for the issue.
      // On Android (Mali-G715-Immortalis MC11 v1.r38p1-01eac0.c1a71ccca2acf211eb87c5db5322f569)
      // if depth-stencil texture is not set, call of vkCmdSetDepthCompareOp leads to disappearing of all content.
      if (!framebuffer_.depthStencil.texture) {
          return;
      }
#endif
        VK.vkCmdSetDepthCompareOp(Wrapper.Instance, op);
    }

    public void BindIndexBuffer(in BufferHandle indexBuffer, IndexFormat indexFormat, size_t indexBufferOffset)
    {
        if (!indexBuffer)
        {
            logger.LogError("Bind index buffer failed. Handle is not valid.");
            return;
        }
        var buf = vkContext.BuffersPool.Get(indexBuffer);

        HxDebug.Assert(buf is not null && buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDEX_BUFFER_BIT));

        var type = indexFormat.ToVk();
        VK.vkCmdBindIndexBuffer(Wrapper.Instance, buf!.VkBuffer, indexBufferOffset, type);
    }

    public void BindRenderPipeline(in RenderPipelineHandle handle)
    {
        if (!handle)
        {
            logger.LogError("Bind render pipeline failed. Handle is not valid.");
            return;
        }

        currentPipelineGraphics = handle;
        currentPipelineCompute = ComputePipelineHandle.Null;

        var rps = vkContext.RenderPipelinesPool.Get(handle);

        HxDebug.Assert(rps != null);

        bool hasDepthAttachmentPipeline = rps!.Desc.DepthFormat != Format.Invalid;
        bool hasDepthAttachmentPass = framebuffer.DepthStencil.Texture.Valid;

        if (hasDepthAttachmentPipeline != hasDepthAttachmentPass)
        {
            HxDebug.Assert(false);
            logger.LogError("Make sure your render pass and render pipeline both have matching depth attachments");
        }

        var pipeline = vkContext.GetVkPipeline(handle, ViewMask);

        HxDebug.Assert(pipeline.IsNotNull);

        if (lastPipelineBound != pipeline)
        {
            lastPipelineBound = pipeline;
            VK.vkCmdBindPipeline(Wrapper.Instance, VK.VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline);
            vkContext.BindDefaultDescriptorSets(Wrapper.Instance, VK.VK_PIPELINE_BIND_POINT_GRAPHICS, rps.PipelineLayout);
        }
    }

    public void BindScissorRect(in ScissorRect rect)
    {
        VkRect2D scissor = new(new VkOffset2D((int32_t)rect.X, (int32_t)rect.Y), new VkExtent2D(rect.W, rect.H));
        unsafe
        {
            VK.vkCmdSetScissor(Wrapper.Instance, 0, 1, &scissor);
        }
    }

    public void BindVertexBuffer(uint index, in BufferHandle buffer, size_t bufferOffset)
    {
        if (buffer.Empty)
        {
            return;
        }

        var buf = vkContext.BuffersPool.Get(buffer);
        HxDebug.Assert(buf, "Vertex buffer is null. Make sure the buffer is created before binding it.");
        if (buf is null || !buf.Valid)
        {
            logger.LogError("Bind vertex buffer failed. Buffer handle is not valid.");
            return;
        }
        HxDebug.Assert(buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT));
        unsafe
        {
            var vkBuffer = buf.VkBuffer;
            ulong offset = bufferOffset; // Vulkan uses ulong for offsets
            VK.vkCmdBindVertexBuffers2(CmdBuffer, index, 1, &vkBuffer, &offset, null, null);
        }
    }

    public void BindViewport(in ViewportF viewport)
    {
        // https://www.saschawillems.de/blog/2019/03/29/flipping-the-vulkan-viewport/
        VkViewport vp = new()
        {
            x = viewport.X, // float x;
            y = viewport.Height - viewport.Y, // float y;
            width = viewport.Width, // float width;
            height = -viewport.Height, // float height;
            minDepth = viewport.MinDepth, // float minDepth;
            maxDepth = viewport.MaxDepth, // float maxDepth;
        };
        unsafe
        {
            VK.vkCmdSetViewport(Wrapper.Instance, 0, 1, &vp);
        }
    }

    public void ClearColorImage(in TextureHandle tex, in Color4 value, in TextureLayers layers)
    {
        unsafe
        {
            HxDebug.Assert(cond: Unsafe.SizeOf<Color4>() == Unsafe.SizeOf<VkClearColorValue>());
        }


        var img = vkContext.TexturesPool.Get(tex);

        if (img is null || !img.Valid)
        {
            return;
        }

        VkImageSubresourceRange range = new()
        {
            aspectMask = img.GetImageAspectFlags(),
            baseMipLevel = layers.mipLevel,
            levelCount = VK.VK_REMAINING_MIP_LEVELS,
            baseArrayLayer = layers.layer,
            layerCount = layers.numLayers,
        };

        CmdBuffer.ImageMemoryBarrier2(img.Image,
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.AllCommands,
                access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
            },
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.Transfer,
                access = VkAccessFlags2.TransferWrite
            },
            img.ImageLayout, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, range);

        var vkClearValue = value.ToVk();
        unsafe
        {
            VK.vkCmdClearColorImage(Wrapper.Instance, img.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &vkClearValue, 1, &range);
        }

        // a ternary cascade...
        VkImageLayout newLayout = img.ImageLayout == VK.VK_IMAGE_LAYOUT_UNDEFINED ?
            (img.IsAttachment ? VK.VK_IMAGE_LAYOUT_ATTACHMENT_OPTIMAL : img.IsSampledImage ? VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK.VK_IMAGE_LAYOUT_GENERAL) : img.ImageLayout;

        CmdBuffer.ImageMemoryBarrier2(img.Image,
            new StageAccess2()
            {
                stage = VkPipelineStageFlags2.Transfer,
                access = VkAccessFlags2.TransferWrite
            },
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.AllCommands,
                access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
            },
            VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, newLayout, range);

        img.ImageLayout = newLayout;
    }

    public void CopyImage(in TextureHandle src, in TextureHandle dst, in Dimensions extent, in Offset3D srcOffset, in Offset3D dstOffset, in TextureLayers srcLayers, in TextureLayers dstLayers)
    {
        var imgSrc = vkContext.TexturesPool.Get(src);
        var imgDst = vkContext.TexturesPool.Get(dst);

        HxDebug.Assert(imgSrc is not null && imgDst is not null);
        HxDebug.Assert(srcLayers.numLayers == dstLayers.numLayers);

        if (!imgSrc!.Valid || !imgDst!.Valid)
        {
            logger.LogError("Cannot copy image. Source or destination image is not valid.");
            return;
        }

        VkImageSubresourceRange rangeSrc = new()
        {
            aspectMask = imgSrc.GetImageAspectFlags(),
            baseMipLevel = srcLayers.mipLevel,
            levelCount = 1,
            baseArrayLayer = srcLayers.layer,
            layerCount = srcLayers.numLayers,
        };
        VkImageSubresourceRange rangeDst = new()
        {
            aspectMask = imgDst.GetImageAspectFlags(),
            baseMipLevel = dstLayers.mipLevel,
            levelCount = 1,
            baseArrayLayer = dstLayers.layer,
            layerCount = dstLayers.numLayers,
        };

        HxDebug.Assert(imgSrc.ImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);

        VkExtent3D dstExtent = imgDst.Extent;
        bool coversFullDstImage = dstExtent.width == extent.Width && dstExtent.height == extent.Height && dstExtent.depth == extent.Depth &&
                                        dstOffset.x == 0 && dstOffset.y == 0 && dstOffset.z == 0;

        HxDebug.Assert(coversFullDstImage || imgDst.ImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);

        CmdBuffer.ImageMemoryBarrier2(imgSrc.Image,
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.AllCommands,
                access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
            },
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.Transfer,
                access = VkAccessFlags2.TransferRead
            },
            imgSrc.ImageLayout,
            VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            rangeSrc);
        CmdBuffer.ImageMemoryBarrier2(imgDst.Image,
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.AllCommands,
                access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite
            },
            new StageAccess2
            {
                stage = VkPipelineStageFlags2.Transfer,
                access = VkAccessFlags2.TransferWrite
            },
            coversFullDstImage ? VK.VK_IMAGE_LAYOUT_UNDEFINED : imgDst.ImageLayout,
            VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            rangeDst);

        VkImageCopy regionCopy = new()
        {
            srcSubresource = new()
            {
                aspectMask = imgSrc.GetImageAspectFlags(),
                mipLevel = srcLayers.mipLevel,
                baseArrayLayer = srcLayers.layer,
                layerCount = srcLayers.numLayers,
            },
            srcOffset = new() { x = srcOffset.x, y = srcOffset.y, z = srcOffset.z },
            dstSubresource = new()
            {
                aspectMask = imgDst.GetImageAspectFlags(),
                mipLevel = dstLayers.mipLevel,
                baseArrayLayer = dstLayers.layer,
                layerCount = dstLayers.numLayers,
            },
            dstOffset = new() { x = dstOffset.x, y = dstOffset.y, z = dstOffset.z },
            extent = new() { width = extent.Width, height = extent.Height, depth = extent.Depth },
        };
        VkImageBlit regionBlit = new()
        {
            srcSubresource = regionCopy.srcSubresource,
            dstSubresource = regionCopy.dstSubresource
        };
        regionBlit.srcOffsets[0] = new VkOffset3D(srcOffset.x, srcOffset.y, srcOffset.z);
        regionBlit.srcOffsets[1] = new VkOffset3D((int)(srcOffset.x + extent.Width), (int)(srcOffset.y + extent.Height), (int)(srcOffset.z + extent.Depth));
        regionBlit.dstOffsets[0] = new VkOffset3D(dstOffset.x, dstOffset.y, dstOffset.z);
        regionBlit.dstOffsets[1] = new VkOffset3D((int)(dstOffset.x + extent.Width), (int)(dstOffset.y + extent.Height), (int)(dstOffset.z + extent.Depth));
        bool isCompatible = imgSrc.ImageFormat.GetBytesPerPixel() == imgDst.ImageFormat.GetBytesPerPixel();

        if (isCompatible)
        {
            unsafe
            {
                VK.vkCmdCopyImage(Wrapper.Instance, imgSrc.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                                  imgDst.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                  1, &regionCopy);
            }

        }
        else
        {
            unsafe
            {
                VK.vkCmdBlitImage(Wrapper.Instance, imgSrc.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                                  imgDst.Image, VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                  1, &regionBlit, VK.VK_FILTER_LINEAR);
            }
        }
        CmdBuffer.ImageMemoryBarrier2(imgSrc.Image,
             new StageAccess2()
             { stage = VkPipelineStageFlags2.Transfer, access = VkAccessFlags2.TransferRead },
             new StageAccess2()
             { stage = VkPipelineStageFlags2.AllCommands, access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite },
             VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
             imgSrc.ImageLayout, rangeSrc);

        // a ternary cascade...
        VkImageLayout newLayout = imgDst.ImageLayout == VK.VK_IMAGE_LAYOUT_UNDEFINED
                                            ? (imgDst.IsAttachment ? VK.VK_IMAGE_LAYOUT_ATTACHMENT_OPTIMAL
                                               : imgDst.IsSampledImage ? VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                                                                          : VK.VK_IMAGE_LAYOUT_GENERAL)
                                            : imgDst.ImageLayout;

        CmdBuffer.ImageMemoryBarrier2(imgDst.Image,
            new StageAccess2()
            { stage = VkPipelineStageFlags2.Transfer, access = VkAccessFlags2.TransferWrite },
            new StageAccess2()
            { stage = VkPipelineStageFlags2.AllCommands, access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite },
            VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            newLayout, rangeDst);

        imgDst.ImageLayout = newLayout;
    }

    public void DispatchThreadGroups(in Dimensions threadgroupCount, in Dependencies deps)
    {
        HxDebug.Assert(!isRendering);

        for (uint32_t i = 0; i != Dependencies.MAX_SUBMIT_DEPENDENCIES && deps.Textures[i]; i++)
        {
            UseComputeTexture(deps.Textures[i], VkPipelineStageFlags2.ComputeShader);
        }
        for (uint32_t i = 0; i != Dependencies.MAX_SUBMIT_DEPENDENCIES && deps.Buffers[i]; i++)
        {
            var buf = vkContext.BuffersPool.Get(deps.Buffers[i]);
            HxDebug.Assert(buf && buf!.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT),
                           "Did you forget to specify BufferUsageBits_Storage on your buffer?");
            if (buf is null || !buf.Valid)
            {
                logger.LogError("Buffer {INDEX} is null or invalid. Make sure the buffer is created before binding it to the command buffer.", i);
                continue;
            }
            CmdBuffer.BufferBarrier2(buf,
                      VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.FragmentShader,
                      VkPipelineStageFlags2.ComputeShader);
        }

        VK.vkCmdDispatch(CmdBuffer, threadgroupCount.Width, threadgroupCount.Height, threadgroupCount.Depth);
    }

    public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint baseInstance)
    {
        if (vertexCount == 0)
        {
            logger.LogWarning("Unable to Draw. Vertex count is zero.");
            return;
        }

        VK.vkCmdDraw(CmdBuffer, vertexCount, instanceCount, firstVertex, baseInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint baseInstance)
    {
        if (indexCount == 0)
        {
            logger.LogWarning("Unable to DrawIndexed. IndexCount is zero.");
            return;
        }

        VK.vkCmdDrawIndexed(CmdBuffer, indexCount, instanceCount, firstIndex, vertexOffset, baseInstance);
    }

    public void DrawIndexedIndirect(in BufferHandle indirectBuffer, uint indirectBufferOffset, uint drawCount, uint stride)
    {
        var bufIndirect = vkContext.BuffersPool.Get(indirectBuffer);

        HxDebug.Assert(bufIndirect);
        unsafe
        {
            VK.vkCmdDrawIndexedIndirect(
                CmdBuffer, bufIndirect!.VkBuffer, indirectBufferOffset, drawCount, stride > 0 ? stride : (uint)sizeof(VkDrawIndexedIndirectCommand));
        }
    }

    public void DrawIndexedIndirectCount(BufferHandle indirectBuffer, uint indirectBufferOffset, BufferHandle countBuffer, uint countBufferOffset, uint maxDrawCount, uint stride)
    {
        var bufIndirect = vkContext.BuffersPool.Get(indirectBuffer);
        var bufCount = vkContext.BuffersPool.Get(countBuffer);

        HxDebug.Assert(bufIndirect);
        HxDebug.Assert(bufCount);
        unsafe
        {
            VK.vkCmdDrawIndexedIndirectCount(CmdBuffer,
                                          bufIndirect!.VkBuffer,
                                          indirectBufferOffset,
                                          bufCount!.VkBuffer,
                                          countBufferOffset,
                                          maxDrawCount,
                                          stride > 0 ? stride : (uint)sizeof(VkDrawIndexedIndirectCommand));
        }
    }

    public void DrawIndirect(in BufferHandle indirectBuffer, uint indirectBufferOffset, uint drawCount, uint stride)
    {
        var bufIndirect = vkContext.BuffersPool.Get(indirectBuffer);

        HxDebug.Assert(bufIndirect);
        unsafe
        {
            VK.vkCmdDrawIndirect(
                CmdBuffer, bufIndirect!.VkBuffer, indirectBufferOffset, drawCount, stride > 0 ? stride : (uint)sizeof(VkDrawIndirectCommand));
        }
    }

    public void DrawMeshTasks(in Dimensions threadgroupCount)
    {
        VK.vkCmdDrawMeshTasksEXT(CmdBuffer, threadgroupCount.Width, threadgroupCount.Height, threadgroupCount.Depth);
    }

    public void DrawMeshTasksIndirect(in BufferHandle indirectBuffer, uint indirectBufferOffset, uint drawCount, uint stride)
    {
        var bufIndirect = vkContext.BuffersPool.Get(indirectBuffer);

        HxDebug.Assert(bufIndirect);
        unsafe
        {
            VK.vkCmdDrawMeshTasksIndirectEXT(CmdBuffer,
                                          bufIndirect!.VkBuffer,
                                          indirectBufferOffset,
                                          drawCount,
                                          stride > 0 ? stride : (uint)sizeof(VkDrawMeshTasksIndirectCommandEXT));
        }
    }

    public void DrawMeshTasksIndirectCount(in BufferHandle indirectBuffer, uint indirectBufferOffset, in BufferHandle countBuffer, uint countBufferOffset, uint maxDrawCount, uint stride)
    {
        var bufIndirect = vkContext.BuffersPool.Get(indirectBuffer);
        var bufCount = vkContext.BuffersPool.Get(countBuffer);

        HxDebug.Assert(bufIndirect);
        HxDebug.Assert(bufCount);
        unsafe
        {
            VK.vkCmdDrawMeshTasksIndirectCountEXT(CmdBuffer,
                                               bufIndirect!.VkBuffer,
                                               indirectBufferOffset,
                                               bufCount!.VkBuffer,
                                               countBufferOffset,
                                               maxDrawCount,
                                               stride > 0 ? stride : (uint)sizeof(VkDrawMeshTasksIndirectCommandEXT));
        }
    }

    public void EndRendering()
    {
        isRendering = false;

        VK.vkCmdEndRendering(CmdBuffer);

        framebuffer = Framebuffer.Null;
    }

    public void FillBuffer(in BufferHandle buffer, size_t bufferOffset, size_t size, size_t data)
    {
        HxDebug.Assert(buffer);
        HxDebug.Assert(size > 0);
        HxDebug.Assert(size % 4 == 0);
        HxDebug.Assert(bufferOffset % 4 == 0);

        var buf = vkContext.BuffersPool.Get(buffer);

        HxDebug.Assert(buf);

        if (buf is null || !buf.Valid)
        {
            logger.LogError("FillBuffer failed. Buffer handle is not valid.");
            return;
        }

        CmdBuffer.BufferBarrier2(buf, VkPipelineStageFlags2.AllCommands, VkPipelineStageFlags2.Transfer);

        VK.vkCmdFillBuffer(CmdBuffer, buf!.VkBuffer, bufferOffset, size, data);

        var dstStage = VkPipelineStageFlags2.VertexShader;

        if (buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT))
        {
            dstStage |= VkPipelineStageFlags2.DrawIndirect;
        }
        if (buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT))
        {
            dstStage |= VkPipelineStageFlags2.VertexInput;
        }

        CmdBuffer.BufferBarrier2(buf, VkPipelineStageFlags2.Transfer, dstStage);
    }

    public void GenerateMipmap(in TextureHandle handle)
    {
        if (handle.Empty)
        {
            return;
        }

        var tex = vkContext.TexturesPool.Get(handle);
        HxDebug.Assert(tex is not null && tex.Valid, "Texture is null or not valid.");
        if (tex is null || !tex.Valid || tex.IsSwapchainImage)
        {
            logger.LogError("Cannot generate mipmap for swapchain image or invalid texture.");
            return;
        }
        if (tex.NumLevels <= 1)
        {
            return;
        }

        HxDebug.Assert(tex.ImageLayout != VK.VK_IMAGE_LAYOUT_UNDEFINED);

        tex.GenerateMipmap(CmdBuffer);
    }

    public void InsertDebugEventLabel(string label, in Color4 color)
    {
        HxDebug.Assert(!string.IsNullOrEmpty(label));

        if (string.IsNullOrEmpty(label))
        {
            return;
        }
        unsafe
        {
            VkDebugUtilsLabelEXT utilsLabel = new()
            {
                pNext = null,
                pLabelName = label.ToVkUtf8ReadOnlyString()
            };
            color.CopyTo(utilsLabel.color, 4);
            VK.vkCmdInsertDebugUtilsLabelEXT(CmdBuffer, &utilsLabel);
        }
    }

    public void PopDebugGroupLabel()
    {
        VK.vkCmdEndDebugUtilsLabelEXT(CmdBuffer);
    }

    public void PushConstants(nint data, uint size, uint offset)
    {
        HxDebug.Assert(size % 4 == 0); // VUID-vkCmdPushConstants-size-00369: size must be a multiple of 4

        // check push constant size is within max size
        ref readonly var limits = ref vkContext.GetVkPhysicalDeviceProperties().limits;
        if (!(size + offset <= limits.maxPushConstantsSize))
        {
            logger.LogWarning("Push constants size exceeded {SIZE} (max {MAX} bytes)", size + offset, limits.maxPushConstantsSize);
        }

        if (currentPipelineGraphics.Empty && currentPipelineCompute.Empty)
        {
            logger.LogError("No pipeline bound - cannot set push constants");
            return;
        }

        var stateGraphics = currentPipelineGraphics.Empty ? RenderPipelineState.Null : vkContext.RenderPipelinesPool.Get(currentPipelineGraphics);
        var stateCompute = currentPipelineCompute.Empty ? ComputePipelineState.Null : vkContext.ComputePipelinesPool.Get(currentPipelineCompute);

        HxDebug.Assert(stateGraphics || stateCompute);

        VkPipelineLayout layout = stateGraphics ? stateGraphics!.PipelineLayout : stateCompute!.PipelineLayout;
        VkShaderStageFlags shaderStageFlags = stateGraphics ? stateGraphics!.ShaderStageFlags : VK.VK_SHADER_STAGE_COMPUTE_BIT;
        unsafe
        {
            VK.vkCmdPushConstants(CmdBuffer, layout, shaderStageFlags, offset, size, (void*)data);
        }
    }

    public void PushDebugGroupLabel(string label, in Color4 color)
    {
        HxDebug.Assert(!string.IsNullOrEmpty(label));

        if (string.IsNullOrEmpty(label))
        {
            return;
        }
        unsafe
        {
            VkDebugUtilsLabelEXT utilsLabel = new()
            {
                pNext = null,
                pLabelName = label.ToVkUtf8ReadOnlyString()
            };
            color.CopyTo(utilsLabel.color, 4);
            VK.vkCmdBeginDebugUtilsLabelEXT(CmdBuffer, &utilsLabel);
        }
    }

    public void ResetQueryPool(in QueryPoolHandle pool, uint firstQuery, uint queryCount)
    {
        var vkPool = vkContext.QueriesPool.Get(pool);
        HxDebug.Assert(vkPool.IsNotNull, "Query pool is null or not valid.");
        VK.vkCmdResetQueryPool(CmdBuffer, vkPool, firstQuery, queryCount);
    }

    public void SetBlendColor(in Color4 color)
    {
        VK.vkCmdSetBlendConstants(CmdBuffer, color);
    }

    public void SetDepthBias(float constantFactor, float slopeFactor, float clamp)
    {
        VK.vkCmdSetDepthBias(CmdBuffer, constantFactor, clamp, slopeFactor);
    }

    public void SetDepthBiasEnable(bool enable)
    {
        VK.vkCmdSetDepthBiasEnable(CmdBuffer, enable ? VK_BOOL.True : VK_BOOL.False);
    }

    public void TransitionToShaderReadOnly(TextureHandle handle)
    {
        var img = vkContext.TexturesPool.Get(handle);

        HxDebug.Assert(img is not null && !img.IsSwapchainImage);
        if (img is null || img.IsSwapchainImage)
        {
            logger.LogError("Cannot transition swapchain image to shader read only layout.");
            return;
        }

        // transition only non-multisampled images - MSAA images cannot be accessed from shaders
        if (img.SampleCount.HasFlag(VK.VK_SAMPLE_COUNT_1_BIT))
        {
            VkImageAspectFlags flags = img.GetImageAspectFlags();
            // set the result of the previous render pass
            img.TransitionLayout(Wrapper.Instance,
                                 img.IsSampledImage ? VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL : VK.VK_IMAGE_LAYOUT_GENERAL,
                                 new VkImageSubresourceRange(flags, 0, VK.VK_REMAINING_MIP_LEVELS, 0, VK.VK_REMAINING_ARRAY_LAYERS));
        }
    }

    public void UpdateBuffer(in BufferHandle buffer, uint bufferOffset, uint size, nint data)
    {
        HxDebug.Assert(buffer);
        HxDebug.Assert(data != IntPtr.Zero);
        HxDebug.Assert(size > 0 && size <= 65536);
        HxDebug.Assert(size % 4 == 0);
        HxDebug.Assert(bufferOffset % 4 == 0);

        var buf = vkContext.BuffersPool.Get(buffer);
        HxDebug.Assert(buf);

        if (buf is null || !buf.Valid)
        {
            logger.LogError("UpdateBuffer failed. Buffer handle is not valid.");
            return;
        }

        CmdBuffer.BufferBarrier2(buf, VkPipelineStageFlags2.AllCommands, VkPipelineStageFlags2.Transfer);
        unsafe
        {
            VK.vkCmdUpdateBuffer(CmdBuffer, buf!.VkBuffer, bufferOffset, size, (void*)data);
        }

        var dstStage = VkPipelineStageFlags2.VertexShader;

        if (buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT))
        {
            dstStage |= VkPipelineStageFlags2.DrawIndirect;
        }
        if (buf.vkUsageFlags_.HasFlag(VK.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT))
        {
            dstStage |= VkPipelineStageFlags2.VertexInput;
        }

        CmdBuffer.BufferBarrier2(buf, VkPipelineStageFlags2.Transfer, dstStage);
    }

    public void WriteTimestamp(in QueryPoolHandle pool, uint query)
    {
        var vkPool = vkContext.QueriesPool.Get(pool);
        HxDebug.Assert(vkPool.IsNotNull, "Query pool is null or not valid.");
        VK.vkCmdWriteTimestamp(CmdBuffer, VK.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, vkPool, query);
    }
}
