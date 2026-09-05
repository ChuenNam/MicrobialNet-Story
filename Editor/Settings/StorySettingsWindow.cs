using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MicrobialNet.Story.EditorTools.Settings
{
    /// <summary>
    /// 工具包设置窗口（模仿 Unity Project Settings 的官方风格：左侧深色 Tab 栏 + 选中蓝底高亮，右侧标题/分段/字段行）。
    ///
    /// 【独立程序集设计】本窗口位于独立 asmdef（com.microbialnet.story.Settings，不引用包内其它程序集、不引用 TextMeshPro/uGUI），
    /// 因此当 TMP/uGUI 等依赖缺失导致包主体编译失败时，本窗口（及其菜单项、导入后自动打开逻辑）**仍然可用**，
    /// 是修复「依赖缺失导致工具包无法使用」的入口。
    /// 【TMP 功能反射实现】TMP Tab（全局字体）经 <see cref="Type.GetType"/> 反射访问 TMP 类型，零编译期依赖——
    /// TMP 缺失时 Tab 内显示提示，窗口本身不受影响。
    ///
    /// 行为：导入包后首次自动打开（EditorPrefs 记忆「已初始化」）；依赖缺失时按开关每次启动自动打开（默认开）。
    /// </summary>
    internal sealed class StorySettingsWindow : EditorWindow
    {
        private const string KeyFirstRun = "com.microbialnet.story.settings.firstRun";
        private const string KeyAutoOpenOnMissing = "com.microbialnet.story.settings.autoOpenOnMissing";
        private const string TmpAssembly = "Unity.TextMeshPro";

        /// <summary>必需 UPM 依赖（id 含版本便于 <see cref="Client.Add"/>；检查按 name 匹配）。</summary>
        private static readonly (string id, string display, string hint)[] Required =
        {
            ("com.unity.textmeshpro@3.0.7", "TextMeshPro", "对话/选项文本渲染必需"),
            ("com.unity.ugui@1.0.0", "uGUI", "对话框 UI 组件必需"),
            ("com.unity.nuget.newtonsoft-json@3.2.1", "Newtonsoft Json", "JSON 序列化（导入导出/迁移/校验）必需"),
        };

        /// <summary>随包外放的使用指南（相对包根目录）。</summary>
        private static readonly string[] DocFiles =
        {
            "Documentation/剧情表格编写规则.md",
            "Documentation/事件节点使用指南.md",
            "Documentation/对话框生成策略使用指南.md",
            "Documentation/系统接口使用指南.md",
        };

        // —— ProjectSettings 风格配色（dark）——
        private static readonly Color C_Back = new Color(0.16f, 0.16f, 0.16f);        // 窗口底
        private static readonly Color C_TabBar = new Color(0.13f, 0.13f, 0.13f);      // 左 Tab 栏
        private static readonly Color C_Content = new Color(0.20f, 0.20f, 0.20f);     // 右内容区
        private static readonly Color C_Accent = new Color(0.24f, 0.48f, 0.85f);      // 主题蓝（选中 Tab）
        private static readonly Color C_Line = new Color(0.30f, 0.30f, 0.30f);        // 分隔线
        private static readonly Color C_Text = new Color(0.85f, 0.85f, 0.85f);

        [MenuItem("MicrobialNet/Story/设置")]
        internal static void Open()
        {
            var w = GetWindow<StorySettingsWindow>("Story 设置");
            w.minSize = new Vector2(620, 520);
            w.Rebuild();
        }

        // —— 导入后自动打开：首次导入展示一次；依赖缺失时按开关每次启动打开 ——
        [InitializeOnLoadMethod]
        private static void AutoOpenOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorPrefs.GetBool(KeyFirstRun, false))
                {
                    EditorPrefs.SetBool(KeyFirstRun, true);
                    Open();
                    return;
                }
                if (EditorPrefs.GetBool(KeyAutoOpenOnMissing, true) && HasMissingDeps())
                    Open();
            };
        }

        private static bool HasMissingDeps()
        {
            var pkgs = PackageInfo.GetAllRegisteredPackages();
            if (pkgs == null || pkgs.Length == 0) return false; // 包信息未就绪：不误报
            var installed = new HashSet<string>(pkgs.Select(p => p.name));
            return Required.Any(r => !installed.Contains(r.id.Split('@')[0]));
        }

        private void OnEnable() => Rebuild();

        // ══════════ 主布局：ProjectSettings 风格（左 Tab 栏 + 右内容区）══════════

        private void Rebuild()
        {
            if (this == null || rootVisualElement == null) return;
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = C_Back;

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;

            // 左：Tab 栏
            var tabBar = new VisualElement { name = "st-tabbar" };
            tabBar.style.width = 150;
            tabBar.style.backgroundColor = C_TabBar;
            tabBar.style.paddingTop = 8;
            tabBar.style.flexShrink = 0;

            // 右：内容区
            var content = new ScrollView(ScrollViewMode.Vertical) { name = "st-content" };
            content.style.flexGrow = 1;
            content.style.backgroundColor = C_Content;
            content.style.paddingLeft = 18;
            content.style.paddingRight = 18;
            content.style.paddingTop = 14;
            content.style.paddingBottom = 18;
            // ScrollView 滚动容器默认宽度随内容伸缩，显式撑满父宽——子元素宽度以此为准（已扣除 Tab 栏）
            content.contentContainer.style.width = Length.Percent(100);
            
            var tabBase = MakeTab("基础", () => ShowBase(content));
            var tabTmp = MakeTab("TMP", () => ShowTmp(content));
            var tabDirs = MakeTab("资源目录", () => ShowDirs(content));
            _tabBaseBtn = tabBase;
            _tabTmpBtn = tabTmp;
            _tabDirsBtn = tabDirs;
            tabBar.Add(tabBase);
            tabBar.Add(tabTmp);
            tabBar.Add(tabDirs);
            tabBar.Add(new Label("更多 Tab 后续补充") { style = { color = new Color(0.55f, 0.55f, 0.55f), paddingTop = 12, paddingLeft = 12, fontSize = 11 } });
            
            body.Add(tabBar);
            body.Add(content);
            root.Add(body);

            ShowBase(content);
        }

        private Button _tabBaseBtn, _tabTmpBtn, _tabDirsBtn, _lastTab;

        /// <summary>ProjectSettings 风格 Tab 按钮：整行可点、左对齐文本、选中主题蓝底。</summary>
        private Button MakeTab(string label, Action show)
        {
            var btn = new Button(show) { text = label };
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.width = 150;
            btn.style.paddingLeft = 16;
            btn.style.paddingTop = 9;
            btn.style.paddingBottom = 9;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderTopLeftRadius = 0;
            btn.style.borderTopRightRadius = 0;
            btn.style.borderBottomLeftRadius = 0;
            btn.style.borderBottomRightRadius = 0;
            btn.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            btn.style.color = C_Text;
            return btn;
        }

        private void SelectTab(Button tab)
        {
            if (_lastTab != null)
            {
                _lastTab.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _lastTab.style.color = C_Text;
            }
            _lastTab = tab;
            tab.style.backgroundColor = C_Accent;
            tab.style.color = Color.white;
        }

        // ══════════ Tab：基础 ══════════

        private void ShowBase(VisualElement content)
        {
            content.Clear();
            SelectTab(_tabBaseBtn);
            AddTitle(content, "基础");

            AddSection(content, "依赖检查");
            var pkgs = PackageInfo.GetAllRegisteredPackages();
            bool pkgReady = pkgs != null && pkgs.Length > 0;
            var installed = new HashSet<string>(pkgReady ? pkgs.Select(p => p.name) : Enumerable.Empty<string>());
            if (!pkgReady) AddHint(content, "包管理器信息加载中，请稍候…（或手动重新打开本窗口刷新）");
            var missing = new List<string>();
            foreach (var r in Required)
            {
                var name = r.id.Split('@')[0];
                bool ok = pkgReady && installed.Contains(name);
                if (pkgReady && !ok) missing.Add(r.id);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 6;
                var mark = new Label(ok ? "✓" : "✗") { style = { width = 22, color = ok ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.95f, 0.42f, 0.35f) } };
                row.Add(mark);
                var info = new Label($"{r.display}  {name}  —  {r.hint}") { style = { flexGrow = 1 } };
                row.Add(info);
                if (!ok)
                {
                    var btn = new Button(() => Install(r.id)) { text = "安装" };
                    row.Add(btn);
                }
                content.Add(row);
            }

            if (missing.Count > 0)
            {
                var allBtn = new Button(() =>
                {
                    foreach (var id in missing) Install(id);
                }) { text = $"一键安装全部缺失（{missing.Count}）" };
                allBtn.style.marginTop = 8;
                content.Add(allBtn);
                AddHint(content, "安装结果见 Console；安装可能触发包刷新 / 程序集重编译，完成后窗口自动刷新。");
            }
            else
            {
                AddHint(content, "全部必需依赖已就绪。");
            }

            AddSection(content, "使用文档");
            var docRow = new VisualElement();
            docRow.style.flexDirection = FlexDirection.Row;
            docRow.style.flexWrap = Wrap.Wrap;
            foreach (var d in DocFiles)
                docRow.Add(new Button(() => OpenDoc(d)) { text = Path.GetFileNameWithoutExtension(d) });
            content.Add(docRow);

            AddSection(content, "编辑器设置");
            var auto = new Toggle("依赖缺失时启动自动打开此窗口") { value = EditorPrefs.GetBool(KeyAutoOpenOnMissing, true) };
            auto.RegisterValueChangedCallback(e => EditorPrefs.SetBool(KeyAutoOpenOnMissing, e.newValue));
            content.Add(auto);
            AddHint(content, "更多全局设置（默认目录 / 自动保存 / 外观）后续版本补充。");
        }

        // ══════════ Tab：TMP（反射实现，零编译期依赖）══════════

        private void ShowTmp(VisualElement content)
        {
            content.Clear();
            SelectTab(_tabTmpBtn);
            AddTitle(content, "TMP");

            AddSection(content, "全局字体");
            var fontType = FindTmpType("TMP_FontAsset");
            if (fontType == null)
            {
                AddHint(content, "未检测到 TextMeshPro（com.unity.textmeshpro）。请先在「基础」Tab 的依赖检查中安装后再配置字体。");
                return;
            }

            // ObjectField 自身 label 置空（行标签由 AddFieldRow 提供，避免双标签挤压控件宽度）
            var objField = new ObjectField("") { objectType = fontType };
            AddFieldRow(content, "字体资产", objField, "选择或拖入 TMP_FontAsset 资产，作为所有 TMP 组件的统一字体。");

            var applyBtn = new Button(() => ApplyTmpFont(fontType, objField.value)) { text = "应用全部" };
            applyBtn.style.marginTop = 10;
            content.Add(applyBtn);
            AddHint(content, "作用范围：当前打开场景中的所有 TMP 组件（含未激活对象）+ Assets 下所有预制体中的 TMP 组件。");
        }

        /// <summary>把场景内所有 TMP 组件 + Assets 下所有预制体内的 TMP 组件的 font 替换为所选字体资产
        /// （场景组件走反射 + Undo；预制体资产经 PrefabUtility 改内容并保存，属资产写入无 Undo）。</summary>
        private static void ApplyTmpFont(Type fontAssetType, UnityEngine.Object font)
        {
            if (font == null)
            {
                EditorUtility.DisplayDialog("未选择字体", "请先选择要应用的字体资产（TMP_FontAsset）。", "确定");
                return;
            }
            var tmpTextType = FindTmpType("TMP_Text");
            if (tmpTextType == null) return;

            PropertyInfo fontProp = null;
            try { fontProp = tmpTextType.GetProperty("font", BindingFlags.Public | BindingFlags.Instance); }
            catch (Exception) { }
            if (fontProp == null || fontProp.PropertyType != fontAssetType)
            {
                EditorUtility.DisplayDialog("应用失败", "无法解析 TMP 组件的 font 属性（版本差异？）。", "确定");
                return;
            }

            // 1) 场景内 TMP 组件（含未激活对象）
            var all = UnityEngine.Object.FindObjectsOfType(tmpTextType);
            int nScene = 0;
            foreach (var c in all)
            {
                try
                {
                    var cur = fontProp.GetValue(c);
                    if (ReferenceEquals(cur, font)) continue;
                    Undo.RecordObject(c, "替换全局字体");
                    fontProp.SetValue(c, font);
                    EditorUtility.SetDirty(c);
                    nScene++;
                }
                catch (Exception) { /* 单组件失败不中断 */ }
            }

            // 2) Assets 下所有预制体内的 TMP 组件
            int nPrefab = ApplyTmpFontOnPrefabs(fontAssetType, tmpTextType, fontProp, font);

            EditorUtility.DisplayDialog("应用完成",
                $"已替换场景 {nScene} 个、预制体 {nPrefab} 个 TMP 组件的字体为「{font.name}」。（未找到时为 0）", "确定");
        }

        /// <summary>遍历 Assets 下所有 .prefab，把其中 TMP 组件的 font 替换为目标资产并保存（预制体写入无 Undo）。</summary>
        private static int ApplyTmpFontOnPrefabs(Type fontAssetType, Type tmpTextType, PropertyInfo fontProp, UnityEngine.Object font)
        {
            int n = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue; // 包内 / 只读目录跳过
                var go = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (var c in go.GetComponentsInChildren(tmpTextType, true))
                    {
                        try
                        {
                            var cur = fontProp.GetValue(c);
                            if (ReferenceEquals(cur, font)) continue;
                            fontProp.SetValue(c, font);
                            dirty = true;
                            n++;
                        }
                        catch (Exception) { /* 单组件失败不中断 */ }
                    }
                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(go, path);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
                catch (Exception) { /* 单预制体失败不中断 */ }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(go);
                }
            }
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }

        private static Type FindTmpType(string typeName)
        {
            try { return Type.GetType("TMPro." + typeName + ", " + TmpAssembly); }
            catch (Exception) { return null; }
        }

        // ══════════ Tab：资源目录（各键空间源目录配置）══════════

        /// <summary>展示各键空间源目录：包约定默认目录只读展示；自定义目录供业务补充（迁移工具 SourceDirs 合并读取）。
        /// 目录只影响编辑器侧迁移/归类，运行时解析不受此配置影响（仍按约定目录 + Resources 全扫兜底）。
        /// 编辑行即时保存不重建（避免输入丢焦点）；增删行后仅替换本键空间块（不重建其它内容）。</summary>
        private void ShowDirs(VisualElement content)
        {
            content.Clear();
            SelectTab(_tabDirsBtn);
            AddTitle(content, "资源目录");

            AddHint(content, "剧情各资产类别的读取目录。◆ 包约定目录固定（可复现/迁移归正）；＋ 自定义目录供业务补充——" +
                             "迁移工具按「默认 ∪ 自定义」收集资产，业务资产放任意目录也能被识别与迁移。");
            foreach (var (key, title) in StoryKeySpaceDirsSettings.KeySpaces)
                AddKeySpaceBlock(content, key, title);
            AddHint(content, "路径填「Assets/...」项目内目录；改动即时保存（EditorPrefs）。");
        }

        /// <summary>键空间块 = 节标题 + 目录内容容器。内容容器增删时原位替换（ReplaceBlock），节标题不动。</summary>
        private void AddKeySpaceBlock(VisualElement content, string key, string title)
        {
            AddSection(content, $"{title}  （{key}）");
            var block = new VisualElement();
            content.Add(block);
            FillBlock(block, key);
        }

        /// <summary>原位替换某键空间的内容容器（增删目录后调用：移除旧容器、同位置插入新容器并重填）。</summary>
        private static void ReplaceBlock(VisualElement oldBlock, string key)
        {
            var parent = oldBlock.parent;
            if (parent == null) return;
            int idx = ChildIndex(parent, oldBlock);
            parent.Remove(oldBlock);
            var nb = new VisualElement();
            if (idx >= 0) parent.Insert(idx, nb);
            else parent.Add(nb);
            FillBlock(nb, key);
        }

        /// <summary>返回 child 在 parent 中的索引（UIElements 无公开 IndexOf，逐子遍历）。</summary>
        private static int ChildIndex(VisualElement parent, VisualElement child)
        {
            int i = 0;
            foreach (var c in parent.Children())
            {
                if (ReferenceEquals(c, child)) return i;
                i++;
            }
            return -1;
        }

        /// <summary>填充键空间内容容器：默认目录只读行 + 自定义目录可编辑行（即时保存；✕ 删除后重建本块）+ 添加按钮。</summary>
        private static void FillBlock(VisualElement block, string key)
        {
            foreach (var d in StoryKeySpaceDirsSettings.DefaultDirs(key))
                block.Add(MakeReadonlyDirRow(d));

            var custom = StoryKeySpaceDirsSettings.GetCustomDirs(key);
            for (int i = 0; i < custom.Count; i++)
            {
                int index = i; // 闭包捕获行下标；删除后 ReplaceBlock 重建、下标随之刷新
                string dir = custom[i];
                block.Add(MakeEditableDirRow(key, index, dir, () =>
                {
                    var cur = StoryKeySpaceDirsSettings.GetCustomDirs(key);
                    if (index >= 0 && index < cur.Count) cur.RemoveAt(index);
                    StoryKeySpaceDirsSettings.SaveCustomDirs(key, cur);
                    ReplaceBlock(block, key);
                }));
            }

            var addBtn = new Button(() =>
            {
                var cur = StoryKeySpaceDirsSettings.GetCustomDirs(key);
                cur.Add(string.Empty);
                StoryKeySpaceDirsSettings.SaveCustomDirs(key, cur);
                ReplaceBlock(block, key);
            }) { text = "+ 添加自定义目录" };
            addBtn.style.marginTop = 6;
            block.Add(addBtn);
        }

        /// <summary>只读目录行（包约定默认目录）。</summary>
        private static VisualElement MakeReadonlyDirRow(string dir)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            row.style.width = Length.Percent(100);
            var m = new Label("◆") { style = { width = 22, color = new Color(0.55f, 0.55f, 0.55f), flexShrink = 0 } };
            row.Add(m);
            var l = new Label(dir)
            {
                style = { color = new Color(0.62f, 0.62f, 0.62f), flexGrow = 1, fontSize = 12, unityFontStyleAndWeight = FontStyle.Italic }
            };
            row.Add(l);
            return row;
        }

        /// <summary>可编辑自定义目录行：TextField 值变更即写回该键空间第 index 项；✕ 删除该行并重建块。</summary>
        private static VisualElement MakeEditableDirRow(string key, int index, string dir, System.Action onRemove)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            row.style.width = Length.Percent(100);

            var m = new Label("＋") { style = { width = 22, color = new Color(0.45f, 0.8f, 0.45f), flexShrink = 0 } };
            row.Add(m);

            var tf = new TextField { value = dir ?? "" };
            tf.style.flexGrow = 1;
            ShrinkAll(tf);
            tf.RegisterValueChangedCallback(e =>
            {
                var cur = StoryKeySpaceDirsSettings.GetCustomDirs(key);
                if (index >= 0 && index < cur.Count)
                {
                    cur[index] = e.newValue;
                    StoryKeySpaceDirsSettings.SaveCustomDirs(key, cur);
                }
            });
            row.Add(tf);

            var del = new Button(onRemove) { text = "✕" };
            del.style.width = 26;
            del.style.flexShrink = 0;
            row.Add(del);
            return row;
        }
        // ══════════ ProjectSettings 风格布局助手 ══════════

        /// <summary>页面大标题 + 下方分隔线。</summary>
        private static void AddTitle(VisualElement content, string text)
        {
            var t = new Label(text) { style = { fontSize = 20, unityFontStyleAndWeight = FontStyle.Bold, color = C_Text, marginBottom = 12 } };
            content.Add(t);
            content.Add(HR());
        }

        /// <summary>1px 分隔线。</summary>
        private static VisualElement HR()
        {
            return new VisualElement { style = { height = 1, backgroundColor = C_Line, marginBottom = 8 } };
        }

        /// <summary>分段标题（加粗，上方留白）。</summary>
        private static void AddSection(VisualElement content, string text)
        {
            var s = new Label(text)
            {
                style = { fontSize = 13, unityFontStyleAndWeight = FontStyle.Bold, color = C_Text, marginTop = 20, marginBottom = 10 }
            };
            content.Add(s);
        }

        /// <summary>字段行：左侧标签（固定宽、右对齐），右侧控件（flexGrow 撑满内容区，已扣除 Tab 栏宽度）。</summary>
        private static void AddFieldRow(VisualElement content, string label, VisualElement control, string hint = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.width = Length.Percent(100); // 行宽 = 内容区宽度（ScrollView 已扣除 Tab 栏）
            var l = new Label(label) { style = { width = 130, marginRight = 10, color = C_Text, flexShrink = 0 } };
            row.Add(l);
            control.style.flexGrow = 1;
            ShrinkAll(control); // 递归清零 minWidth：控件内部 TextInput 等默认 minWidth=Auto 会撑爆容器（同 FieldWidgetFactory.ForceShrink）
            row.Add(control);
            content.Add(row);
            if (!string.IsNullOrEmpty(hint)) AddHint(content, hint);
        }

        /// <summary>递归把控件及其所有子元素的最小宽度清零并允许收缩（flexShrink=1）。
        /// UI Toolkit 控件（TextField/ObjectField/DropdownField 等）内部含 TextInput 等子元素，默认 minWidth=Auto，
        /// 仅设外层 minWidth=0 无法传递进去，必须逐层清零才能真正收缩进面板。</summary>
        private static void ShrinkAll(VisualElement e)
        {
            e.style.flexShrink = 1;
            e.style.minWidth = 0;
            foreach (var c in e.Children())
                ShrinkAll(c);
        }

        /// <summary>灰色说明文字。</summary>
        private static void AddHint(VisualElement content, string text)
        {
            content.Add(new Label(text) { style = { color = new Color(0.55f, 0.55f, 0.55f), marginTop = 4, fontSize = 11, whiteSpace = WhiteSpace.Normal } });
        }

        private void Install(string id)
        {
            Debug.Log($"[Story] 正在安装依赖 {id} …");
            Client.Add(id);
            EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
        }

        private static void OpenDoc(string doc)
        {
            var pkg = PackageInfo.FindForAssembly(typeof(StorySettingsWindow).Assembly);
            if (pkg == null) return;
            var dir = Path.GetDirectoryName(pkg.resolvedPath);
            var abs = Path.Combine(dir ?? "", doc);
            if (File.Exists(abs)) EditorUtility.OpenWithDefaultApp(abs);
            else EditorUtility.DisplayDialog("文档不存在", abs, "确定");
        }
    }
}
