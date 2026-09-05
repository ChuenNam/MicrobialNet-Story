using System;
using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 全局变量资产（跨章节共享的变量真相来源）。
    ///
    /// 与 <see cref="StoryGraphAsset.variables"/>（本图局部变量）是两个正交的维度：
    /// 本资产里的变量在整个工程中共享，每个剧情图自身的 variables 仍是局部变量。
    /// 变量经「变量 id」被条件/赋值节点引用，改名不影响既有配置。
    ///
    /// 重要：<see cref="StoryVariableDef.scope"/>（Local/Global）是「图实例内 vs 图实例间持久」语义，
    /// 与本资产的「跨章节共享」不是同一概念，请勿混用（详见 MEMORY.md 架构约定）。
    ///
    /// 注意：本类只承载数据，不引用任何 UnityEditor API（保持 Runtime 程序集纯净、可进入发布包）。
    /// 查找/创建等需要资产数据库的操作放在 Editor 层的 <c>GlobalVariableLookup</c>。
    /// </summary>
    [CreateAssetMenu(menuName = "MicrobialNet/Story/全局变量", fileName = "GlobalVariables")]
    public sealed class StoryGlobalVariableAsset : ScriptableObject
    {
        /// <summary>全局共享变量定义列表（与 StoryGraphAsset.variables 结构相同）。</summary>
        public System.Collections.Generic.List<StoryVariableDef> variables = new System.Collections.Generic.List<StoryVariableDef>();
    }
}
