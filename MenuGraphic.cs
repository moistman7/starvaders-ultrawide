using System;
using UnityEngine;
using UnityEngine.UI;

namespace StarVadersUltrawide
{
    // Render the approved Settings-style shapes as geometry, so artwork cannot
    // disappear, be trimmed, or change depending on which scene loaded first.
    public class MenuGraphic : MaskableGraphic
    {
        public MenuGraphic(IntPtr pointer) : base(pointer) { }
        internal float Bottom, Top = 1f, Slant = 10f;

        public override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            float bottom = Mathf.Lerp(rect.yMin, rect.yMax, Bottom);
            float top = Mathf.Lerp(rect.yMin, rect.yMax, Top);
            float cut = Mathf.Min(5f, (top - bottom) * 0.15f);
            var points = new Vector2[]
            {
                new(rect.xMin + Slant, bottom), new(rect.xMax - cut, bottom),
                new(rect.xMax, bottom + cut), new(rect.xMax - Slant, top - cut),
                new(rect.xMax - Slant - cut, top), new(rect.xMin + cut, top),
                new(rect.xMin, top - cut), new(rect.xMin + Slant - cut, bottom + cut)
            };
            mesh.AddVert(new Vector3(rect.center.x, (bottom + top) * 0.5f, 0), color, Vector2.zero);
            foreach (var point in points) mesh.AddVert(new Vector3(point.x, point.y, 0), color, Vector2.zero);
            for (int i = 0; i < points.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
        }
    }
}
