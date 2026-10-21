using UnityEngine.UI;

namespace EscapeFromDuckovCoopMod;

internal sealed class CoopModeBackdrop : MaskableGraphic
{
    private CoopModeBackdropStyle _style;
    private Color _base;

    internal void Initialize(CoopModeBackdropStyle style, Color background)
    {
        _style = style;
        _base = background;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (_style == null) return;
        const int steps = 96;
        var area = rectTransform.rect;
        for (var y = 0; y <= steps; y++)
        for (var x = 0; x <= steps; x++)
        {
            var point = new Vector2(x / (float)steps, y / (float)steps);
            var shade = Color.Lerp(_base, _style.ambient, _style.ambient.a);
            var floor = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(_style.floorHeight - .08f, _style.floorHeight + .08f, point.y));
            shade = Color.Lerp(shade, _style.floor, floor * _style.floor.a);
            var along = Vector2.Dot(point - _style.lightOrigin, _style.lightDirection.normalized);
            var across = Mathf.Abs(Vector2.Dot(point - _style.lightOrigin,
                new Vector2(-_style.lightDirection.y, _style.lightDirection.x).normalized));
            var beam = Mathf.Exp(-across * across / (_style.lightWidth * _style.lightWidth)) *
                Mathf.Clamp01(1 - Mathf.Abs(along) / _style.lightLength);
            shade = Color.Lerp(shade, _style.light, beam * _style.light.a);
            foreach (var shadow in _style.shadows)
            {
                var offset = new Vector2((point.x - shadow.center.x) / shadow.size.x, (point.y - shadow.center.y) / shadow.size.y);
                shade = Color.Lerp(shade, _style.shadow, Mathf.Exp(-offset.sqrMagnitude * 2) * _style.shadow.a);
            }
            shade = Color.Lerp(_base, shade, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(_style.fadeTop, 1, point.y)));
            shade.a = 1;
            mesh.AddVert(new Vector3(area.xMin + point.x * area.width, area.yMin + point.y * area.height), shade, Vector2.zero);
            if (x == steps || y == steps) continue;
            var index = y * (steps + 1) + x;
            mesh.AddTriangle(index, index + steps + 1, index + 1);
            mesh.AddTriangle(index + 1, index + steps + 1, index + steps + 2);
        }
    }
}
