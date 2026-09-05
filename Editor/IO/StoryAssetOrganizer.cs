using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 一次性 / 按需整理：把工程里散落的剧情相关资产移动到固定目录结构（Assets/Story）。
    /// 已就位的跳过；整体幂等，可重复运行。编辑器加载时自动跑一次以落实「固定布局」行为。
    /// </summary>
    public static class StoryAssetOrganizer
    {
        /// <summary>整理全部剧情相关资产，返回摘要；仅在确有移动时才输出日志。</summary>
        public static string OrganizeAll()
        {
            StoryAssetPaths.EnsureFolder(StoryAssetPaths.GraphsDir);
            StoryAssetPaths.EnsureFolder(StoryAssetPaths.CharactersDir);
            StoryAssetPaths.EnsureFolder(StoryAssetPaths.GlobalVarsDir);

            int movedGraphs = 0, movedChars = 0, movedGlobals = 0;

            // 剧情图 → Graphs/{chapter}/（迁移防护：已搬离标准树的图不拉回——被宿主迁往
            // Addressables 等目录做热更的资产，拉回会触发「移入 Resources 自动清 entry」逆转迁移）
            foreach (var guid in AssetDatabase.FindAssets("t:StoryGraphAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                string before = AssetDatabase.GetAssetPath(asset);
                if (!StoryAssetPaths.IsUnderStoryRoot(before)) continue;
                string chapter = asset.meta != null ? asset.meta.chapter : "";
                string after = StoryAssetPaths.MoveAssetToDir(asset, StoryAssetPaths.GetGroupDir(chapter));
                if (before != after) movedGraphs++;
            }

            // 角色 → Characters/（同上迁移防护）
            foreach (var guid in AssetDatabase.FindAssets("t:StoryCharacterAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<StoryCharacterAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                string before = AssetDatabase.GetAssetPath(asset);
                if (!StoryAssetPaths.IsUnderStoryRoot(before)) continue;
                string after = StoryAssetPaths.MoveAssetToDir(asset, StoryAssetPaths.CharactersDir);
                if (before != after) movedChars++;
            }

            // 全局变量 → GlobalVariables/（同时存在多个则统一收口到标准路径）
            var g = GlobalVariableLookup.GetAsset();
            if (g != null)
            {
                string before = AssetDatabase.GetAssetPath(g);
                string after = StoryAssetPaths.MoveAssetToDir(g, StoryAssetPaths.GlobalVarsDir);
                if (before != after) movedGlobals++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            StoryAssetPaths.PruneEmptyGroupFolders(); // 整理后顺手清理空分组文件夹

            string summary = $"整理完成：移动 剧情图 {movedGraphs} 项、角色 {movedChars} 项、全局变量 {movedGlobals} 项 → Assets/Story";
            if (movedGraphs + movedChars + movedGlobals > 0)
                Debug.Log("[Story] " + summary);
            return summary;
        }

        /// <summary>编辑器加载时自动整理一次，落实固定布局（幂等，已就位则无操作）。</summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void AutoOrganizeOnLoad()
        {
            // 仅编辑器环境、非批量模式时执行，避免 CI / 命令行导入阶段干扰。
            if (Application.isBatchMode) return;
            OrganizeAll();
        }
    }
}
