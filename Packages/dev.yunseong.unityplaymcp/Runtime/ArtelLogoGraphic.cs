using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    /// <summary>Draws the Artel mark without requiring an SVG runtime dependency.</summary>
    public sealed class ArtelLogoGraphic : Graphic
    {
        internal static readonly Color32 Charcoal = new Color32(0x20, 0x23, 0x2B, 0xFF);
        internal static readonly Color32 Ink = new Color32(0xF2, 0xEF, 0xE9, 0xFF);
        internal static readonly Color32 Coral = new Color32(0xF0, 0x4B, 0x3A, 0xFF);

        // #F04B3A는 다크 배경(#14161C) 위에서 대비 4.5:1을 넘지 못한다. 라이트에서는
        // 원본을 그대로 쓴다. artel-home과 마케팅 사이트가 쓰는 규칙과 같다.
        internal static readonly Color32 CoralDark = new Color32(0xFF, 0x5C, 0x48, 0xFF);

        internal static Color32 Accent(bool darkTheme) => darkTheme ? CoralDark : Coral;

        internal static Color32 Body(bool darkTheme) => darkTheme ? Ink : Charcoal;

        private Color32 bodyColor = Charcoal;
        private Color32 accentColor = Coral;

        public Color32 BodyColor
        {
            get => bodyColor;
            set
            {
                bodyColor = value;
                SetVerticesDirty();
            }
        }

        public Color32 AccentColor
        {
            get => accentColor;
            set
            {
                accentColor = value;
                SetVerticesDirty();
            }
        }

        private const float StrokeWidth = 9f;
        private static readonly Vector2[] BodyPoints =
        {
            new Vector2(52f, 40f),
            new Vector2(52f, 18f),
            new Vector2(32f, 6f),
            new Vector2(12f, 18f),
            new Vector2(12f, 46f),
            new Vector2(30f, 56f)
        };

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            AddPolyline(vertexHelper, BodyPoints, bodyColor);
            AddStroke(vertexHelper, new Vector2(36f, 56f), new Vector2(52f, 46f), accentColor);
        }

        private void AddPolyline(VertexHelper vertexHelper, Vector2[] points, Color32 strokeColor)
        {
            var first = vertexHelper.currentVertCount;
            var halfWidth = StrokeWidth * Scale * 0.5f;

            for (var index = 0; index < points.Length; index++)
            {
                var point = ToLocal(points[index]);
                var previousNormal = index == 0
                    ? SegmentNormal(points[0], points[1])
                    : SegmentNormal(points[index - 1], points[index]);
                var nextNormal = index == points.Length - 1
                    ? previousNormal
                    : SegmentNormal(points[index], points[index + 1]);
                var miter = (previousNormal + nextNormal).normalized;
                var offset = miter * (halfWidth / Vector2.Dot(miter, nextNormal));

                AddVertex(vertexHelper, point - offset, strokeColor);
                AddVertex(vertexHelper, point + offset, strokeColor);
            }

            for (var index = 0; index < points.Length - 1; index++)
            {
                var start = first + (index * 2);
                vertexHelper.AddTriangle(start, start + 1, start + 3);
                vertexHelper.AddTriangle(start, start + 3, start + 2);
            }
        }

        private void AddStroke(VertexHelper vertexHelper, Vector2 start, Vector2 end, Color32 strokeColor)
        {
            var scale = Scale;
            start = ToLocal(start);
            end = ToLocal(end);

            var normal = Vector2.Perpendicular((end - start).normalized) * StrokeWidth * scale * 0.5f;
            var first = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, start - normal, strokeColor);
            AddVertex(vertexHelper, start + normal, strokeColor);
            AddVertex(vertexHelper, end + normal, strokeColor);
            AddVertex(vertexHelper, end - normal, strokeColor);
            vertexHelper.AddTriangle(first, first + 1, first + 2);
            vertexHelper.AddTriangle(first, first + 2, first + 3);
        }

        private float Scale => Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 64f;

        private Vector2 ToLocal(Vector2 point)
        {
            var scale = Scale;
            var origin = new Vector2(
                rectTransform.rect.xMin + ((rectTransform.rect.width - (64f * scale)) * 0.5f),
                rectTransform.rect.yMin + ((rectTransform.rect.height - (64f * scale)) * 0.5f));
            return origin + new Vector2(point.x, 64f - point.y) * scale;
        }

        private Vector2 SegmentNormal(Vector2 start, Vector2 end)
        {
            return Vector2.Perpendicular((ToLocal(end) - ToLocal(start)).normalized);
        }

        private static void AddVertex(VertexHelper vertexHelper, Vector2 position, Color32 strokeColor)
        {
            vertexHelper.AddVert(position, strokeColor, Vector2.zero);
        }
    }
}
