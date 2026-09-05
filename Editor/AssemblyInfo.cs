using System.Runtime.CompilerServices;

// 允许测试程序集访问 Editor 内部类型（StoryGraphModel / StoryValidator / StorySimulator /
// StoryJsonExporter / StoryTableResolver 等），供 Editor/Tests 下的自动化测试直接构造与断言。
// 不对宿主（Assembly-CSharp）等外部程序集开放。
[assembly: InternalsVisibleTo("com.microbialnet.story.Tests")]
