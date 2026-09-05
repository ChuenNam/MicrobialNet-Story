# MicrobialNet Story

可视化剧情 / 流程编辑与运行时播放工具包。策划在编辑器里搭剧情图，程序在运行时按图播放，并把真实的游戏系统（存档、角色、战斗、UI 等）通过一组接缝接口接入——剧情逻辑本身零改动。

## 功能特性

- **编辑器可视化搭建**：节点 + 连线，内置本地化 / 变量 / 角色 / 事件等节点类型。
- **运行时播放**：把剧情图资产交给控制组件即可开播，支持暂停、选项、章节跳转、断点续玩。
- **一组对外接缝**：事件 / 变量 / 文本本地化 / 角色 / 存档 / 多图，全部以接口形式暴露，宿主按需实现。
- **控制门面 `StoryFlow`**：运行时唯一的装配与驱动入口（一个 MonoBehaviour 组件）。

## 快速开始

1. 本工具以 UPM 包形式内置在工程内（`Packages/com.microbialnet.story`），无需额外安装。
2. 在场景里选中剧情 GameObject，点击 Inspector 的 **Add Component**，搜索 `story`，即可在 **MicrobialNet / Story** 分组下找到以下组件：
   - **Story Flow** —— 剧情控制门面，挂上它并指派剧情图资产（`.asset`）后即可开播。
   - **Story Graph Registry** —— 全局图注册表，用于多图 / 章节跳转场景。
   - **Story View (TMP)** / **Story Variable Debug** —— 可选的 TextMeshPro 表现层与变量调试面板。
3. 运行时调用 `storyFlow.Play()` 即开始播放；具体装配方式见下方「使用文档」。

## 相关文档

- [使用文档](https://chuennam.github.io/MicrobialNet-Story/manual/html/)  
MicrobialNet Story 的基础使用方法与操作演示 **（推荐优先阅读）**

- [组件 API 参考](https://chuennam.github.io/MicrobialNet-Story/component-api-reference/html/)  
每个宿主可见类：元信息 / 属性 / 方法签名 / 事件 / 生命周期消息 / 完整示例，按类定位。

- [系统接口使用指南](https://chuennam.github.io/MicrobialNet-Story/system-api-guide/html/)  
全部可接入的接缝接口（变量 / 事件 / 文本 / 角色 / 存档 / 多图 / 表现层）说明与完整装配示例。

- [剧情表编写规则](https://chuennam.github.io/MicrobialNet-Story/story-table-rules/html/)  
剧本表格的书写格式规则。具体说明表的列结构、行类型与跳转规则

- [事件节点使用指南](https://chuennam.github.io/MicrobialNet-Story/event-node-guide/html/)  
通过使用事件节点与自定义事件，更好的搭建剧情与游戏流程

- [对话框生成策略使用指南](https://chuennam.github.io/MicrobialNet-Story/dialogue-box-spawn-guide/html/)  
面向开发者实现自定义对话框出现效果的接口手册与参考案例


## 接缝接口一览

所有接缝都在 `StoryFlowConfig` 里一次性填好，再交给 `StoryFlow.Configure(config)`：

| 模块 | 接口 | 作用 | 默认实现 |
|---|---|---|---|
| 变量 | `IStoryVariableProvider` | 条件分支 / 赋值读写变量 | `InMemoryVariableProvider` |
| 事件 | `IStoryEventHandler` + `IStoryEvent` | 剧情里的暂停点 / 业务事件 | `StoryEventBus` |
| 文本 / 本地化 | `IStoryTextProvider` | 对白 / 选项的多语言 | `LocalizationTextProvider` |
| 角色 | `IStoryCharacterResolver` | 把角色 ID 解析为名字 / 立绘 / 主题色 | `ScriptableCharacterResolver` |
| 存档 | `IStorySaveStore` | 断点续玩进度落地 | `PlayerPrefsSaveStore` |
| 多图 / 章节 | `Func<string, StoryGraphAsset>` | 章节跳转取目标图 | `StoryGraphCollection.Resolver` |
| 表现层 | `StoryFlow` 事件 / `IStoryPresenter` | 驱动自定义 UI | 内置 `StoryView`(TMP) 或自实现 |

> 除「变量」外，其余接缝留空也能跑（自动用默认实现），适合先跑通 demo 再逐步替换成真实游戏系统。

## 模块与依赖

- 运行时程序集：`com.microbialnet.story`
- 编辑器程序集：`com.microbialnet.story.Editor`
- 可选 TextMeshPro 表现层：`com.microbialnet.story.TMP`
- 依赖：Unity `2022.3`+、TextMeshPro `3.0.7`、UGUI `1.0.0`
- 示例：位于 `Samples~`，不进入正式构建

## 版本

变更记录见 [CHANGELOG.md](CHANGELOG.md)。
