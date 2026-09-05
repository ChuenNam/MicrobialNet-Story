using System.Runtime.CompilerServices;

// 允许同包 Editor 程序集访问 Runtime 内部类型：编辑器需要直接读写剧情数据模型
// （节点 / 边 / 资产字段 / 注册表等），但宿主（Assembly-CSharp）等外部程序集不可见，
// 从而实现「封装内部实现 + 底层数据结构」——对外只经 StoryFlow facade 露使用函数。
[assembly: InternalsVisibleTo("com.microbialnet.story.Editor")]

// 允许同包 TMP 视图程序集访问 Runtime 内部类型（如 TypingScheduler），使视图能消费打字机节奏序列，
// 但不向宿主（Assembly-CSharp）等外部程序集暴露内部实现。
[assembly: InternalsVisibleTo("com.microbialnet.story.TMP")]

// 允许同包测试程序集访问 Runtime 内部类型（StoryPlayer/RuntimeStoryGraph/StoryTableSubGraph/
// ConditionEvaluator/NodeRegistry 等），供 Editor/Tests 下的自动化测试直接构造与断言。
[assembly: InternalsVisibleTo("com.microbialnet.story.Tests")]
