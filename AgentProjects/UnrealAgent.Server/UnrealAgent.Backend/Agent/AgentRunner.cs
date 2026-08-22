using Microsoft.Extensions.Hosting;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;

namespace UnrealAgent.Backend.Agent;

/// <summary>
/// 메시지 큐 + 에이전트 호출 + ChatStore 관리를 담당하는 서비스
/// 리더와 팀원 모두 동일한 서비스를 활용
/// Store 수정은 직접하지 않고 OnChatEvent를 통해 UI 스레드에서 실행
/// </summary>
public sealed class AgentRunner(AgentSession Session) : BackgroundService
{
    /// <summary> 반응형 상태 관리자. </summary>
    public ChatStore Store { get; } = new();
    
    /// <summary> 사용자 입력을 순서대로 보관하는 메세지 큐 </summary>
    private readonly Queue<UserInput> MessageQueue = new();
    
    /// <summary> 큐에메시지가 도착하면 BackgroundService 루프를 깨우는 시그널 </summary>
    private readonly SemaphoreSlim Signal = new(0);
    
    /// <summary> ChatEvent 발생시 UI 스레드에서 처리할 이벤트 </summary>
    public event Func<ChatEvent, Task>? OnChatEvent;
    
    /// <summary> 메세지를 Queue에 추가하고 BackgroundService루프를 깨운다. </summary>
    public async Task EnqueueMessage(UserInput input)
    {
        // 사용자 메세지 UI를 위한 await
        await DispatchEventAsync(new ChatEvent.User(input.Text));
        
        MessageQueue.Enqueue(input);
        Signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken Ct)
    {
        while (!Ct.IsCancellationRequested)
        {
            // 시그널 대기 - EnqueueMessage 메세지 도착 시 해제
            await Signal.WaitAsync(Ct);

            // 메시지 큐를 순차 처리
            await DrainQueue();
        }
    }

    private async Task DrainQueue()
    {
        while (MessageQueue.TryDequeue(out UserInput? input))
        {
            try
            {
                await foreach (ChatEvent evt in Session.ProcessMessage(input))
                    await DispatchEventAsync(evt);
            }
            catch (Exception e)
            {
                await DispatchEventAsync(new ChatEvent.System(e.Message));
            }
        }
    }

    /// <summary> ChatEvent를 UI 스레드로 디스패치 </summary>
    private Task DispatchEventAsync(ChatEvent evt)
    {
        if (OnChatEvent is { } Handler)
            return Handler(evt);
        // 구독자가 없으면 ( UI 미로드 상태 ) 직접 Store에 누적
        Store.Process(evt);
        return Task.CompletedTask;
    }
}