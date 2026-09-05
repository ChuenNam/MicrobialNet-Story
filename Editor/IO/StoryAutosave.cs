using System;
using System.IO;
using MicrobialNet.Story;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 剧情图自动保存快照（崩溃恢复，对应需求 A5）。
    /// 窗口每隔 60 秒把当前「脏」图序列化到 Library/StoryEditorAutosave/；
    /// 异常退出后再次打开该图时，窗口检测快照并提示恢复。
    /// 快照仅在图处于脏状态时写入；保存后由窗口显式清除，避免正常会话误报。
    /// </summary>
    public static class StoryAutosave
    {
        private const string Dir = "Library/StoryEditorAutosave";

        public static string PathFor(StoryGraphAsset a)
        {
            string name = a != null && !string.IsNullOrEmpty(a.name) ? Sanitize(a.name) : "unnamed";
            return $"{Dir}/{name}.autosave.json";
        }

        public static bool HasSnapshot(StoryGraphAsset a) => File.Exists(PathFor(a));

        public static void Write(StoryGraphAsset a)
        {
            if (a == null) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                File.WriteAllText(PathFor(a), StoryJsonExporter.Export(a));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Story] 自动保存快照失败：{ex.Message}");
            }
        }

        public static string Read(StoryGraphAsset a)
        {
            string p = PathFor(a);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        public static void Clear(StoryGraphAsset a)
        {
            try
            {
                string p = PathFor(a);
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception) { /* 忽略清理失败 */ }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "unnamed" : r;
        }
    }
}
