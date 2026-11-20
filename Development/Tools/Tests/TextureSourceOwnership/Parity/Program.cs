using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine;
using BEngine.Platform;
using BEngine.Platform.DotNet;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;
using BEngine.TiledMap;
using Microsoft.Extensions.DependencyInjection;

var output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
using var services=new ServiceCollection().AddBEngineDotNetPlatform().AddSingleton(new GraphicsUtility(new DrawingPlatform())).BuildServiceProvider();
PlatformRuntime.Initialize(services);
Resources.RegisterResourceRoot(Path.GetFullPath(args[1]));
RuntimeHelpers.RunModuleConstructor(typeof(Tilemap).Module.ModuleHandle);
using var scene=new Scene("Texture source parity",services);
var opaque=new byte[]{1,2,3,255,4,5,6,255,7,8,9,255,10,11,12,255};
var transparent=opaque.ToArray();transparent[3]=127;
byte[] Encode(byte[] rgba) { var texture=Texture.FromRgba32(2,2,rgba); try{return texture.EncodeToPNG();}finally{BObject.DestroyImmediate(texture);} }
var fixtures=new Dictionary<string,byte[]> {
 ["opaque.png"]=Encode(opaque), ["transparent.png"]=Encode(transparent),
 ["mips.btex"]=new TextureImageData(2,2,TextureCompressionFormat.Rgba32,new byte[][]{opaque,new byte[]{0,0,0,0}}).Serialize(),
 ["compressed.btex"]=new TextureImageData(4,4,TextureCompressionFormat.Bc1,new byte[][]{new byte[8]}).Serialize(),
 ["bad.png"]=[1,2,3], ["empty.png"]=[],
};
var corrupt=fixtures["mips.btex"].ToArray();corrupt[^1]^=1;fixtures["corrupt.btex"]=corrupt;
foreach(var fixture in fixtures)File.WriteAllBytes(Path.Combine(output,fixture.Key),fixture.Value);
var palette=new TilePalette{ Name="Parity", Tiles=fixtures.Keys.Append("absent.png").Select((name,index)=>new TileDefinition{Id=index+1,Texture=Path.Combine(output,name)}).ToList() };
var go=scene.CreateGameObject("Tiles");var tilemap=go.AddComponent<Tilemap>();tilemap.palette=palette;
var renderer=go.AddComponent<TilemapRenderer>();renderer.cullOutsideCamera=false;
for(var y=0;y<8;y++)for(var x=0;x<12;x++)tilemap.SetTile(new(x-6,y-4),1+(x+y)%8);
using var device=new RecordingDevice();
using var drawing=new PortableSceneRenderer(device);
using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);device.Capture=hash;
Span<int> counters=stackalloc int[4];
var drawnFrames=0;
for(var frame=0;frame<256;frame++)
{
    if(frame%64==0)foreach(var tile in palette.Tiles)BAsset.Invalidate(tile.Texture);
    if(frame==80)renderer.color=new Color(1,1,1,Fix64.Half);
    if(frame==128)renderer.color=Color.white;
    if(frame==160)renderer.enabled=false;
    if(frame==192)renderer.enabled=true;
    if(frame==224)tilemap.SetTile(new(0,0),2,TileTransformFlags.None);
    var before=device.Draws;
    drawing.Render(scene,RenderCamera.Default with { Position=new Vector2(frame%16/(Fix64)32,0) },1280,720);
    if(device.Draws>before)drawnFrames++;
    counters[0]=frame;counters[1]=device.Draws-before;counters[2]=drawing.LastRenderStatistics.VisibleSubmissionCount;counters[3]=drawing.LastRenderStatistics.BatchCount;
    hash.AppendData(MemoryMarshal.AsBytes(counters));
}
if(drawnFrames!=224||device.Draws<224||device.ResourcesCreated<8)throw new Exception($"Insufficient tile workload: frames={drawnFrames}, draws={device.Draws}, resources={device.ResourcesCreated}");
var digest=Convert.ToHexString(hash.GetHashAndReset());device.Capture=null;
drawing.Dispose();BObject.DestroyImmediate(palette);
File.WriteAllText(Path.Combine(output,"Result.json"),JsonSerializer.Serialize(new { Passed=true,Checks=5,Frames=256,DrawnFrames=drawnFrames,TextureVertexBlendAndDrawSha256=digest,device.Draws,device.ResourcesCreated,device.ResourcesDisposed,Scope="96 mixed tiles: opaque/transparent PNG, RGBA mip chains, unsupported GPU compression fallback, corrupt/empty/missing input, generation invalidation and enable/disable. RHI command/texture bytes, not GPU pixels." },new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("TILE_TEXTURE_PARITY_OK");
