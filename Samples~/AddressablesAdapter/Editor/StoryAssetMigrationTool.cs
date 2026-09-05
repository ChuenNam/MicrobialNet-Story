using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using MicrobialNet.Story.EditorTools.Settings;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情资产迁移工具（正式小工具，随包 Samples~ 分发）：把 Resources 下的剧情资产**全类目**迁出并标注
    /// Addressable，搬移与标注同一事务完成（迁出同步执行）。幂等可重跑；「撤销迁移」原路搬回（GUID 不变，
    /// 引用无缝还原）。
    ///
    /// <para><b>搬移语义（合并式，两侧可并存）</b>：目标目录已存在时逐子项搬（文件/子目录递归），同名跳过
    /// = 幂等——支持「部分已迁移、Resources 又新增了图」的常态工作流（新图逐张补迁，已迁项不动）。
    /// 搬移前清理目标侧<b>孤儿 meta</b>（目录已空但 .meta 残留会占用路径，令 MoveAsset 拒绝写入）；
    /// 搬完源侧变空壳则连 meta 一起删除。失败项收集错误消息在弹窗显示，不静默。</para>
    ///
    /// <para><b>迁移清单（数据驱动，见 <see cref="BuildMigrationList"/>）</b>——两类消费形态：</para>
    /// <list type="bullet">
    /// <item><b>接缝 key 消费</b>（运行时经 StoryAssetLocator 按键加载）：图（Label "Story/Graphs" 批量）、
    /// 角色（Label "Story/Characters" 批量）、对话框模板（address 同名单资产）、样式（Label
    /// "StoryDialogueBoxStyles" 批量）、生成策略（Label "StorySpawnStrategies" 批量）——键形态与各消费方
    /// 代码逐一对齐，标注后 ChainedAssetLocator 的 primary 直接命中。</item>
    /// <item><b>GUID 依赖消费</b>（被图/节点直接引用）：表格（Story/Tables，节点 tableBinding 持 GUID）、
    /// 本地化表（Story/Localization，每图一张、图持 GUID 依赖）——引用本身不需 key，但**必须搬出 Resources**
    /// （否则作 bundle 资产的隐式依赖会打两份、热更后版本漂移）；顺带标 entry+Label 使其可独立热更。</item>
    /// </list>
    ///
    /// <para><b>刻意不迁的两类（场景直连资产）</b>：全局变量（StoryFlow.globalVariables 序列化字段）与
    /// 打字机配置（StoryView.typingProfile）——场景直接引用构建时冻结进场景/包体，迁移不产生热更价值，
    /// 反而破坏「场景引用即随包」的简单语义。这类资产的正确热更姿势是宿主改为引导期经接缝注入。</para>
    ///
    /// <para><b>引导形态</b>：不挂场景、不碰场景文件——<see cref="AddressablesDemoBoot"/> 为静态引导
    /// （RuntimeInitializeOnLoadMethod BeforeSceneLoad，早于一切 Awake），启停由编译宏
    /// STORY_HOTUPDATE_DEMO 控制（迁移加宏 / 撤销移除宏，各触发一次重编译，属预期）。</para>
    ///
    /// <para><b>搬运方式</b>：<c>AssetDatabase.MoveAsset</c>（GUID 保留，场景/资产引用自动跟随；
    /// StoryGraphWindow 图列表走全工程 FindAssets，不受目录迁移影响）。</para>
    /// </summary>
    internal static class StoryAssetMigrationTool
    {
        /// <summary>一个类目的迁移描述：从 Resources 侧搬往 AddressableStory 侧，并按约定标注。</summary>
        private sealed class MigrationItem
        {
            /// <summary>类目显示名（弹窗明细用）。</summary>
            public string Title;
            /// <summary>迁移候选源目录（可多个：包约定位置 + 用户实际摆放偏差；都不存在 = 跳过搬移仅补标注）。</summary>
            public string[] SourceDirs;
            /// <summary>撤销迁移的搬回目标（统一为包约定位置——归正摆放偏差，编辑器下拉/运行时键的口径）。</summary>
            public string RevertDir;
            /// <summary>AddressableStory 侧目标目录。</summary>
            public string AbDir;
            /// <summary>批量 Label（= 运行时批量键；null = 纯 address 类目）。</summary>
            public string Label;
            /// <summary>FindAssets 类型过滤。</summary>
            public string TypeQuery;
            /// <summary>address 前缀（= 逻辑键空间；条目 address = 前缀 + 相对路径去扩展名）。</summary>
            public string AddressPrefix;
        }

        private const string AbRoot = "Assets/AddressableStory";
        private const string GroupName = "Default Local Group"; // Addressables 初始化自带的标准组（含打包 schema）
        private const string DemoDefine = "STORY_HOTUPDATE_DEMO"; // 静态引导启停宏（加/移除触发一次重编译）

        // ══ 迁移清单（与运行时消费方逐一对齐；新增类目只需在此加一行）════════════
        // SourceDirs 统一读 StoryKeySpaceDirsSettings（包约定默认 ∪ 业务自定义），业务目录无需改代码即可纳入迁移；
        // 撤销迁移仍归正到包约定位置（RevertDir 固定）。

        private static List<MigrationItem> BuildMigrationList() => new List<MigrationItem>
        {
            new MigrationItem { Title = "剧情图",   SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("Story/Graphs"), RevertDir = "Assets/Resources/Story/Graphs", AbDir = AbRoot + "/Graphs", Label = "Story/Graphs", TypeQuery = "t:StoryGraphAsset", AddressPrefix = "Story/Graphs" },
            new MigrationItem { Title = "角色",     SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("Story/Characters"), RevertDir = "Assets/Resources/Story/Characters", AbDir = AbRoot + "/Characters", Label = "Story/Characters", TypeQuery = "t:StoryCharacterAsset", AddressPrefix = "Story/Characters" },
            new MigrationItem { Title = "表格",     SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("Story/Tables"), RevertDir = "Assets/Resources/Story/Tables", AbDir = AbRoot + "/Tables", Label = "Story/Tables", TypeQuery = "t:StoryTableAsset", AddressPrefix = "Story/Tables" },
            new MigrationItem { Title = "本地化表", SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("Story/Localization"), RevertDir = "Assets/Resources/Story/Localization", AbDir = AbRoot + "/Localization", Label = "Story/Localization", TypeQuery = "t:StoryLocalizationTable", AddressPrefix = "Story/Localization" },
            new MigrationItem { Title = "对话框模板", SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("StoryDialogueBoxes"), RevertDir = "Assets/Resources/StoryDialogueBoxes", AbDir = AbRoot + "/StoryDialogueBoxes", Label = null, TypeQuery = "t:Prefab", AddressPrefix = "StoryDialogueBoxes" },
            new MigrationItem { Title = "对话框样式", SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("StoryDialogueBoxStyles"), RevertDir = "Assets/Resources/StoryDialogueBoxStyles", AbDir = AbRoot + "/StoryDialogueBoxStyles", Label = "StoryDialogueBoxStyles", TypeQuery = "t:DialogueBoxStyleAsset", AddressPrefix = "StoryDialogueBoxStyles" },
            new MigrationItem { Title = "生成策略", SourceDirs = StoryKeySpaceDirsSettings.GetSourceDirs("StorySpawnStrategies"), RevertDir = "Assets/Resources/StorySpawnStrategies", AbDir = AbRoot + "/StorySpawnStrategies", Label = "StorySpawnStrategies", TypeQuery = "t:DialogueBoxSpawnStrategyAsset", AddressPrefix = "StorySpawnStrategies" },
        };

        // ══ 一键迁移 ═══════════════════════════════════════

        [MenuItem("MicrobialNet/Story/资产迁移/一键迁移（Story 资产全类目 → Addressables）")]
        public static void Migrate()
        {
            // 1) Addressables 工程配置（没有则创建标准配置：含 Default Local Group 与打包 schema）
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                settings = AddressableAssetSettings.Create(
                    "Assets/AddressableAssetsData", "AddressableAssetSettings", true, true);
                AddressableAssetSettingsDefaultObject.Settings = settings;
            }
            var group = settings.FindGroup(GroupName);
            if (group == null)
            {
                EditorUtility.DisplayDialog("资产迁移",
                    "未找到「" + GroupName + "」资产组。\n请先打开一次 Addressables Groups 窗口" +
                    "（Window → Asset Management → Addressables → Groups）让它完成初始化，再重试本工具。", "知道了");
                return;
            }

            // 2) 迁出同步执行：搬移（合并式）+ 标注（同一事务，逐类目幂等）
            var list = BuildMigrationList();
            var detail = new StringBuilder();
            var errors = new List<string>();
            int movedFiles = 0, tagged = 0;
            foreach (var item in list)
            {
                // Label 预注册（Label 语义类目）
                if (item.Label != null && !settings.GetLabels().Contains(item.Label))
                    settings.AddLabel(item.Label);

                int itemMoved = 0;
                foreach (var srcDir in item.SourceDirs) // 多候选源：约定位置 + 摆放偏差位置都扫
                    itemMoved += MoveContents(srcDir, item.AbDir, errors); // 合并搬移：新项补迁、已迁不动
                movedFiles += itemMoved;
                int n = TagEntries(settings, group, item);
                tagged += n;
                detail.AppendLine($"  {item.Title}：Ab 侧标注 {n} 项，本次补迁 {itemMoved} 项");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("资产迁移完成",
                $"本次搬移资产：{movedFiles} 项（合并式：新项补迁、已迁不动）；Ab 侧共标注 {tagged} 项\n\n" +
                detail.ToString() +
                "  刻意不迁：全局变量、打字机配置（场景直连资产，冻结进场景，迁移无热更价值）\n" +
                (errors.Count > 0
                    ? "\n⚠ 搬移失败项（路径被占用或 IO 错误，处理后重跑本工具即补迁）：\n" + string.Join("\n", errors.Take(10)) + "\n"
                    : "") +
                $"引导：静态引导已启用（编译宏 {DemoDefine}，Unity 会自动重编译一次）\n\n" +
                "下一步：Addressables Groups → Build → New Build → Default Build Script，\n" +
                "Play Mode Script 切「Use Existing Build」后 Play 即走热更通道（Console 看 [热更演示] 日志）。", "好的");

            // 3) 最后一步：加编译宏启用静态引导（触发一次重编译，必须放弹窗之后）
            SetDefine(true);
        }

        // ══ 撤销迁移 ═══════════════════════════════════════

        [MenuItem("MicrobialNet/Story/资产迁移/撤销迁移（搬回 Resources）")]
        public static void Revert()
        {
            if (!Directory.Exists(AbRoot))
            {
                // 目录虽无，宏可能还在（上轮撤销中途失败等）——顺手关掉再提示
                bool defineWasOn = IsDefineOn();
                if (defineWasOn) SetDefine(false);
                EditorUtility.DisplayDialog("资产迁移",
                    "未发现迁移目录（" + AbRoot + "），" + (defineWasOn ? "已顺带关闭热更引导宏。" : "无需撤销。"), "好的");
                return;
            }

            // 1) 资产搬回 Resources（合并式搬回：Resources 侧已存在的同名项不动）+ 清 entry
            //    （搬回 Resources 的资产若留 entry 会打双份，且下次构建 entry 指向已失效路径）
            var list = BuildMigrationList();
            var errors = new List<string>();
            int removed = 0, movedBack = 0;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Story");
            foreach (var item in list)
            {
                movedBack += MoveContents(item.AbDir, item.RevertDir, errors); // 合并搬回 + 孤儿 meta 清理（统一归正到包约定位置）
                if (settings != null)
                    removed += RemoveEntries(settings, item.RevertDir, item.TypeQuery);
            }

            // 2) 迁移根目录清空后删除
            TryDeleteEmptyFolder(AbRoot);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("资产迁移已撤销",
                $"已搬回 {movedBack} 项资产并移除 {removed} 条 Addressable 标注；热更引导宏已移除（Unity 会自动重编译一次，" +
                "资产通道回到 Resources 默认）。\n" +
                (errors.Count > 0
                    ? "\n⚠ 搬移失败项（处理后重跑撤销即补搬）：\n" + string.Join("\n", errors.Take(10)) + "\n"
                    : "") +
                "建议把 Play Mode Script 切回「Use Asset Database」恢复普通编辑器工作流。", "好的");

            // 3) 最后一步：移除编译宏关闭静态引导（触发一次重编译，必须放弹窗之后）
            SetDefine(false);
        }

        // ══ 内部工具 ═══════════════════════════════════════

        private static bool IsDefineOn()
            => PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone)
                   .Split(';').Select(d => d.Trim()).Contains(DemoDefine);

        /// <summary>加/移除引导宏（改 PlayerSettings，触发一次域重载重编译）。幂等。</summary>
        private static void SetDefine(bool enable)
        {
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = current.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
            bool has = defines.Contains(DemoDefine);
            if (enable && !has) defines.Add(DemoDefine);
            else if (!enable && has) defines.Remove(DemoDefine);
            else return; // 已是目标状态，不触发无谓重编译
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }

        /// <summary>
        /// 为一个类目标注全部资产：address = 前缀 + 相对 AbDir 路径（去扩展名，即逻辑键）；
        /// Label 类目顺带打同名 Label（运行时批量键）。幂等（CreateOrMoveEntry 重算覆盖）。
        /// </summary>
        private static int TagEntries(AddressableAssetSettings settings, AddressableAssetGroup group, MigrationItem item)
        {
            if (!AssetDatabase.IsValidFolder(item.AbDir)) return 0;
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets(item.TypeQuery, new[] { item.AbDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var rel = path.Substring(item.AbDir.Length + 1);
                rel = rel.Substring(0, rel.Length - Path.GetExtension(path).Length);
                var entry = settings.CreateOrMoveEntry(guid, group);
                entry.address = item.AddressPrefix + "/" + rel;
                if (item.Label != null && !entry.labels.Contains(item.Label))
                    entry.labels.Add(item.Label);
                count++;
            }
            return count;
        }

        private static int RemoveEntries(AddressableAssetSettings settings, string folder, string typeQuery)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets(typeQuery, new[] { folder }))
                if (settings.RemoveAssetEntry(guid))
                    count++;
            return count;
        }

        // ══ 合并式目录搬移（核心）══════════════════════════

        /// <summary>
        /// 把 <paramref name="src"/> 目录内容搬入 <paramref name="dst"/>（合并语义）：
        /// 子目录——目标同名不存在则整树 MoveAsset（保 GUID），存在则递归合并；
        /// 文件——目标同名已存在则跳过（幂等），否则 MoveAsset。搬前清理 dst 侧孤儿 meta
        /// （目录不存在但 .meta 残留 → Unity 视路径被占用，MoveAsset 会失败）。搬完 src 无资产则连壳删除。
        /// 返回实际搬入的资产数；失败项以「路径 —— 错误消息」记入 errors（不静默）。
        /// </summary>
        private static int MoveContents(string src, string dst, List<string> errors)
        {
            if (string.IsNullOrEmpty(src) || !AssetDatabase.IsValidFolder(src)) return 0;
            TryDeleteOrphanMeta(dst); // 孤儿 meta 占位会让 EnsureFolder/MoveAsset 失败
            EnsureFolder(dst);

            int moved = 0;
            // 子目录：整树搬（目标不存在）或递归合并（目标存在）
            foreach (var subDir in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(subDir);
                string subDst = dst + "/" + name;
                if (!AssetDatabase.IsValidFolder(subDst))
                {
                    string err = AssetDatabase.MoveAsset(ToAssetPath(subDir), subDst);
                    if (string.IsNullOrEmpty(err)) moved += CountAssets(subDst);
                    else errors.Add($"{subDir} —— {err}");
                }
                else
                {
                    moved += MoveContents(ToAssetPath(subDir), subDst, errors);
                }
            }
            // 文件（跳过 .meta）：同名跳过 = 幂等
            foreach (var file in Directory.GetFiles(src))
            {
                if (file.EndsWith(".meta")) continue;
                string name = Path.GetFileName(file);
                string fileDst = dst + "/" + name;
                if (File.Exists(fileDst) || AssetDatabase.LoadAssetAtPath<Object>(fileDst) != null) continue;
                string err = AssetDatabase.MoveAsset(ToAssetPath(file), fileDst);
                if (string.IsNullOrEmpty(err)) moved++;
                else errors.Add($"{file} —— {err}");
            }

            TryDeleteEmptyFolder(src); // 源侧搬空后连壳（含空子目录与 meta）删除
            return moved;
        }

        /// <summary>删目标侧孤儿 meta（目录不存在但 .meta 在 → 路径被占用，后续 MoveAsset 会失败）。</summary>
        private static void TryDeleteOrphanMeta(string dir)
        {
            string meta = dir + ".meta";
            if (!Directory.Exists(dir) && File.Exists(meta))
            {
                File.Delete(meta);
                AssetDatabase.Refresh();
            }
        }

        private static int CountAssets(string folder)
            => AssetDatabase.IsValidFolder(folder) ? AssetDatabase.FindAssets("", new[] { folder }).Length : 0;

        /// <summary>目录内已无任何资产（含子目录）则整目录删除（连同 meta 与空子目录）。</summary>
        private static void TryDeleteEmptyFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            var anyChild = AssetDatabase.FindAssets("", new[] { folder });
            if (anyChild.Length == 0) AssetDatabase.DeleteAsset(folder);
        }

        /// <summary>物理路径 → 资产路径（反斜杠归一为正斜杠）。</summary>
        private static string ToAssetPath(string p) => p.Replace('\\', '/');

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
