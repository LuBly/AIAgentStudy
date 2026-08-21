using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;

namespace UnrealAgent.Backend.Agent;

/// <summary>
/// 에이전트 세션
/// 프로세스 아이덴티티, 대화 상태, 미들웨어 파이프라인 통합
/// </summary>
public class AgentSession(AgentLoop Loop)
{
    /// <summary> 이 세션의 대화 히스토리 </summary>
    public Conversation.Conversation Conversation { get; } = new();

    /// <summary> 사용자 메세지를 처리 </summary>
    public IAsyncEnumerable<ChatEvent> ProcessMessage(UserInput Input) => Loop.RunAsync(Input, this);
}