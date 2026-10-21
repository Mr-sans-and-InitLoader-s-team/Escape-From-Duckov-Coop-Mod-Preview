using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopModeCardScrim : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        var area = rectTransform.rect;
        var fadeEnd = area.yMax - Mathf.Min(100, area.height);
        Add(area.xMin, area.yMin, area.xMax, fadeEnd, color, color);
        var clear = new Color(color.r, color.g, color.b, 0);
        Add(area.xMin, fadeEnd, area.xMax, area.yMax, color, clear);

        void Add(float left, float bottom, float right, float top, Color lower, Color upper)
        {
            var start = vertices.currentVertCount;
            vertices.AddVert(new Vector3(left, bottom), lower, Vector2.zero);
            vertices.AddVert(new Vector3(left, top), upper, Vector2.up);
            vertices.AddVert(new Vector3(right, top), upper, Vector2.one);
            vertices.AddVert(new Vector3(right, bottom), lower, Vector2.right);
            vertices.AddTriangle(start, start + 1, start + 2);
            vertices.AddTriangle(start, start + 2, start + 3);
        }
    }
}
