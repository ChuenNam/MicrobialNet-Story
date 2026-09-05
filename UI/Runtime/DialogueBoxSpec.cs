using System;
using UnityEngine;
using MicrobialNet.Story;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 弹出对话框的声明式请求（纯数据）。调用方构造后交给
    /// <see cref="DialogueBoxManager.Show"/>，返回值是一个 <see cref="DialogueBoxHandle"/> 令牌。
    /// 设计为纯数据，便于入队、序列化与测试。
    /// </summary>
    public sealed class DialogueBoxSpec
    {
        /// <summary>样式键，对应 StyleRegistry 中注册的预制体。</summary>
        public string styleKey;

        /// <summary>任意业务数据，由内容视图（IDialogueBoxView）自行转型。</summary>
        public object payload;

        /// <summary>生成策略（核心扩展点）。null = 沿用下方 position 静态值 / 样式默认策略。</summary>
        public IDialogueBoxSpawnStrategy spawnStrategy;

        /// <summary>定位策略（null = 屏幕底部居中；若同时设置 spawnStrategy，则以策略解析结果为准）。</summary>
        public DialogueBoxPosition position;

        /// <summary>层级。越大越靠上；同层按弹出顺序叠放。</summary>
        public int layer;

        /// <summary>是否模态：模态框会拦截其下方所有层级的输入。</summary>
        public bool modal;

        /// <summary>「点击继续」时是否保留自身（不关闭）。默认 false = 点击继续后正常关闭。
        /// 由生成策略（resolution.persistent）或调用方置 true，用于「一串对话保留显示」的级联场景。
        /// 仅影响 StoryLineBoxView 的点击行为：若为 true，点击继续只触发 onAdvance，不关自身。</summary>
        public bool persistent;

        /// <summary>自动关闭秒数。&gt;0 表示到时自动关闭；0 = 需手动关闭。</summary>
        public float autoCloseSeconds;

        /// <summary>分组标签，供 CloseByTag 批量关闭。</summary>
        public string tag;

        /// <summary>打开完成回调。</summary>
        public Action<DialogueBoxHandle> onOpened;

        /// <summary>关闭完成（已回收/销毁）回调。</summary>
        public Action<DialogueBoxHandle> onClosed;

        public static DialogueBoxSpec Create(string styleKey, object payload = null) =>
            new DialogueBoxSpec { styleKey = styleKey, payload = payload, position = DialogueBoxPosition.BottomCenter() };
    }
}
