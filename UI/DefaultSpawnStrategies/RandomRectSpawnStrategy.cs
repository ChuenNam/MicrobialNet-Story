using System.Collections.Generic;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 示例策略：在屏幕归一化矩形区域内随机取点放置（分辨率无关）。
    /// 典型用途——「一串对话在给定范围内随机出现」：因策略实例跨多次 Show 保持，
    /// 它记住已用点，配合 avoidOverlap 可实现整段对话散布且不重叠。
    ///
    /// 这是「业务自定义出现逻辑」的范本：继承 <see cref="DialogueBoxSpawnStrategyAsset"/> 或实现
    /// <see cref="IDialogueBoxSpawnStrategy"/>，按业务需求计算 <see cref="DialogueBoxSpawnResolution"/> 即可，
    /// 完全无需改动管理器或剧情代码。
    /// </summary>
    [CreateAssetMenu(fileName = "RandomRectSpawnStrategy", menuName = "MicrobialNet/Story/对话框策略/矩形随机", order = 1)]
    public sealed class RandomRectSpawnStrategy : DialogueBoxSpawnStrategyAsset
    {
        [Tooltip("归一化矩形（x/y:0..1，原点屏幕左下；w/h:0..1）。默认整屏。例：左下 1/4 区 = (0,0,0.5,0.5)。")]
        [SerializeField] private Rect rangeNormalized = new Rect(0f, 0f, 1f, 1f);

        [SerializeField] private bool avoidOverlap;
        [SerializeField, Tooltip("避让判定的最小间距（像素，基于当前分辨率）")] private float minSpacing = 80f;
        [SerializeField, Tooltip("避让重试上限；超过则接受最近一次随机点")] private int maxRetries = 12;
        [SerializeField, Tooltip("已用点记忆上限（超出后丢弃最早记录）")] private int historyCap = 24;

        private readonly List<Vector2> _used = new List<Vector2>();

        /// <inheritdoc />
        public override DialogueBoxSpawnResolution Resolve(DialogueBoxSpawnContext context)
        {
            float w = Screen.width;
            float h = Screen.height;
            float rx = rangeNormalized.x * w;
            float ry = rangeNormalized.y * h;
            float rw = rangeNormalized.width * w;
            float rh = rangeNormalized.height * h;

            Vector2 off = Vector2.zero;
            for (int i = 0; i < maxRetries; i++)
            {
                float px = rx + Random.value * rw;
                float py = ry + Random.value * rh;
                // 转成以屏幕中心为原点的 anchoredPosition（Overlay 画布坐标，y 向上）
                off = new Vector2(px - w * 0.5f, py - h * 0.5f);
                if (!avoidOverlap || !ContainsTooClose(off))
                {
                    if (avoidOverlap) PushUsed(off);
                    break;
                }
            }

            return new DialogueBoxSpawnResolution
            {
                position = new DialogueBoxPosition
                {
                    mode = DialogueBoxPositionMode.ScreenAnchor,
                    anchor = TextAnchor.MiddleCenter,
                    offset = off
                }
            };
        }

        private bool ContainsTooClose(Vector2 p)
        {
            for (int i = 0; i < _used.Count; i++)
                if (Vector2.Distance(_used[i], p) < minSpacing) return true;
            return false;
        }

        private void PushUsed(Vector2 p)
        {
            _used.Add(p);
            while (_used.Count > historyCap) _used.RemoveAt(0);
        }
    }
}