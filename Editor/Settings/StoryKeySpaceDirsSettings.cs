using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Settings
{
    /// <summary>
    /// 剧情各键空间（剧情图 / 角色 / 表格 / 本地化 / 对话框模板 / 样式 / 生成策略）的**源目录**全局配置。
    ///
    /// 设计动机：此前各键空间的"读取目录"散落在编辑器下拉过滤与迁移工具 <c>SourceDirs</c> 的硬编码里，
    /// 业务侧把资产放自己的目录会导致"编辑器/迁移识别不到"。本类把目录配置收敛为统一设置：
    /// - <see cref="DefaultDirs"/>：包约定目录（只读，也是迁移撤销的归正目标，不可改）；
    /// - 自定义目录：业务侧补充的来源目录（EditorPrefs JSON 持久化），随设置窗口「资源目录」Tab 编辑；
    /// - <see cref="GetSourceDirs"/> = 默认 ∪ 自定义，供迁移工具 SourceDirs 等消费。
    ///
    /// 【程序集定位】位于独立 asmdef（com.microbialnet.story.Settings）——迁移工具（Samples 侧）经引用本程序集调用，
    /// 设置窗口（同程序集）直接使用；不依赖包主体，依赖缺失时仍可用。
    /// </summary>
    public static class StoryKeySpaceDirsSettings
    {
        /// <summary>键空间注册表：key 与迁移工具 AddressPrefix / 运行时批量键同口径（AddressPrefix 各键空间唯一）。</summary>
        public static readonly (string key, string title)[] KeySpaces =
        {
            ("Story/Graphs",          "剧情图"),
            ("Story/Characters",      "角色"),
            ("Story/Tables",          "剧情表"),
            ("Story/Localization",    "本地化表"),
            ("StoryDialogueBoxes",    "对话框模板"),
            ("StoryDialogueBoxStyles","对话框样式"),
            ("StorySpawnStrategies",  "生成策略"),
        };

        /// <summary>EditorPrefs 键（v1 版本号防未来结构变更）。</summary>
        private const string PrefsKey = "com.microbialnet.story.keySpaceDirs.v1";

        /// <summary>包约定源目录（只读；同时是迁移撤销的归正目标）。与迁移工具撤销口径一致。</summary>
        public static string[] DefaultDirs(string key) => key switch
        {
            "Story/Graphs"           => new[] { "Assets/Resources/Story/Graphs" },
            "Story/Characters"       => new[] { "Assets/Resources/Story/Characters" },
            "Story/Tables"           => new[] { "Assets/Resources/Story/Tables" },
            "Story/Localization"     => new[] { "Assets/Resources/Story/Localization" },
            "StoryDialogueBoxes"     => new[] { "Assets/Resources/StoryDialogueBoxes" },
            "StoryDialogueBoxStyles" => new[] { "Assets/Resources/StoryDialogueBoxStyles" },
            "StorySpawnStrategies"   => new[] { "Assets/Resources/StorySpawnStrategies", "Assets/Resources/Story/StorySpawnStrategies" },
            _ => Array.Empty<string>(),
        };

        /// <summary>该键空间全部来源目录 = 包约定默认 ∪ 业务自定义（去重、去空，保留顺序）。迁移工具 SourceDirs 消费此值。</summary>
        public static string[] GetSourceDirs(string key)
            => DefaultDirs(key).Concat(GetCustomDirs(key))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .ToArray();

        // ── 自定义目录（EditorPrefs JSON）────────────────────

        /// <summary>取业务自定义目录列表（可直接修改后 <see cref="SaveCustomDirs"/> 持久化）。</summary>
        public static List<string> GetCustomDirs(string key)
        {
            var all = LoadAll();
            return all.TryGetValue(key, out var l) ? l : new List<string>();
        }

        /// <summary>写回某键空间的自定义目录并持久化（空列表也会保存，用于清空）。</summary>
        public static void SaveCustomDirs(string key, List<string> dirs)
        {
            var all = LoadAll();
            all[key] = dirs ?? new List<string>();
            Persist(all);
        }

        /// <summary>读取全部键空间自定义目录（JSON 反序列化；无存档返回空表）。</summary>
        public static Dictionary<string, List<string>> LoadAll()
        {
            var result = new Dictionary<string, List<string>>();
            try
            {
                var raw = EditorPrefs.GetString(PrefsKey, string.Empty);
                if (string.IsNullOrEmpty(raw)) return result;
                var root = JsonUtility.FromJson<KeySpaceDirsRoot>(raw);
                if (root?.entries == null) return result;
                foreach (var e in root.entries)
                    if (e != null && !string.IsNullOrEmpty(e.key))
                        result[e.key] = e.dirs ?? new List<string>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StoryKeySpaceDirsSettings] 读取自定义目录设置失败，已按空处理：{ex.Message}");
            }
            return result;
        }

        private static void Persist(Dictionary<string, List<string>> all)
        {
            var root = new KeySpaceDirsRoot
            {
                entries = all.Select(kv => new KeySpaceDirsEntry { key = kv.Key, dirs = kv.Value }).ToList(),
            };
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(root));
        }

        [Serializable]
        private sealed class KeySpaceDirsRoot
        {
            public List<KeySpaceDirsEntry> entries = new List<KeySpaceDirsEntry>();
        }

        [Serializable]
        private sealed class KeySpaceDirsEntry
        {
            public string key;
            public List<string> dirs = new List<string>();
        }
    }
}
