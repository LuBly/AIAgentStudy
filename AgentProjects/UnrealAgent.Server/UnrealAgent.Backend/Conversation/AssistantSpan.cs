using UnrealAgent.Backend.Core;

namespace UnrealAgent.Backend.Conversation;

/// <summary>
/// Claude API 호출 1회의 결과입니다.
/// 어시스턴트 응답 블록과 도구 실행 결과를 포함합니다.
/// </summary>
public sealed class AssistantSpan
{
    /// <summary> 어시스턴트 응답 블록 목록 </summary>
    public required IReadOnlyList<Block> AssistantBlocks { get; init; }

    /// <summary> 도구 실행 결과 레코드 </summary>
    public sealed record ToolExecution(string ToolUseId, string Name, string Output, bool bIsError);

    /// <summary> 도구 실행 결과 목록. 도구 호출이 없다면 비어있다. </summary>
    public List<ToolExecution> ToolExecutions { get; } = [];
}