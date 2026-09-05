using System.Collections.Generic;
using System.Linq;
using MicrobialNet.Story;
using MicrobialNet.Story.EditorTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Validation
{
    /// <summary>
    /// 构建期校验门禁（对应需求 G8）：打包前扫描工程内全部剧情图资产，逐张跑 StoryValidator；
    /// 若存在 Error 级问题则中止构建，并在 Console 逐条列出，确保断链 / 空文本等质量问题不进包。
    /// 实现 IPreprocessBuildWithReport，随编辑器自动生效，无需手动挂载。
    /// </summary>
    public class StoryBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var assets = AssetDatabase.FindAssets("t:StoryGraphAsset")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                // 排除 Sample/ 下的示例与测试资产：它们多为开发期演示/故意不完整的图，
                // 只是未引用的数据资产，进包无害，不应阻塞生产构建。
                .Where(p => !p.Replace('\\', '/').Contains("/Sample/"))
                .Select(p => AssetDatabase.LoadAssetAtPath<StoryGraphAsset>(p))
                .Where(a => a != null)
                .ToList();
            if (assets.Count == 0) return;

            var errors = new List<string>();
            foreach (var a in assets)
            {
                var model = new StoryGraphModel(a);
                model.SyncUsedCharacters();
                var issues = StoryValidator.Validate(model);
                foreach (var iss in issues)
                    if (iss.Severity == ValidationSeverity.Error)
                        errors.Add($"[{a.name}] {iss.Message}");
            }

            if (errors.Count > 0)
            {
                foreach (var e in errors)
                    Debug.LogError("[Story] 剧情图校验错误：" + e);
                throw new BuildFailedException(
                    $"[Story] 剧情图校验未通过，已阻止打包（共 {errors.Count} 个错误）。详见 Console。");
            }
        }
    }
}
