using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 全局变量资产的 Editor 侧查找/创建与合并工具。
    /// Runtime 层的 <see cref="StoryGlobalVariableAsset"/> 不含任何 UnityEditor API，
    /// 因此涉及 AssetDatabase 的操作集中在此处（仅 Editor 程序集可调用）。
    /// </summary>
    public static class GlobalVariableLookup
    {
        /// <summary>全局变量资产的标准路径（Assets/Story/GlobalVariables/GlobalVariables.asset）。</summary>
        public static string DefaultPath => StoryAssetPaths.GlobalVarPath;

        /// <summary>查找工程中唯一的全局变量资产；不存在则返回 null。优先返回已落在标准路径者。</summary>
        public static StoryGlobalVariableAsset GetAsset()
        {
            var guids = AssetDatabase.FindAssets("t:StoryGlobalVariableAsset");
            if (guids.Length == 0) return null;
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p == DefaultPath)
                    return AssetDatabase.LoadAssetAtPath<StoryGlobalVariableAsset>(p);
            }
            return AssetDatabase.LoadAssetAtPath<StoryGlobalVariableAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>查找，若不存在则在标准路径 <c>Assets/Story/GlobalVariables/GlobalVariables.asset</c> 自动创建并返回。已存在但位置偏离的会被收口回标准路径。</summary>
        public static StoryGlobalVariableAsset GetOrCreate()
        {
            var existing = GetAsset();
            if (existing != null)
            {
                // 迁移防护：已被宿主搬离标准树（如迁往 Addressables 目录）时不拉回（同 StoryAssetOrganizer）。
                string path = AssetDatabase.GetAssetPath(existing);
                if (path != DefaultPath && StoryAssetPaths.IsUnderStoryRoot(path))
                    StoryAssetPaths.MoveAssetToDir(existing, StoryAssetPaths.GlobalVarsDir);
                return existing;
            }
            StoryAssetPaths.EnsureFolder(StoryAssetPaths.GlobalVarsDir);
            var asset = ScriptableObject.CreateInstance<StoryGlobalVariableAsset>();
            AssetDatabase.CreateAsset(asset, DefaultPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }
    }
}
