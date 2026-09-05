using UnityEngine;

namespace MicrobialNet.Story
{
    /// <summary>
    /// 基于 PlayerPrefs 的存档落地默认实现（即插即用示例）。
    ///
    /// 正式宿主可替换为自家存档系统（云存档 / 二进制 / SQLite 等），只要实现
    /// <see cref="IStorySaveStore"/> 并在装配时传入 <see cref="StoryFlowConfig.Save"/> 即可，
    /// 剧情逻辑零改动。
    /// </summary>
    public sealed class PlayerPrefsSaveStore : IStorySaveStore
    {
        private readonly string _key;

        /// <summary>构造一个存档槽。<paramref name="key"/> 为该槽在 PlayerPrefs 中的键。</summary>
        public PlayerPrefsSaveStore(string key = "StoryFlow.Progress") => _key = key;

        public void Save(string json)
        {
            PlayerPrefs.SetString(_key, json);
            PlayerPrefs.Save();
        }

        public string Load() => PlayerPrefs.GetString(_key, null);

        public bool HasSave() => PlayerPrefs.HasKey(_key);

        public void Clear()
        {
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.Save();
        }
    }
}
