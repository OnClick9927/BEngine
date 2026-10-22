using Silk.NET.OpenGL;

namespace BEngine.Rendering;

public sealed class SceneFramebuffer : IDisposable
{
    private readonly GL _gl;
    private uint _framebuffer;
    private uint _depthBuffer;

    public uint ColorTexture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public SceneFramebuffer(GL gl, int width, int height)
    {
        _gl = gl;
        Resize(width, height);
    }

    public unsafe void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (Width == width && Height == height)
        {
            return;
        }

        Release();
        Width = width;
        Height = height;
        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        ColorTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, ColorTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)Width, (uint)Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ColorTexture, 0);

        _depthBuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8,
            (uint)Width, (uint)Height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _depthBuffer);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("Could not create the editor scene framebuffer.");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
    }

    public void Unbind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        if (_depthBuffer != 0) _gl.DeleteRenderbuffer(_depthBuffer);
        if (ColorTexture != 0) _gl.DeleteTexture(ColorTexture);
        if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
        _depthBuffer = 0;
        ColorTexture = 0;
        _framebuffer = 0;
    }
}
