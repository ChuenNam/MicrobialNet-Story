using System.Collections.Generic;
using MicrobialNet.Story.EditorTools.UI;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

namespace MicrobialNet.Story.EditorTools.Graph
{
    /// <summary>
    /// 试跑路径流动绘制工具：直接在「原连线自身的 EdgeControl」上叠加流动的白色亮点，
    /// 表示剧情流向。连线本身的青色高亮由 StoryGraphView 对 EdgeControl.inputColor/outputColor
    /// 赋值完成（EdgeControl 自己绘制）；本工具仅负责在 EdgeControl.generateVisualContent 事件里，
    /// 复用 EdgeControl 自己的 controlPoints，经 edge.ChangeCoordinatesTo(edgeControl, p) 转换到
    /// edgeControl 局部坐标系后，沿折线画移动白点。Unity 2022.3 的可见连线是「直线段+圆角折线」
    /// （非贝塞尔），故直接以控制点作折线顶点，与默认连线形状完全一致、无坐标偏差、不覆盖节点
    /// （按 capRadius 裁剪端点）。
    /// </summary>
    public static class FlowLineDraw
    {
        /// <summary>沿曲线移动的亮点数量。</summary>
        private const int DotCount = 3;

        private const float DotRadius = 3.5f;

        /// <summary>流动连线青色（与 StoryGraphView 曾用色一致），改由主题集中提供。</summary>
        public static Color FlowColor => StoryEditorTheme.Flow;

        /// <summary>
        /// 在 EdgeControl 的 generateVisualContent 事件里调用：沿其连线画移动白点。
        /// mgc 绘制坐标系 == EdgeControl 局部坐标系；controlPoints 在 edge 坐标系，由 GetCurvePoints
        /// 经 edge.ChangeCoordinatesTo(edgeControl, p) 转换到该局部坐标系后再用。
        /// </summary>
        public static void DrawDots(MeshGenerationContext mgc, Edge edge, float time)
        {
            var ec = edge.edgeControl;
            if (ec == null || edge.input == null || edge.output == null) return;
            if (edge.input.panel == null || edge.output.panel == null) return;

            var pts = GetCurvePoints(ec, edge);
            if (pts.Count < 2) return;

            float cap = ec.capRadius;
            pts = TrimByCapRadius(pts, cap);
            if (pts.Count < 2) return;

            var painter = mgc.painter2D;
            // 显式绘制青色连线：不依赖 EdgeControl.inputColor/outputColor 的重绘行为（部分 2022.3 小版本改色不触发重绘，
            // 导致线不变色）。直接在本 handler 里描边，与默认连线完全重合（同一条折线、同一坐标系），
            // 每次 generateVisualContent 都重画，保证推进时新连线立即变青。描边略粗于默认线宽以覆盖其下的灰色默认线。
            painter.lineWidth = Mathf.Max(ec.edgeWidth, 2f) + 2f;
            painter.strokeColor = FlowColor;
            painter.BeginPath();
            painter.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++)
                painter.LineTo(pts[i]);
            painter.Stroke();

            var samples = BuildArcLengthSamples(pts);
            for (int i = 0; i < DotCount; i++)
            {
                float t = (time * 0.5f + (float)i / DotCount) % 1f;
                if (t < 0f) t += 1f;
                Vector2 p = SampleByLength(samples, t);
                FillCircle(mgc, p, DotRadius);
            }
        }

        /// <summary>
        /// 取连线路径（EdgeControl 局部坐标系）。根据 Unity 2022.3 源码：
        /// EdgeControl.controlPoints 位于「edge（父级/图）坐标系」，EdgeControl 自身绘制时通过
        /// edge.ChangeCoordinatesTo(edgeControl, p) 转成 edgeControl 局部坐标系后渲染。
        /// 可见连线不是贝塞尔曲线，而是「直线段 + 圆角折线」（m_RenderPoints 网格）；
        /// 因此这里用同样的 ChangeCoordinatesTo 转换后，直接把控制点当作折线顶点顺序连接，
        /// 与默认连线形状完全一致、零坐标偏差，也不再做任何曲线拟合。
        /// </summary>
        private static List<Vector2> GetCurvePoints(EdgeControl ec, Edge edge)
        {
            var pts = new List<Vector2>();
            var cp = ec.controlPoints;
            if (cp != null && cp.Length >= 2)
            {
                // controlPoints 在 edge 局部坐标系；转成 edgeControl 局部坐标系（与 EdgeControl 自身绘制完全一致）
                for (int i = 0; i < cp.Length; i++)
                    pts.Add(edge.ChangeCoordinatesTo(ec, cp[i]));
                return pts;
            }

            // fallback：端口中心（世界坐标）→ edgeControl 局部坐标，直线连接
            if (edge.input == null || edge.output == null) return pts;
            pts.Add(ec.WorldToLocal(edge.output.worldBound.center));
            pts.Add(ec.WorldToLocal(edge.input.worldBound.center));
            return pts;
        }

