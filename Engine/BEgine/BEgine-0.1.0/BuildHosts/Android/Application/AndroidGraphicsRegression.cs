using System;
using System.Numerics;
using Android.Opengl;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Player.Hosts.Android;

/// <summary>Opt-in native pixel regression, enabled by the bengineValidateGraphics intent extra.</summary>
internal static class AndroidGraphicsRegression
{
    internal static bool TryRunPostProcessing(AndroidGlGraphicsDevice device)
    {
        // Package resources become available after the AOT loader registers AB
        // content. Exercise the actual shipped shaders, not a copy of their UVs.
        var vertex = BEngine.Resources.Load<string>("Shaders/PostProcessing/PostProcess.opengl.vert.glsl");
        var fragment = BEngine.Resources.Load<string>("Shaders/PostProcessing/PostProcess.opengl.frag.glsl");
        if (vertex is null || fragment is null) return false;
        using var source = device.CreateRenderTarget("Android post-process source", new(4,4,GraphicsTextureFormat.Rgba8Unorm,null,SampleColor:true));
        using (device.PushRenderTarget(source))
        {
            device.SetViewport(new(0,0,4,4));
            device.SetScissor(new(0,0,4,2)); device.Clear(GraphicsClearFlags.Color,new(1,0,0,1));
            device.SetScissor(new(0,2,4,2)); device.Clear(GraphicsClearFlags.Color,new(0,0,1,1));
            device.SetScissor(null);
        }
        using var target = device.CreateRenderTarget("Android post-process result", new(4,4,GraphicsTextureFormat.Rgba8Unorm,null));
        using var targetScope = device.PushRenderTarget(target);
        using var program = device.CreateProgram(new("Android post-process orientation",
            new(GraphicsShaderStage.Vertex,GraphicsShaderLanguage.Glsl,vertex),
            new(GraphicsShaderStage.Fragment,GraphicsShaderLanguage.Glsl,fragment)));
        var vertices = new System.Collections.Generic.List<float>();
        foreach (var point in new[]{new System.Numerics.Vector4(0,0,0,0),new System.Numerics.Vector4(4,0,1,0),new System.Numerics.Vector4(4,4,1,1),new System.Numerics.Vector4(0,0,0,0),new System.Numerics.Vector4(4,4,1,1),new System.Numerics.Vector4(0,4,0,1)})
        {
            vertices.AddRange(new[]{point.X,point.Y,point.Z,point.W, 0,0,0,0, 1,1,1,1,
                0,.001f,1,0, 0,0,0,0, 0,1,.5f,0, .25f,.25f,1,0});
        }
        var attributes = new GraphicsVertexAttribute[8];
        attributes[0]=new(0,2,0); attributes[1]=new(1,2,8);
        for (int i=2;i<8;i++) attributes[i]=new(i,4,(4+(i-2)*4)*sizeof(float));
        using var mesh = device.CreateMesh(new("Android post-process orientation",vertices.ToArray(),new(28*sizeof(float),attributes)));
        device.SetViewport(new(0,0,4,4)); device.SetScissor(null);
        device.SetDepthState(GraphicsDepthState.Disabled); device.SetBlendMode(GraphicsBlendMode.Disabled);
        device.SetRasterizerState(GraphicsRasterizerState.Default);
        program.SetFloat("uViewportWidth",4); program.SetFloat("uViewportHeight",4); program.SetInt("uTexture",0);
        device.BindTexture(0,source.ColorTexture!); device.Draw(mesh);
        using var readback = device.RequestColorReadback(new(0,0,4,4),4,4);
        if (!readback.TryGetResult(out var image) || image is null) throw new InvalidOperationException("Android post-process readback failed.");
        var pixels=image.Pixels.Span; const int bottom=3*4*4;
        if (pixels[0]<250 || pixels[2]>3 || pixels[bottom]>3 || pixels[bottom+2]<250 || GLES20.GlGetError()!=0)
            throw new InvalidOperationException($"Android post-process orientation mismatch: top={pixels[0]},{pixels[2]}, bottom={pixels[bottom]},{pixels[bottom+2]}.");
        global::Android.Util.Log.Info("BEngine","BENGINE_ANDROID_POSTPROCESS_REGRESSION_OK|shipped shader|top red|bottom blue");
        return true;
    }

