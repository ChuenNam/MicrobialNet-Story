using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MicrobialNet.Story.EditorTools
{
    /// <summary>
    /// 把 <see cref="UnityEngine.Object"/> 引用（ScriptableObject / GameObject / Component）序列化为资产 GUID 字符串，
    /// 反序列化时按 GUID 还原为资产引用。
    /// 目的：避免 Newtonsoft.Json 直接反射 GameObject / Component 时触碰已废弃的
    /// rigidbody / camera / collider 等属性 getter（Unity 2022.3 会抛 NotSupportedException，
    /// 表现为「保存/自动保存/打开剧情图资产时崩溃」），同时让样式资产等引用在剧情图
    /// JSON 导入/导出/自动保存中正确往返。
    /// 仅用于 Editor 程序集（依赖 UnityEditor.AssetDatabase）。
    /// </summary>
    internal sealed class UnityObjectRefConverter : JsonConverter
    {
        public override bool CanConvert(System.Type objectType)
            => typeof(UnityEngine.Object).IsAssignableFrom(objectType);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var obj = (UnityEngine.Object)value;
            if (obj == null) { writer.WriteNull(); return; }
            string path = AssetDatabase.GetAssetPath(obj);
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            writer.WriteValue(guid);
        }

        public override object ReadJson(JsonReader reader, System.Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            string guid = reader.Value as string;
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath(path, objectType);
        }
    }
}
