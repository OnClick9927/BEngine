using NVector4 = System.Numerics.Vector4;
namespace BEngine.Rendering;
public static class RenderCameraFactory
{
    public static RenderCamera From(Camera2D camera)
    {
        IRenderCameraPolicy? policy = null;
        var gameObject = camera.gameObject;
        if (gameObject.activeInHierarchy)
        {
            var components = gameObject.components;
            for (var index = 0; index < components.Count; index++)
            {
                var component = components[index];
                if (component is not Behaviour { enabled: true } ||
                    component is not IRenderCameraPolicyProvider provider) continue;
                policy = provider.CreatePolicy();
                break;
            }
        }
        return new RenderCamera(
        camera.transform.position,
        camera.transform.rotation,
        camera.size,
        new NVector4((float)camera.backgroundColor.r, (float)camera.backgroundColor.g,
            (float)camera.backgroundColor.b, (float)camera.backgroundColor.a),
        camera.clearMode,
        camera.cullingMask,
        camera.viewportRect)
        {
            Policy = policy
        };
    }

}
