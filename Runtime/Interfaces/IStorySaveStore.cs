namespace MicrobialNet.Story
{
    /// <summary>
    /// 进度存档落地接口（即插即用接缝）。
    ///
    /// 框架只认这个接口，不认 PlayerPrefs / 宿主存档系统；宿主在装配时提供一个实现即可。
    /// 单槽语义：一个 StoryFlowConfig 对应一个存档槽（一张剧情图的断点续玩进度）。
    /// 多剧情/多槽场景由宿主持有多个 IStorySaveStore 实例即可。
    /// </summary>
    public interface IStorySaveStore
    {
        /// <summary>写入当前进度（json 已由框架序列化好）。</summary>
        void Save(string json);

        /// <summary>读取已存进度；无存档时返回 null 或空串。</summary>
        string Load();

        /// <summary>是否存在有效存档。</summary>
        bool HasSave();

        /// <summary>清除存档（剧情结束 / 存档失效时调用）。</summary>
        void Clear();
    }
}
