using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MicrobialNet.Story;
using NUnit.Framework;
using UnityEngine;

namespace MicrobialNet.Story.Tests
{
    /// <summary>
    /// 资产定位接缝（IStoryAssetLocator）契约与运行时加载点接线测试：
    /// 契约（缺失→null / 空目录→空数组非 null / 异步成员同步完成）+
    /// 接线（角色兜底 FromResources 与图批量扫描 StoryGraphRegistry.Awake 均经 StoryAssetLocator.Current）。
    /// 涉及进程级全局（Current / StoryConstants.GraphResolver），SetUp/TearDown 与 finally 严格存取恢复。
    /// </summary>
    public class AssetLocatorSeamTests
    {
        private IStoryAssetLocator _originalLocator;

        [SetUp]
        public void SetUp() => _originalLocator = StoryAssetLocator.Current;

        [TearDown]
        public void TearDown() => StoryAssetLocator.Current = _originalLocator;

        /// <summary>可注入预设资产的假定位器：记录 LoadAll 键路径，供断言「加载点确实经接缝且键原样传递」。</summary>
        private sealed class FakeLocator : IStoryAssetLocator
        {
            public readonly List<string> LoadAllPaths = new List<string>();
            private readonly Dictionary<string, UnityEngine.Object[]> _byPath;

            public FakeLocator(Dictionary<string, UnityEngine.Object[]> byPath) => _byPath = byPath;

            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                if (_byPath != null && _byPath.TryGetValue(path, out var arr))
                    foreach (var o in arr)
                        if (o is T t) return t;
                return null;
            }

            public T[] LoadAllAssets<T>(string path) where T : UnityEngine.Object
            {
                LoadAllPaths.Add(path);
                if (_byPath == null || !_byPath.TryGetValue(path, out var arr)) return new T[0];
                var list = new List<T>();
                foreach (var o in arr)
                    if (o is T t) list.Add(t);
                return list.ToArray();
            }

            public Task<T> LoadAssetAsync<T>(string path) where T : UnityEngine.Object
                => Task.FromResult(LoadAsset<T>(path));

            public Task<T[]> LoadAllAssetsAsync<T>(string path) where T : UnityEngine.Object
                => Task.FromResult(LoadAllAssets<T>(path));
        }

        // ── 契约：默认实现（Resources）的缺失/空/异步语义 ─────────────────

        [Test]
        public void ResourcesLocator_LoadMissing_ReturnsNull()
        {
            var locator = new ResourcesStoryAssetLocator();
            Assert.IsNull(locator.LoadAsset<StoryCharacterAsset>("Definitely/Not/Here"));
        }

        [Test]
        public void ResourcesLocator_LoadAllEmptyDir_ReturnsEmptyNotNull()
        {
            var locator = new ResourcesStoryAssetLocator();
            var all = locator.LoadAllAssets<StoryCharacterAsset>("Definitely/Not/Here");
            Assert.IsNotNull(all, "契约：LoadAllAssets 返回空数组而非 null");
            Assert.AreEqual(0, all.Length);
        }

        [Test]
        public void ResourcesLocator_AsyncMembers_CompleteSynchronously()
        {
            var locator = new ResourcesStoryAssetLocator();
            var single = locator.LoadAssetAsync<StoryCharacterAsset>("Definitely/Not/Here");
            var all = locator.LoadAllAssetsAsync<StoryCharacterAsset>("Definitely/Not/Here");
            Assert.IsTrue(single.IsCompleted, "本地实现：Task.FromResult 立即完成（无网络语义）");
            Assert.IsNull(single.Result, "契约：异步缺失完成于 null 结果");
            Assert.IsTrue(all.IsCompleted);
            Assert.IsNotNull(all.Result);
            Assert.AreEqual(0, all.Result.Length);
        }

        // ── 接线：两个运行时加载点确实经 StoryAssetLocator.Current ────────

        [Test]
        public void CharacterResolver_FromResources_GoesThroughLocator()
        {
            var character = ScriptableObject.CreateInstance<StoryCharacterAsset>();
            try
            {
                character.characterId = "hero";
                character.displayName = "勇者";
                var fake = new FakeLocator(new Dictionary<string, UnityEngine.Object[]>
                {
                    ["Story/Characters"] = new UnityEngine.Object[] { character },
                });
                StoryAssetLocator.Current = fake;

                var resolver = ScriptableCharacterResolver.FromResources("Story/Characters");

                CollectionAssert.AreEqual(new[] { "Story/Characters" }, fake.LoadAllPaths);
                var vm = resolver.Resolve("hero");
                Assert.IsTrue(vm.isValid, "经接缝加载的角色应注册进解析器");
                Assert.AreEqual("勇者", vm.displayName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(character);
            }
        }

        [Test]
        public void GraphRegistry_Awake_ScanGoesThroughLocator()
        {
            var graph = ScriptableObject.CreateInstance<StoryGraphAsset>();
            GameObject go = null;
            var savedResolver = StoryConstants.GraphResolver;
            try
            {
                graph.name = "g1";
                var fake = new FakeLocator(new Dictionary<string, UnityEngine.Object[]>
                {
                    ["Story/Graphs"] = new UnityEngine.Object[] { graph },
                });
                StoryAssetLocator.Current = fake;

                go = new GameObject("asset-locator-registry-test");
                var registry = go.AddComponent<StoryGraphRegistry>();
                // EditMode 下 AddComponent 不触发 Awake，反射调用真实 Awake 路径（用序列化默认子目录 Story/Graphs）。
                var awake = typeof(StoryGraphRegistry).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(awake);
                awake.Invoke(registry, null);

                CollectionAssert.AreEqual(new[] { "Story/Graphs" }, fake.LoadAllPaths);
                Assert.IsNotNull(StoryConstants.GraphResolver, "扫描到图后应注册全局解析器");
                Assert.AreSame(graph, StoryConstants.GraphResolver("g1"), "按资产文件名兜底键可解析到经接缝加载的图");
            }
            finally
            {
                StoryConstants.GraphResolver = savedResolver; // 进程级全局：恢复，避免污染其它用例
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }
    }
}
