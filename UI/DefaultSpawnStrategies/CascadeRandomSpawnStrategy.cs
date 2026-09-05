using System.Collections.Generic;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 级联随机策略：新框围绕「最近一次弹出的对话」在其周围随机偏移出现，且层级自动递增（永远在最上层）。
    ///
    /// 典型用途——用户需求「一串对话保留显示，新出的对话在旧的周围随机生成，且盖在旧的上层」。
    /// 与 <see cref="RandomRectSpawnStrategy"/> 的区别：锚点不是固定矩形的中心，而是动态的「上一个框中心」，
    /// 因此形成由一点向外扩散的级联堆叠，而非在固定区域内均匀散布。
    ///
    /// 使用前提（保留显示）：调用侧需「多次 Show 而不关闭前一个框」——本策略只管出现位置与层级，
    /// 不负责关闭旧框。<see cref="DialogueBoxManager.Show"/> 本身不会关闭任何已有框，
    /// 因此只要调用侧不主动 Close 上一个句柄，旧框即保留（见 StoryView 的 ShowLine / 事件节点并发 Show 等用法）。
    /// </summary>
    [CreateAssetMenu(fileName = "CascadeRandomSpawnStrategy", menuName = "MicrobialNet/Story/对话框策略/级联随机", order = 2)]
    public sealed class CascadeRandomSpawnStrategy : DialogueBoxSpawnStrategyAsset
    {
        /// <summary>锚点取法。</summary>
        public enum AnchorMode
        {
            /// <summary>围绕最近一次弹出的框中心（由内向外扩散）。</summary>
            LastCenter,
            /// <summary>围绕当前全部活动框的几何中心。</summary>
            BoundingCenter,
        }

        [Tooltip("无历史框（第一框）时的基准锚点，以屏幕中心为原点（像素，y 向上）。默认屏幕中心。")]
        [SerializeField] private Vector2 originOffset = Vector2.zero;

        [SerializeField] private AnchorMode anchorMode = AnchorMode.LastCenter;

        [Tooltip("相对锚点的随机半径区间（像素，基于当前分辨率）。")]
        [SerializeField] private float radiusMin = 40f;
        [SerializeField] private float radiusMax = 160f;

        [Tooltip("是否把结果坐标夹在屏幕内（留边距）。")]
        [SerializeField] private bool clampToScreen = true;
        [SerializeField] private float screenMargin = 40f;

        [Tooltip("层级步长。新框层级 = 当前活动数 × 步长，天然递增到最上层。")]
        [SerializeField] private int layerStep = 1;

        [Tooltip("点击继续时是否关闭自身。true=普通对白（点了就关）；false=保留（级联串默认，整串逐条点出并堆叠显示）。")]
        [SerializeField] private bool closeOnAdvance = false;

        /// <inheritdoc />
        public override DialogueBoxSpawnResolution Resolve(DialogueBoxSpawnContext context)
        {
            Vector2 anchor = ResolveAnchor(context);
            float ang = Random.value * Mathf.PI * 2f;
            float dist = radiusMin + Random.value * Mathf.Max(0f, radiusMax - radiusMin);
            Vector2 off = anchor + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;

            if (clampToScreen)
            {
                float halfW = Screen.width * 0.5f - screenMargin;
                float halfH = Screen.height * 0.5f - screenMargin;
                off.x = Mathf.Clamp(off.x, -halfW, halfW);
                off.y = Mathf.Clamp(off.y, -halfH, halfH);
            }

            return new DialogueBoxSpawnResolution
            {
                position = new DialogueBoxPosition
                {
                    mode = DialogueBoxPositionMode.ScreenAnchor,
                    anchor = TextAnchor.MiddleCenter,
                    offset = off
                },
                // 层级随弹出数递增，新框永远在最上层（ApplyOrder 按 layer 排序，同层按 instanceId 平手）。
                layerOverride = Mathf.Max(0, context.totalActive) * layerStep,
                // 级联串默认保留：点击继续只推进剧情、不关自身，整串逐条点出并堆叠显示。
                persistent = !closeOnAdvance
            };
        }

        private Vector2 ResolveAnchor(DialogueBoxSpawnContext context)
        {
            if (context.activeBoxes == null || context.activeBoxes.Count == 0)
                return originOffset;

            if (anchorMode == AnchorMode.BoundingCenter)
            {
                Vector2 sum = Vector2.zero;
                int n = 0;
                foreach (var b in context.activeBoxes)
                {
                    var p = b.spec != null ? b.spec.position : null;
                    if (p != null && p.mode == DialogueBoxPositionMode.ScreenAnchor)
                    {
                        sum += p.offset;
                        n++;
                    }
                }
                return n > 0 ? sum / n : originOffset;
            }

            // LastCenter：取当前最上层（列表最后，_active 已按 layer+instanceId 排序）的框中心。
            for (int i = context.activeBoxes.Count - 1; i >= 0; i--)
            {
                var p = context.activeBoxes[i].spec?.position;
                if (p != null && p.mode == DialogueBoxPositionMode.ScreenAnchor)
                    return p.offset;
            }
            return originOffset;
        }
    }
}
