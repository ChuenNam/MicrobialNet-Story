using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MicrobialNet.Story.UI;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 对话框系统一键 Playtest（验证用）。挂到场景任意物体上，点 Play 后在 Game 视图左上角出现按钮面板，
    /// 用于验证弹出 / 关闭 / 层级 / 模态拦截 / 自动关闭。自带纯 UGUI 测试模板，不依赖 TMP 与剧情资产。
    /// 由菜单 MicrobialNet/Story/测试/对话框系统 Playtest 一键生成，也可手动 Add Component 挂载。
    /// </summary>
    [AddComponentMenu("MicrobialNet/Story/测试/对话框 Playtest", 100)]
    public sealed class DialogueBoxPlaytest : MonoBehaviour
    {
        private int _counter;

        private void Awake()
        {
            var mgr = DialogueBoxManager.Ensure();
            if (!mgr.HasStyle("playtest"))
                mgr.RegisterStyle("playtest", BuildTemplate(), 0.2f, 0.2f);
        }

        private static GameObject BuildTemplate()
        {
            var go = new GameObject("PlaytestTemplate");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(640f, 160f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.2f, 0.35f, 0.92f);
            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            // Unity 2022.3 起内置字体由 Arial.ttf 改名为 LegacyRuntime.ttf，旧名取不到会抛 ArgumentException。
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 28;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            var txtRT = (RectTransform)txtGo.transform;
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = new Vector2(16f, 16f); txtRT.offsetMax = new Vector2(-16f, -16f);
            go.AddComponent<CanvasGroup>();
            go.AddComponent<PlaytestBoxView>();
            go.SetActive(false);
            return go;
        }

        private sealed class PlaytestBoxView : MonoBehaviour, IDialogueBoxView, IDialogueBoxRecyclable, IPointerDownHandler
        {
            private Text _txt;
            private DialogueBoxHandle _h;
            private void Awake() => _txt = GetComponentInChildren<Text>();
            public void Setup(DialogueBoxHandle h, object payload)
            {
                _h = h;
                if (_txt != null) _txt.text = payload as string ?? "(空)";
            }
            public void OnPointerDown(PointerEventData e) => _h?.Close();
            public void OnRecycle()
            {
                if (_txt != null) _txt.text = string.Empty;
                _h = null;
            }
        }

        private void OnGUI()
        {
            var mgr = DialogueBoxManager.Ensure();
            GUILayout.BeginArea(new Rect(10, 10, 270, 380), "对话框系统 Playtest", GUI.skin.window);
            if (GUILayout.Button($"① 弹普通对白 (layer=10)")) ShowNormal();
            if (GUILayout.Button("② 弹模态确认 (modal,layer=20)")) ShowModal();
            if (GUILayout.Button("③ 弹 2 秒自动关闭")) ShowAutoClose();
            if (GUILayout.Button("④ 弹顶部居中 (layer=50)")) ShowTop();
            if (GUILayout.Button("⑤ 叠 2 个 (验证层级)")) { ShowNormal(40); ShowNormal(41); }
            if (GUILayout.Button("⑥ CloseTop (关最顶)")) mgr.CloseTop();
            if (GUILayout.Button("⑦ CloseAll (关全部)")) mgr.CloseAll();
            GUILayout.Label($"当前活动数: {mgr.ActiveCount}");
            GUILayout.EndArea();
        }

        private void ShowNormal() => Show($"普通对白 #{++_counter}", DialogueBoxPosition.BottomCenter(), 10, false, 0f);
        private void ShowNormal(int layer) => Show($"叠放对白 #{++_counter}", DialogueBoxPosition.BottomCenter(), layer, false, 0f);
        private void ShowModal() => Show($"模态确认：点本框关闭，下层不穿透 (#{++_counter})", DialogueBoxPosition.BottomCenter(), 20, true, 0f);
        private void ShowAutoClose() => Show($"我会 2 秒后自动消失 (#{++_counter})", DialogueBoxPosition.BottomCenter(), 30, false, 2f);
        private void ShowTop() => Show($"顶部居中高层级 (#{++_counter})", DialogueBoxPosition.TopCenter(), 50, false, 0f);

        private void Show(string text, DialogueBoxPosition pos, int layer, bool modal, float auto)
        {
            DialogueBoxManager.Ensure().Show(new DialogueBoxSpec
            {
                styleKey = "playtest",
                position = pos,
                layer = layer,
                modal = modal,
                autoCloseSeconds = auto,
                payload = text
            });
        }
    }
}