        /// <summary>按首尾各裁剪 capRadius 长度，避免流动连线进入节点内容区覆盖节点。</summary>
        private static List<Vector2> TrimByCapRadius(List<Vector2> pts, float radius)
        {
            if (pts.Count < 2 || radius <= 0f) return pts;
            float total = 0f;
            var segLens = new float[pts.Count - 1];
            for (int i = 0; i < pts.Count - 1; i++)
            {
                segLens[i] = (pts[i + 1] - pts[i]).magnitude;
                total += segLens[i];
            }
            if (total <= radius * 2f) return pts; // 太短就不裁剪

            float TrimStart(float limit)
            {
                float acc = 0f;
                for (int i = 0; i < segLens.Length; i++)
                {
                    float next = acc + segLens[i];
                    if (next >= limit)
                    {
                        float ratio = (limit - acc) / segLens[i];
                        return i + ratio;
                    }
                    acc = next;
                }
                return segLens.Length;
            }

            float startIdx = TrimStart(radius);
            float endIdx = TrimStart(total - radius);

            var result = new List<Vector2>();
            int i0 = Mathf.FloorToInt(startIdx);
            float r0 = startIdx - i0;
            int i1 = Mathf.FloorToInt(endIdx);
            float r1 = endIdx - i1;

            result.Add(Vector2.Lerp(pts[i0], pts[i0 + 1], r0));
            for (int i = i0 + 1; i <= i1; i++)
                result.Add(pts[i]);
            if (i1 + 1 < pts.Count)
                result.Add(Vector2.Lerp(pts[i1], pts[i1 + 1], r1));

            return result;
        }

        private static void FillCircle(MeshGenerationContext mgc, Vector2 center, float r)
        {
            var painter = mgc.painter2D;
            painter.fillColor = new Color(1f, 1f, 1f, 0.95f);
            painter.BeginPath();
            const int seg = 12;
            for (int s = 0; s <= seg; s++)
            {
                float a = (float)s / seg * Mathf.PI * 2f;
                Vector2 p = center + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                if (s == 0) painter.MoveTo(p);
                else painter.LineTo(p);
            }
            painter.ClosePath();
            painter.Fill();
        }

        private sealed class LengthSample
        {
            public float Length;
            public Vector2 Point;
        }

        private static LengthSample[] BuildArcLengthSamples(List<Vector2> pts)
        {
            if (pts.Count == 0) return new LengthSample[0];
            var list = new List<LengthSample> { new LengthSample { Length = 0f, Point = pts[0] } };
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                acc += (pts[i] - pts[i - 1]).magnitude;
                list.Add(new LengthSample { Length = acc, Point = pts[i] });
            }
            return list.ToArray();
        }

        private static Vector2 SampleByLength(LengthSample[] samples, float t)
        {
            if (samples.Length == 0) return Vector2.zero;
            if (samples.Length == 1) return samples[0].Point;
            float total = samples[samples.Length - 1].Length;
            if (total <= 0f) return samples[0].Point;
            float target = t * total;
            for (int i = 1; i < samples.Length; i++)
            {
                if (samples[i].Length >= target)
                {
                    float ratio = (target - samples[i - 1].Length) / (samples[i].Length - samples[i - 1].Length);
                    return Vector2.Lerp(samples[i - 1].Point, samples[i].Point, ratio);
                }
            }
            return samples[samples.Length - 1].Point;
        }
    }
}
