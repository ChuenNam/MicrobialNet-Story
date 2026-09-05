using System.IO;
using MicrobialNet.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 一键创建「剧情播放示例场景」。搭出 Canvas + EventSystem + StoryView（IStoryPresenter）+ StoryFlow +
    /// VariableDebugView + 事件桥接，保存为 DemoStory.unity。点 Play 即可看示例剧情——
    /// 实际对白由 DialogueBoxManager 弹出（story-line / story-choice / story-end 样式）。
    /// </summary>
    public static class StoryDemoSceneCreator
    {
        private const string SamplesDir = "Packages/com.microbialnet.story/Runtime/Sample";
        private const string ScenePath = SamplesDir + "/DemoStory.unity";

        [MenuItem("MicrobialNet/Story/创建示例剧情场景")]
        public static void CreateDemoScene()
        {
            if (!Directory.Exists(SamplesDir))
                Directory.CreateDirectory(SamplesDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // EventSystem（UI 指针事件必需）
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            // 主相机（预览/常规预期）
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var camera = cam.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            cam.AddComponent<AudioListener>();
            cam.transform.position = new Vector3(0f, 0f, -10f);

            // 主画布
            var canvasGO = new GameObject("StoryCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // 对话面板：仅作 StoryView / 变量监视 / 事件桥接 的容器（实际对白由 DialogueBoxManager 弹出）
            var panel = new GameObject("DialoguePanel");
            panel.transform.SetParent(canvasGO.transform, false);

            var view = panel.AddComponent<StoryView>();

            // 中文字体：示例剧情全为中文，找项目里非 LiberationSans 的 TMP 字体资产并应用（找不到则提示）。
            var cjkFont = FindCjkFontAsset();
            if (cjkFont == null)
                Debug.LogWarning("[StoryDemoSceneCreator] 未找到中文字体资产（TMP_FontAsset，且非 LiberationSans）。示例剧情中文可能显示为方框；请导入中文字体并用 Font Asset Creator 生成 SDF 后重跑本菜单。");

            // 变量监视（独立 TMP，由 VariableDebugView 每帧刷新）
            var varDebug = MakeTMP(panel.transform, "VarDebug", 18, TextAlignmentOptions.TopLeft, false, cjkFont);
            var varDebugRT = varDebug.GetComponent<RectTransform>();
            varDebugRT.anchorMin = new Vector2(0.55f, 0.88f); varDebugRT.anchorMax = new Vector2(1f, 1f);
            varDebugRT.offsetMin = new Vector2(8f, 4f); varDebugRT.offsetMax = new Vector2(-8f, -4f);
            varDebugRT.pivot = new Vector2(1f, 1f);
            varDebug.raycastTarget = false;
            varDebug.fontStyle = FontStyles.Normal;
            varDebug.outlineWidth = 0.15f;
            varDebug.outlineColor = Color.black;
            varDebug.gameObject.AddComponent<VariableDebugView>();

            // 宿主
            var host = panel.AddComponent<StoryFlow>();
            host.Configure(view);
            panel.AddComponent<StoryDemoEventBridge>();
            EditorUtility.SetDirty(host);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"[StoryDemoSceneCreator] 示例场景已生成：{ScenePath}。点 Play 即可播放内置示例剧情（对白由 DialogueBoxManager 弹出）。");
        }

        private static TextMeshProUGUI MakeTMP(Transform parent, string name, float fontSize, TextAlignmentOptions align, bool raycast, TMP_FontAsset font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.raycastTarget = raycast;
            t.text = string.Empty;
            return t;
        }

        /// <summary>在项目里查找一个覆盖中文的 TMP 字体资产（排除默认的 LiberationSans）。找不到返回 null。</summary>
        private static TMP_FontAsset FindCjkFontAsset()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("LiberationSans")) continue;
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa != null) return fa;
            }
            return null;
        }
    }
}
