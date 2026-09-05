using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 剧情对话样式模板构建器。在运行时用代码拼出 story-line / story-choice / story-end 的
    /// 预制体模板（无需 .prefab 资产），交给 DialogueBoxManager 注册后按样式键克隆。
    /// 模板本身保持 inactive；实际显示由管理器克隆并弹出。
    /// </summary>
    public static class StoryBoxTemplates
    {
        public static GameObject BuildLineTemplate()
        {
            var go = new GameObject("StoryLineTemplate");
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(900f, 260f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

            var speaker = AddTMP(go.transform, "Speaker", 26, TextAlignmentOptions.Left);
            var speakerRT = (RectTransform)speaker.transform;
            speakerRT.anchorMin = new Vector2(0f, 0.82f); speakerRT.anchorMax = new Vector2(1f, 1f);
            speakerRT.offsetMin = new Vector2(24f, 4f); speakerRT.offsetMax = new Vector2(-24f, -4f);

            var portrait = new GameObject("Portrait", typeof(RectTransform));
            portrait.transform.SetParent(go.transform, false);
            var pImg = portrait.AddComponent<Image>();
            pImg.enabled = false;
            var pRT = (RectTransform)portrait.transform;
            pRT.anchorMin = new Vector2(0f, 0.82f); pRT.anchorMax = new Vector2(0.04f, 1f);
            pRT.offsetMin = Vector2.zero; pRT.offsetMax = Vector2.zero;

            var body = AddTMP(go.transform, "Body", 24, TextAlignmentOptions.TopLeft);
            var bodyRT = (RectTransform)body.transform;
            bodyRT.anchorMin = new Vector2(0f, 0.1f); bodyRT.anchorMax = new Vector2(1f, 0.8f);
            bodyRT.offsetMin = new Vector2(24f, 8f); bodyRT.offsetMax = new Vector2(-24f, -8f);
            body.enableWordWrapping = true;

            var hint = AddTMP(go.transform, "Hint", 20, TextAlignmentOptions.Right);
            var hintRT = (RectTransform)hint.transform;
            hintRT.anchorMin = new Vector2(0.7f, 0f); hintRT.anchorMax = new Vector2(1f, 0.1f);
            hintRT.offsetMin = new Vector2(8f, 4f); hintRT.offsetMax = new Vector2(-24f, -4f);
            hint.text = "> 点击继续";

            var view = go.AddComponent<StoryLineBoxView>();
            view.InitRefs(speaker, body, hint, pImg);
            go.AddComponent<CanvasGroup>();
            go.SetActive(false);
            return go;
        }

        public static GameObject BuildChoiceTemplate()
        {
            var go = new GameObject("StoryChoiceTemplate");
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(900f, 320f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.9f);

            var choices = new GameObject("Choices", typeof(RectTransform));
            choices.transform.SetParent(go.transform, false);
            var vlg = choices.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 8;
            vlg.padding = new RectOffset(16, 16, 16, 16);
            var cRT = (RectTransform)choices.transform;
            cRT.anchorMin = Vector2.zero; cRT.anchorMax = Vector2.one;
            cRT.offsetMin = Vector2.zero; cRT.offsetMax = Vector2.zero;

            var view = go.AddComponent<StoryChoiceBoxView>();
            view.InitRefs(choices.transform);
            go.AddComponent<CanvasGroup>();
            go.SetActive(false);
            return go;
        }

        public static GameObject BuildMessageTemplate(bool isError)
        {
            var go = new GameObject(isError ? "StoryErrorTemplate" : "StoryEndTemplate");
            go.AddComponent<RectTransform>().sizeDelta = new Vector2(700f, 200f);
            var bg = go.AddComponent<Image>();
            bg.color = isError ? new Color(0.4f, 0.05f, 0.05f, 0.9f) : new Color(0.05f, 0.05f, 0.08f, 0.85f);

            var title = AddTMP(go.transform, "Title", 24, TextAlignmentOptions.Center);
            var titleRT = (RectTransform)title.transform;
            titleRT.anchorMin = new Vector2(0f, 0.6f); titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.offsetMin = new Vector2(16f, 4f); titleRT.offsetMax = new Vector2(-16f, -4f);

            var body = AddTMP(go.transform, "Body", 22, TextAlignmentOptions.Center);
            var bodyRT = (RectTransform)body.transform;
            bodyRT.anchorMin = new Vector2(0f, 0f); bodyRT.anchorMax = new Vector2(1f, 0.6f);
            bodyRT.offsetMin = new Vector2(16f, 8f); bodyRT.offsetMax = new Vector2(-16f, -8f);
            body.enableWordWrapping = true;

            var view = go.AddComponent<StoryMessageBoxView>();
            view.InitRefs(title, body);
            go.AddComponent<CanvasGroup>();
            go.SetActive(false);
            return go;
        }

        private static TextMeshProUGUI AddTMP(Transform parent, string name, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = StoryFontResolver.Resolve();
            t.fontSize = fontSize;
            t.alignment = align;
            t.raycastTarget = false;
            t.text = string.Empty;
            return t;
        }
    }
}
