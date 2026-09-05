using System;
using System.Collections;
using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools.Commands
{
    /// <summary>
    /// 列表字段的增 / 删（选项列表、条件组等）。
    /// index &lt; 0 表示向列表末尾追加一个默认元素；否则按索引删除。
    /// 走命令以保证 Undo 可用；结构性变更后由属性面板回调自行刷新（避免每次按键重建面板丢焦点）。
    /// </summary>
    internal sealed class EditListCommand : IGraphCommand
    {
        private readonly string _nodeId;
        private readonly string _listPath;
        private readonly int _index;

        public EditListCommand(string nodeId, string listPath, int index)
        {
            _nodeId = nodeId;
            _listPath = listPath;
            _index = index;
        }

        public string Description => _index < 0
            ? $"添加列表项 {_listPath}"
            : $"删除列表项 {_listPath}[{_index}]";

        public GraphChange Change => new GraphChange(GraphChangeType.FieldChanged, new[] { _nodeId });

        public void Execute(StoryGraphModel model)
        {
            var node = model.GetNode(_nodeId);
            if (node == null) return;
            var list = ReflectionUtil.GetValue(node, _listPath) as IList;
            if (list == null) return;

            Undo.RecordObject(model.Asset, Description);
            if (_index < 0)
            {
                var elemType = list.GetType().GetGenericArguments()[0];
                list.Add(Activator.CreateInstance(elemType));
            }
            else if (_index >= 0 && _index < list.Count)
            {
                list.RemoveAt(_index);
            }
        }
    }
}