    internal static void Run(AndroidGlGraphicsDevice device)
    {
        const string vertex = "#version 330 core\nlayout(location=0) in vec2 position; uniform mat4 matrix; void main(){gl_Position=matrix*vec4(position,0.,1.);}";
        const string fragment = "#version 330 core\nuniform vec4 tint; uniform float gain; uniform int enabled; uniform sampler2D image; out vec4 color; void main(){color=tint*gain*float(enabled)*texture(image,vec2(.5));}";
        var description = new GraphicsShaderProgramDescription("Android native regression",
            new(GraphicsShaderStage.Vertex,GraphicsShaderLanguage.Glsl,vertex),
            new(GraphicsShaderStage.Fragment,GraphicsShaderLanguage.Glsl,fragment));
        using var target = device.CreateRenderTarget("Android regression",new(4,4,GraphicsTextureFormat.Rgba8Unorm,null));
        using var scope = device.PushRenderTarget(target);
        using var program = device.CreateProgram(description);
        using var decoy = device.CreateProgram(description with {Label="Android regression decoy"});
        using var mesh = device.CreateMesh(new("Android regression dynamic",new float[]{-1,-1,3,-1,-1,3},new(8,[new(0,2,0)]),usage:GraphicsBufferUsage.Dynamic));
        mesh.Update(new float[]{-1,-1,3,-1,-1,3});
        using var texture = device.CreateTexture2D("Android regression texture",new(1,1,GraphicsTextureFormat.Rgba8Unorm,GraphicsTextureUsage.Sampled),new byte[]{255,255,255,255});
        texture.Update(new byte[]{255,255,255,255});
        device.SetViewport(new(0,0,4,4)); device.SetScissor(null);
        device.SetDepthState(new(false,false)); device.SetBlendMode(GraphicsBlendMode.Disabled);
        device.Clear(GraphicsClearFlags.Color,System.Numerics.Vector4.Zero);
        decoy.Bind(); program.SetMatrix4x4("matrix",System.Numerics.Matrix4x4.Identity);
        decoy.Bind(); program.SetVector4("tint",new(.4f,.6f,.8f,1));
        decoy.Bind(); program.SetFloat("gain",.5f);
        decoy.Bind(); program.SetInt("enabled",1);
        decoy.Bind(); program.SetInt("image",0);
        device.BindTexture(0,texture); device.Draw(mesh);
        using var readback = device.RequestColorReadback(new(0,0,4,4),4,4);
        if (!readback.TryGetResult(out var image) || image is null) throw new InvalidOperationException("Android graphics regression readback failed.");
        var pixel = image.Pixels.Span;
        if (Math.Abs(pixel[0]-51)>2 || Math.Abs(pixel[1]-77)>2 || Math.Abs(pixel[2]-102)>2 || GLES20.GlGetError()!=0)
            throw new InvalidOperationException($"Android graphics regression pixel mismatch: {pixel[0]},{pixel[1]},{pixel[2]}.");
        global::Android.Util.Log.Info("BEngine","BENGINE_ANDROID_GRAPHICS_REGRESSION_OK|uniform binding|dynamic mesh|texture update|pixel readback");
        var compressed=Convert.FromBase64String("QlRFWDAwMDEEAAAABAAAAAUAAAABAAAAEAAAAP8dkkkkkkkkuGgwAv/////SdpkKs+eumdpJTttFLZ5BexDQ/VD1khyT6Zmf5e+Tfw==");
        var imported=TextureImageData.Deserialize(compressed);
        global::Android.Util.Log.Info("BEngine","BENGINE_ANDROID_BTEXT_HEADER_OK");
        using var etc=TextureUpload.Create(device,"Android ETC2 regression",compressed);
        global::Android.Util.Log.Info("BEngine","BENGINE_ANDROID_ETC2_UPLOAD_OK");
        program.SetVector4("tint",System.Numerics.Vector4.One);program.SetFloat("gain",1);
        device.BindTexture(0,etc);device.Draw(mesh);
        using var etcReadback=device.RequestColorReadback(new(0,0,4,4),4,4);
        if(!etcReadback.TryGetResult(out var etcImage)||etcImage is null)throw new InvalidOperationException("Android ETC2 readback failed.");
        // The fixture's block is a constant color. CPU decoding is verified by
        // the import regression; this probe exercises the native upload path.
        var result=etcImage.Pixels.Span;
        global::Android.Util.Log.Info("BEngine",$"BENGINE_ANDROID_ETC2_PIXEL|{result[0]},{result[1]},{result[2]},{result[3]}");
        if(GLES20.GlGetError()!=0 || Math.Abs(result[0]-181)>3 || Math.Abs(result[1]-99)>3 ||
            Math.Abs(result[2]-41)>3 || result[3]!=255)throw new InvalidOperationException("Android ETC2 native pixels invalid.");
        global::Android.Util.Log.Info("BEngine",$"BENGINE_ANDROID_TEXTURE_REGRESSION_OK|BTEX|ETC2|budget={GraphicsTextureMemory.Budget.LimitBytes}");
    }
}
