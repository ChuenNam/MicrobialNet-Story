using TMPro;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 运行时变量监视面板（示例 / 验证用）。
    ///
    /// 剧情变量运行期活在 <see cref="IStoryVariableProvider"/>（纯 C# 对象，Inspector / Game 视图默认不可见），
    /// 本组件把 <see cref="StoryFlow.FormatVariables"/> 的结果每帧刷到一个 TMP 文本上，
    /// 让玩家 / 开发者在 Play 模式直观看到变量变化（如 hp 100 → 90）。
    ///
    /// 正式接入宿主时此组件可移除，由宿主自己的 HUD / 调试器接管变量展示。
    /// </summary>
    [AddComponentMenu("MicrobialNet/Story/Story Variable Debug", 30)]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class VariableDebugView : MonoBehaviour
    {
        [SerializeField] private StoryFlow host;

        private TextMeshProUGUI _text;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
            if (host == null) host = GetComponentInParent<StoryFlow>();
        }

        private void Update()
        {
            if (host == null || _text == null) return;
            _text.text = host.FormatVariables();
        }
    }
}
