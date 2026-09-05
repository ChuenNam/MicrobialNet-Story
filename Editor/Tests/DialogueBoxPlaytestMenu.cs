using MicrobialNet.Story.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 一键在当前场景生成 <see cref="DialogueBoxPlaytest"/> 物体（顺带补 EventSystem）。
    /// 该运行时组件位于 UI 程序集（非 Editor），故可被正常挂载；本菜单仅负责生成，保留在 Editor 程序集。
    /// 生成后点 Play，在 Game 视图左上角点击按钮验证对话框系统。
    /// </summary>
    public static class DialogueBoxPlaytestMenu
    {
        [MenuItem("MicrobialNet/Story/测试/对话框系统 Playtest")]
        public static void SetupPlaytest()
        {
            if (UnityEngine.Object.FindObjectOfType<DialogueBoxPlaytest>() != null)
            {
                Debug.Log("[DialogueBoxPlaytest] 场景已存在 Playtest 物体，点 Play 后看 Game 视图左上角按钮。");
                return;
            }
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            var go = new GameObject("DialogueBoxPlaytest");
            go.AddComponent<DialogueBoxPlaytest>();
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[DialogueBoxPlaytest] 已生成 Playtest 物体。点 Play，在 Game 视图左上角点击按钮验证对话框系统。");
        }
    }
}
