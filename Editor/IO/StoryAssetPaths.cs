using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情资产固定目录布局（编辑器强制行为）。
    /// 所有剧情相关资产统一收拢到 Assets/Resources/Story 下（置于 Resources 以便运行时 Resources.Load / 批量注册扫描），含：
    ///   Graphs/          剧情图（再按分组 chapter 建子文件夹；空分组→未分组）
    ///   Characters/      角色
    ///   GlobalVariables/ 全局变量（GlobalVariables.asset）
    ///   Localization/    本地化主表（再按「图名」建子文件夹，每张图一个 <图名>.l10ntable.asset）
    /// 新建 / 迁移资产一律经此计算路径，保证「三 tab + 本地化 = 同根目录」始终成立。
    /// 路径规则集中在这一处，便于统一修改根目录名。
    /// </summary>
    public static class StoryAssetPaths
    {
        public const string RootDir = "Assets/Resources/Story";
        public const string GraphsDir = RootDir + "/Graphs";
        public const string CharactersDir = RootDir + "/Characters";
        public const string GlobalVarsDir = RootDir + "/GlobalVariables";
        public const string GlobalVarPath = GlobalVarsDir + "/GlobalVariables.asset";
        public const string LocalizationDir = RootDir + "/Localization";
        public const string TablesDir = RootDir + "/Tables";
        public const string Ungrouped = "未分组";

        /// <summary>按分组名得到剧情图子文件夹；空/非法分组归未分组。</summary>
        public static string GetGroupDir(string chapter)
        {
            string name = Sanitize(chapter);
            if (string.IsNullOrEmpty(name)) name = Ungrouped;
            return GraphsDir + "/" + name;
        }

        /// <summary>
        /// 判定资产路径是否位于标准剧情目录树（<see cref="RootDir"/>）内。供「固定布局收口」类逻辑做
        /// <b>迁移防护</b>：资产已被宿主搬离标准树（如迁往 Addressables 目录做热更）时，整理/分组收口
        /// 应尊重当前位置跳过拉回——否则每次域名重载（Organizer 自动整理）或窗口保存都会把迁移走的
        /// 资产搬回 Resources，并触发 Addressables「移入 Resources 自动清 entry」逆转迁移。
        /// </summary>
        public static bool IsUnderStoryRoot(string assetPath)
            => !string.IsNullOrEmpty(assetPath)
               && assetPath.StartsWith(RootDir + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>按图名得到本地化主表所在子文件夹（与图同名）；返回 <see cref="LocalizationDir"/>/&lt;图名&gt;/（无则建议 EnsureFolder 新建）。</summary>
        public static string GetLocalizationDir(string graphName)
        {
            string name = Sanitize(graphName);
            if (string.IsNullOrEmpty(name)) name = "未命名图";
            return LocalizationDir + "/" + name;
        }

        /// <summary>按图名得到本地化主表资产路径：<see cref="LocalizationDir"/>/&lt;图名&gt;/&lt;图名&gt;.l10ntable.asset。</summary>
        public static string GetLocalizationTablePath(string graphName)
        {
            string name = Sanitize(graphName);
            if (string.IsNullOrEmpty(name)) name = "未命名图";
            return GetLocalizationDir(name) + "/" + name + ".l10ntable.asset";
        }

        /// <summary>枚举 Graphs/ 下所有现有分组（子文件夹名），供新建对话框的可编辑下拉使用。</summary>
        public static List<string> GetExistingGroups()
        {
            var list = new List<string>();
            if (!AssetDatabase.IsValidFolder(GraphsDir)) return list;
            foreach (var g in AssetDatabase.GetSubFolders(GraphsDir))
                list.Add(Path.GetFileName(g));
            return list;
        }

        /// <summary>清理 Graphs/ 下所有空分组文件夹（无子文件夹且无任何资产）。含「未分组」，空则一并删除，需要时由 EnsureFolder 重建。</summary>
        public static void PruneEmptyGroupFolders()
        {
            if (!AssetDatabase.IsValidFolder(GraphsDir)) return;
            foreach (var sub in AssetDatabase.GetSubFolders(GraphsDir))
            {
                var subfolders = AssetDatabase.GetSubFolders(sub);
                var assets = AssetDatabase.FindAssets("", new[] { sub });
                if (subfolders.Length == 0 && assets.Length == 0)
                    AssetDatabase.DeleteAsset(sub);
            }
        }

        /// <summary>确保目录（含中间层级）存在。返回是否实际新建了文件夹。</summary>
        public static bool EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return false;
            var parts = dir.Split('/');
            string cur = parts[0];
            bool created = false;
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(cur, parts[i]);
                    created = true;
                }
                cur = next;
            }
            return created;
        }

        /// <summary>把资产移动到目标目录（文件名不变）。已在该目录则跳过。返回最终路径。</summary>
        public static string MoveAssetToDir(UnityEngine.Object asset, string targetDir)
        {
            if (asset == null) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return null;
            // 新建文件夹后需 Refresh，否则 MoveAsset 尚未识别目标目录会静默失败
            bool created = EnsureFolder(targetDir);
            if (created) AssetDatabase.Refresh();
            string fileName = Path.GetFileName(path);
            string target = targetDir + "/" + fileName;
            if (path == target) return path;
            string result = AssetDatabase.MoveAsset(path, target);
            if (string.IsNullOrEmpty(result))
            {
                // 兜底：强制刷新后重试一次，处理目标文件夹刚创建等时序问题
                AssetDatabase.Refresh();
                result = AssetDatabase.MoveAsset(path, target);
                if (string.IsNullOrEmpty(result))
                {
                    Debug.LogWarning($"[Story] 移动资产失败：{path} → {target}");
                    return path;
                }
            }
            return result;
        }

        /// <summary>去除文件系统非法字符（保留空格与中文），并裁剪首尾空白。</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // 移除文件系统非法字符；保留空格与中文（利于中文分组名）
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                if (Array.IndexOf(invalid, c) < 0) sb.Append(c);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 把操作系统绝对路径转为「项目相对路径」（相对工程根目录，即 Assets 的父目录），
        /// 形如 <c>Packages/com.x/.../foo.xlsx</c> 或 <c>Assets/.../foo.xlsx</c>。
        /// 目的：让 <see cref="StoryTableAsset.sourceFilePath"/> 等引用在换机 / 克隆 / 移动工程后依然可解析，
        /// 避免存绝对路径导致「重新导入并同步」失效。文件位于工程外时无法相对化，保留（归一化后的）绝对路径。
        /// </summary>
        public static string ToProjectRelative(string absPath)
        {
            if (string.IsNullOrEmpty(absPath)) return absPath;
            string norm = absPath.Replace('\\', '/').Trim();
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
                if (string.Equals(norm, projectRoot, StringComparison.OrdinalIgnoreCase)
                    || norm.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return norm.Substring(projectRoot.Length).TrimStart('/');
                }
            }
            catch { /* Application.dataPath 在极少数宿主环境不可用：退回归一化绝对值 */ }
            return norm;
        }

        /// <summary>
        /// 把存储的源文件路径（可能是项目相对路径或绝对路径）解析为当前存在的绝对路径。
        /// 解析顺序：① 直接作为绝对路径存在则用之；② 当作项目相对路径拼回工程根再试。
        /// 两者皆不可用时返回 null（调用方应给出「源文件未找到」的清晰提示，而非静默跳过）。
        /// </summary>
        public static string ResolveSourcePath(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            string norm = stored.Replace('\\', '/');
            // ① 绝对路径且存在
            try
            {
                if (Path.IsPathRooted(norm) && File.Exists(norm)) return norm;
            }
            catch { /* 路径非法等：进入下一步 */ }
            // ② 项目相对路径 → 拼回工程根
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
                string full = projectRoot + "/" + norm.TrimStart('/');
                if (File.Exists(full)) return full;
            }
            catch { /* 同上 */ }
            return null;
        }
    }
}
