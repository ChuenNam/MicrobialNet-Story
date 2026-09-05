using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情图 JSON 的单个版本迁移步骤：把 FromVersion 的文档升级为 ToVersion（就地修改 JObject）。
    /// 注册进 <see cref="StoryJsonMigrator"/> 后，<see cref="StoryJsonExporter.Import"/> 自动按链式顺序执行。
    /// </summary>
    internal interface ISerializationMigrator
    {
        /// <summary>源版本号（与导出 DTO 的 version 字段比对）。</summary>
        string FromVersion { get; }

        /// <summary>目标版本号。</summary>
        string ToVersion { get; }

        /// <summary>就地升级文档（可修改 nodes / edges / tableRows 等任意 JToken）。</summary>
        void Apply(JObject root);
    }

    /// <summary>
    /// 剧情图 JSON 迁移链（version → migrator）+ 节点类型别名表。
    ///
    /// 动机：JSON 以 TypeNameHandling.Auto 记录节点 $type 全名——节点类改名 / 移动命名空间后，
    /// 旧文件（导出物 / 自动保存快照 / 关闭回滚基线）会反序列化失败；导出格式演进也需要可组合的升级路径。
    ///
    /// 用法（节点类改名时，在编辑器初始化处注册一次，旧文件导入 / 快照恢复自动归一）：
    /// <code>
    /// StoryJsonMigrator.RegisterTypeAlias(
    ///     "MicrobialNet.Story.Nodes.OldName, com.microbialnet.story",
    ///     "MicrobialNet.Story.Nodes.NewName, com.microbialnet.story");
    /// </code>
    /// 版本迁移：实现 <see cref="ISerializationMigrator"/>（From→To 一步）并 <see cref="RegisterStep"/>；
    /// Import 按版本链自动衔接多步。未注册任何别名 / 步骤时 <see cref="Migrate"/> 零开销直通。
    /// </summary>
    internal static class StoryJsonMigrator
    {
        private static readonly Dictionary<string, string> TypeAliases =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly List<ISerializationMigrator> Steps = new List<ISerializationMigrator>();

        /// <summary>是否已注册任何别名/迁移步骤（测试用守卫：未注册时 Migrate 零开销直通）。</summary>
        internal static bool HasRegistrations => TypeAliases.Count > 0 || Steps.Count > 0;

        /// <summary>注册节点类型别名：$type 值（"类型全名, 程序集"）按「类型全名」部分匹配并改写，程序集名保留原值。</summary>
        public static void RegisterTypeAlias(string oldFullTypeName, string newFullTypeName)
        {
            if (string.IsNullOrWhiteSpace(oldFullTypeName) || string.IsNullOrWhiteSpace(newFullTypeName)) return;
            TypeAliases[NormalizeTypeName(oldFullTypeName)] = NormalizeTypeName(newFullTypeName);
        }

        /// <summary>注册版本迁移步骤（From→To 一步）。同 From/To 重复注册幂等跳过。</summary>
        public static void RegisterStep(ISerializationMigrator step)
        {
            if (step == null) return;
            if (Steps.Any(s => s.FromVersion == step.FromVersion && s.ToVersion == step.ToVersion)) return;
            Steps.Add(step);
        }

        /// <summary>
        /// 把任意历史版本的 JSON 归一为 <paramref name="currentVersion"/>：
        /// ① 缺版本 / 已是当前版本 → 跳过版本链；② 按注册步骤链式升级；③ 递归改写 $type 别名。
        /// 未注册任何别名 / 步骤时原样返回（零开销直通）。
        /// </summary>
        public static string Migrate(string json, string currentVersion)
        {
            if (string.IsNullOrEmpty(json)) return json;
            if (TypeAliases.Count == 0 && Steps.Count == 0) return json;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception) { return json; /* 非法 JSON 交由上层反序列化报明确错误 */ }

            var version = root["version"]?.ToString();
            if (!string.IsNullOrEmpty(version) && version != currentVersion)
                ApplyVersionChain(root, version, currentVersion);
            ApplyTypeAliases(root);
            return root.ToString();
        }

        // —— 版本链：从文档当前版本出发，找 FromVersion 匹配的步骤执行并推进，直到到达目标版本。 ——
        private static void ApplyVersionChain(JObject root, string fromVersion, string targetVersion)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { fromVersion };
            var current = fromVersion;
            // 步骤数即链长上限（每步最多消费一次），防注册环路死循环。
            int budget = Steps.Count + 1;
            while (current != targetVersion && budget-- > 0)
            {
                var step = Steps.FirstOrDefault(s => s.FromVersion == current);
                if (step == null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[StoryJsonMigrator] 未注册从版本「{current}」到「{targetVersion}」的迁移步骤，按当前格式直接尝试解析。");
                    return;
                }
                step.Apply(root);
                current = step.ToVersion;
                if (!visited.Add(current))
                {
                    UnityEngine.Debug.LogWarning("[StoryJsonMigrator] 版本迁移链检测到环路，已中止。");
                    return;
                }
            }
        }

        // —— $type 别名改写：递归遍历整棵 JToken 树，命中「类型全名」部分即替换。 ——
        private static void ApplyTypeAliases(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    var typeProp = obj.Property("$type");
                    if (typeProp != null && typeProp.Value.Type == JTokenType.String)
                    {
                        var raw = (string)typeProp.Value;
                        int comma = raw.IndexOf(',');
                        var typeName = NormalizeTypeName(comma >= 0 ? raw.Substring(0, comma) : raw);
                        if (TypeAliases.TryGetValue(typeName, out var mapped))
                            typeProp.Value = comma >= 0 ? mapped + raw.Substring(comma) : mapped;
                    }
                    foreach (var prop in obj.Properties()) ApplyTypeAliases(prop.Value);
                    break;
                case JArray arr:
                    foreach (var item in arr) ApplyTypeAliases(item);
                    break;
            }
        }

        private static string NormalizeTypeName(string s) => s?.Trim() ?? string.Empty;
    }
}
