using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimePlacement
{
    internal static bool Ground(Vector3 point,out RaycastHit ground)
    {
        var movement=CharacterMainControl.Main?.GetComponentInChildren<ECM2.CharacterMovement>(true);
        var layers=movement?movement.collisionLayers.value:Physics.DefaultRaycastLayers;
        ground=Physics.RaycastAll(point+Vector3.up*3,Vector3.down,7,layers,QueryTriggerInteraction.Ignore)
            .Where(h=>h.normal.y>.7f && !h.collider.GetComponentInParent<CharacterMainControl>())
            .OrderBy(h=>Mathf.Abs(h.point.y-point.y)).FirstOrDefault();
        return ground.collider;
    }

    internal static object Move(JObject command)
    {
        var requested=new Vector3((float)command["x"],(float)command["y"],(float)command["z"]);
        foreach(var offset in new[]{Vector3.zero,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,
            new Vector3(2,0,2),new Vector3(-2,0,2),new Vector3(2,0,-2),new Vector3(-2,0,-2)})
        {
            if(!Ground(requested+offset,out var hit))continue;
            var point=hit.point;
            if(new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back}.Any(direction=>
                !Ground(point+direction*.4f,out var edge) || Mathf.Abs(edge.point.y-point.y)>.2f))continue;
            CharacterMainControl.Main.SetPosition(point+Vector3.up*.05f);
            return new {position=new[]{point.x,point.y+.05f,point.z},ground=hit.collider.name};
        }
        throw new InvalidOperationException("No supported ground near the requested test point");
    }
}
