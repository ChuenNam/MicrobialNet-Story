using System.Collections.Generic;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 项目内「角色库」：枚举全部 StoryCharacterAsset 资产，供左侧角色面板与
    /// [CharacterPicker] 下拉共用。角色资产是讲述者的唯一真相来源（需求 D1）。
    /// </summary>
    public static class CharacterLibrary
    {
        public static List<StoryCharacterAsset> All()
        {
            var list = new List<StoryCharacterAsset>();
            foreach (var guid in AssetDatabase.FindAssets("t:StoryCharacterAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<StoryCharacterAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        /// <summary>
        /// 把 characterId 解析为运行时视图模型（名字 + 主题色 + 立绘，见 <see cref="StoryConstants.CharacterViewModel"/>）。
        /// 供注册到 StoryConstants.CharacterViewModelResolver，使 Runtime 的 ResolveCharacter / SpeakerDisplayName
        /// 自动解析。旁白/未知/空走内置中文名；命中角色资产则取其 displayName/colorHex/avatar；否则回退 default（isValid=false）。
        /// 回退 default（而非裸 ID）是刻意设计：让 StoryConstants.ResolveCharacter 落到 [未配置]
        /// 占位符，绝不允许角色 ID（纯数据标识）泄漏到玩家画面。
        /// </summary>
        public static StoryConstants.CharacterViewModel ResolveViewModel(string id)
        {
            if (id == StoryConstants.NarrationId) return new StoryConstants.CharacterViewModel { displayName = "旁白", isValid = true };
            if (id == StoryConstants.UnknownId) return new StoryConstants.CharacterViewModel { displayName = "???", isValid = true };
            if (id == StoryConstants.SelfId) return new StoryConstants.CharacterViewModel { displayName = "玩家自己", isValid = true };
            if (string.IsNullOrEmpty(id)) return new StoryConstants.CharacterViewModel { displayName = "旁白", isValid = true };
            foreach (var a in All())
            {
                if (a != null && a.characterId == id && !string.IsNullOrEmpty(a.displayName))
                    return new StoryConstants.CharacterViewModel
                    {
                        displayName = a.displayName,
                        colorHex = a.colorHex,
                        avatar = a.avatar,
                        isValid = true,
                    };
            }
            // 解析不到：回退 default（isValid=false），交由 StoryConstants.ResolveCharacter 落到 [未配置] 占位符。
            // 绝不回退裸 ID——角色 ID 只是数据标识，绝不能出现在玩家画面。
            return default;
        }
    }
}
