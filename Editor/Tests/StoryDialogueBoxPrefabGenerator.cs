#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MicrobialNet.Story;       // StoryBoxTemplates（public）
using MicrobialNet.Story.UI;  // DialogueBox

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 把代码构建的对话框模板导出为 .prefab 资产，供美术在编辑器中自由定制样式。
    /// 导出后运行时 <see cref="StoryView"/> 会优先用这些 Prefab（Resources/StoryDialogueBoxes/，
    /// 位于工程 Assets/Resources 下，不塞进工具包），找不到才回退内置代码模板。
    /// </summary>
    public static class StoryDialogueBoxPrefabGenerator
    {
        // 放在工程 Assets/Resources 下（而非包内）：美术定制文件留在项目侧，不污染工具包，
        // 且 Resources.Load 为全局搜索，包内代码仍可正常加载。
        private const string Dir = "Assets/Resources/StoryDialogueBoxes";

        [MenuItem("Tools/生成对话框模板 Prefab")]
        public static void Generate()
        {
            EnsureDir();
            Gen("story-line",   StoryBoxTemplates.BuildLineTemplate());
            Gen("story-choice", StoryBoxTemplates.BuildChoiceTemplate());
            Gen("story-end",    StoryBoxTemplates.BuildMessageTemplate(false));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[StoryDialogueBoxPrefabGenerator] 已生成 3 个对话框模板 Prefab → {Dir}\n（美术可直接打开这些 Prefab 改样式；运行时自动加载，无需其他配置）");
        }

        private static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/Resources", "StoryDialogueBoxes");
        }

        private static void Gen(string key, GameObject go)
        {
            // 让 Prefab 是“完整对话框”：补 DialogueBox 组件（Acquire 也会补，但 Prefab 直接带更清晰，美术可检视）。
            if (go.GetComponent<DialogueBox>() == null)
                go.AddComponent<DialogueBox>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{key}.prefab");
            Object.DestroyImmediate(go);
            if (prefab == null)
                Debug.LogError($"[StoryDialogueBoxPrefabGenerator] 生成失败：{key}");
        }
    }
}
#endif
