using System;

namespace MicrobialNet.Story.EditorTools.Validation
{
    /// <summary>校验问题的严重级别。</summary>
    public enum ValidationSeverity
    {
        /// <summary>错误：会导致剧情无法正确运行，必须修复。</summary>
        Error,
        /// <summary>警告：可能不符合预期，建议检查。</summary>
        Warning,
        /// <summary>提示：信息性，不影响运行。</summary>
        Info,
    }

    /// <summary>
    /// 一条校验问题。<see cref="NodeId"/> 为 null 表示图级问题（如缺少开始节点）。
    /// </summary>
    [Serializable]
    public sealed class ValidationIssue
    {
        public ValidationSeverity Severity;
        public string RuleId;
        public string NodeId;
        public string Message;

        public ValidationIssue() { }

        public ValidationIssue(ValidationSeverity severity, string ruleId, string message, string nodeId = null)
        {
            Severity = severity;
            RuleId = ruleId;
            Message = message;
            NodeId = nodeId;
        }
    }
}
