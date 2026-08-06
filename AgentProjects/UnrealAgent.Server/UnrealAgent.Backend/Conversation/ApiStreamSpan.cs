using System.Globalization;
using System.Text;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Core;
using Block = UnrealAgent.Backend.Core.Block;

namespace UnrealAgent.Backend.Conversation;
/// <summary>
/// API 호출 1회의 스트리밍 응답을 파싱하고 누적합니다.
/// Process()로 이벤트를 먹이고, Complete()로 결과를 확정합니다.
/// </summary>
public sealed class ApiStreamSpan
{
    /// <summary> 현재 진행 중인 블록의 종류와 상태 </summary>
    private abstract record ActiveBlock
    {
        /// <summary> 텍스트 응답 블록 </summary>
        public sealed record Text :  ActiveBlock;
        
        /// <summary> 사고 과정(Extended Thinking) 응답 블록 </summary>
        public sealed record Thinking :  ActiveBlock;

        /// <summary> 도구 호출 블록 </summary>
        public sealed record ToolUse(string Id, string Name) :  ActiveBlock;
    }

    public abstract record Result
    {
        /// <summary> 응답/사고가 잘려서 이어서 생성해야 합니다. 도구 실행 없이 다음 API 호출로 이어갑니다. </summary>
        public sealed record Continue(AssistantSpan CompletedSpan) : Result;
        
        /// <summary> 대화가 완료되었습니다. </summary>
        public sealed record EndSpan(AssistantSpan CompletedSpan) : Result;
        
        /// <summary> 도구 실행이 필요합니다. 실행 후 다음 API 호출을 계속합니다. </summary>
        public sealed record ExecuteTools(AssistantSpan CompletedSpan, IReadOnlyList<Block.ToolUse> ToolCalls) : Result;
    }
    
    
    // ── 스트리밍 중 상태 ──
    /// <summary> 현재 처리 중인 블록입니다. null인 경우 블록 사이 유후 상태입니다. </summary>
    private ActiveBlock? CurrentBlock;
    /// <summary> 텍스트 델타를 누적하는 버퍼입니다. </summary>
    private readonly StringBuilder TextBuffer = new();
    /// <summary> 사고 과정 델타를 누적하는 버퍼입니다. </summary>
    private readonly StringBuilder ThinkingBuffer = new();
    /// <summary> 도구 입력 Json델타를 누적하는 버퍼 </summary>
    private readonly StringBuilder ToolJsonBuffer = new();
    /// <summary> 사고 블록의 서명입니다. 사고 델타 이후 별도 델타로 도착합니다. </summary>
    private string? ThinkingSignature;
    
    // ── 턴 완료 후 결과 ──
    /// <summary> 메세지 레벨 종료 사유 (EndSpan, ToolUse, PauseTurn 등) </summary>
    public StopReason? FinalStopReason;
    /// <summary> 완성된 어시스턴트 콘텐츠 블록 목록 ( 답변, 사고, 도구 호출 ) </summary>
    private readonly List<Block> AssistantBlocks = [];
    public IReadOnlyList<Block> Blocks => AssistantBlocks;
    
    // ── 일반 함수들 ──
    /// <summary>
    /// 스트리밍 이벤트 하나를 처리
    /// 클라이언트에 전달할 ChatEvent가 있으면 반환하고, 없으면 null을 반환한다.
    /// </summary>
    public ChatEvent? Process(RawMessageStreamEvent Event)
    {
        // 1) 콘텐츠 블록 Start - text / thinking 블록이 새로 열림
        if(Event.TryPickContentBlockStart(out RawContentBlockStartEvent? StartEvt))
            return ProcessBlockStart(StartEvt);
        
        // 2) 콘텐츠 블록 Delta
        if(Event.TryPickContentBlockDelta(out RawContentBlockDeltaEvent? DeltaEvt))
            return ProcessDelta(DeltaEvt);
        
        // 3) 콘텐츠 블록 Stop
        if(Event.TryPickContentBlockStop(out RawContentBlockStopEvent? BlockStopEvt))
            return ProcessBlockStop();
        
        // 4) 메세지 시작 - 응답 전체의 시작, 토큰 사용량(Usage) 등 메타 정보 포함
        if (Event.TryPickStart(out RawMessageStartEvent? StartMsgEvt))
            return ProcessMessageStart(StartMsgEvt);
        
        // 5) 메세지 델타 - 응답 종료 시점, stop_reason 등 최종 메타 정보 포함
        if (Event.TryPickDelta(out RawMessageDeltaEvent? MsgDelta))
            return ProcessMessageDelta(MsgDelta);
        
        return null;
    }
    
    /// <summary>
    /// 블록 시작 이벤트를 처리
    /// 블록 종류를 식별하여 CurrentBlock 상태를 설정한다.
    /// </summary>
    private ChatEvent? ProcessBlockStart(RawContentBlockStartEvent startEvt)
    {
        if (startEvt.ContentBlock.TryPickText(out _))
            CurrentBlock = new ActiveBlock.Text();
        else if (startEvt.ContentBlock.TryPickThinking(out _))
            CurrentBlock = new ActiveBlock.Thinking();
        else if(startEvt.ContentBlock.TryPickToolUse(out ToolUseBlock? ToolUse))
            CurrentBlock = new ActiveBlock.ToolUse(ToolUse.ID, ToolUse.Name);
        
        return null;
    }
    /// <summary>
    /// 콘텐츠 블록 델타를 처리
    /// Current Block 상태에 따라 적절한 버퍼에 누적하고, 텍스트/사고 델타는 ChatEvent로 즉시 반환 
    /// </summary>
    private ChatEvent? ProcessDelta(RawContentBlockDeltaEvent deltaEvt)
    {
        switch (CurrentBlock)
        {
            case ActiveBlock.Text when deltaEvt.Delta.TryPickText(out TextDelta? TextDelta):
                TextBuffer.Append(TextDelta.Text);
                return new ChatEvent.Text(TextDelta.Text);
            
            case ActiveBlock.Thinking when deltaEvt.Delta.TryPickThinking(out ThinkingDelta? ThinkingDelta):
                ThinkingBuffer.Append(ThinkingDelta.Thinking);
                return new ChatEvent.Thinking(ThinkingDelta.Thinking);
            
            case ActiveBlock.Thinking when deltaEvt.Delta.TryPickSignature(out SignatureDelta? SigDelta):
                ThinkingSignature = SigDelta.Signature;
                return null;
            
            case ActiveBlock.ToolUse when deltaEvt.Delta.TryPickInputJson(out InputJsonDelta? JsonDelta):
                ToolJsonBuffer.Append(JsonDelta.PartialJson);
                return null;
            default:
                return null;
        }
    }
    /// <summary>
    /// 블록 종료 이벤트를 처리합니다.
    /// 누적된 버퍼를 확정하여 AssistantBlocks에 도메인 Block으로 추가합니다.
    /// </summary>
    private ChatEvent? ProcessBlockStop()
    {
        switch (CurrentBlock)
        {
            case ActiveBlock.Text:
            {
                if (TextBuffer.Length > 0)
                {
                    AssistantBlocks.Add(new Block.Text(TextBuffer.ToString()));
                    TextBuffer.Clear();
                }
                break;
            }
            case ActiveBlock.Thinking:
            {
                if (ThinkingBuffer.Length > 0)
                {
                    AssistantBlocks.Add(new Block.Thinking(ThinkingBuffer.ToString(), ThinkingSignature));
                    ThinkingBuffer.Clear();
                    ThinkingSignature = null;
                }
                break;
            }
            case ActiveBlock.ToolUse { Id : { } Id, Name: { } Name }:
            {
                string InputJson = ToolJsonBuffer.ToString();
                AssistantBlocks.Add(new Block.ToolUse(Id, Name, InputJson));
                ToolJsonBuffer.Clear();

                break;
            }
            
        }
        
        CurrentBlock = null;
        return null;
    }
    /// <summary>
    /// 메세지 시작 이벤트를 처리.
    /// 캐시 포함 전체 입력 토큰의 수를 캡처
    /// </summary>
    private ChatEvent? ProcessMessageStart(RawMessageStartEvent startMsgEvt)
    {
        return null;
    }
    /// <summary>
    /// 메세지 델타 이벤트를 처리
    /// 종료 사유를 캡처
    /// </summary>
    private ChatEvent? ProcessMessageDelta(RawMessageDeltaEvent msgDelta)
    {
        if(msgDelta.Delta.StopReason is { } Reason)
            FinalStopReason =  Reason;
        
        return null;
    }

    /// <summary>
    /// 스트리밍을 완료하고 AssistantSpan을 생성
    /// 반환값으로 다음 행동 ( 도구 실행, 이어서 호출, 종료 ) 등을 결정
    /// </summary>
    public Result Complete()
    {
        AssistantSpan CompleteSpan = new()
        {
            AssistantBlocks = AssistantBlocks.ToList(),
        };

        // 도구 실행 요청이 있는지 체크
        List<Block.ToolUse> ToolCalls = AssistantBlocks.OfType<Block.ToolUse>().ToList();
        
        // 도구 사용
        if(ToolCalls.Count > 0 && FinalStopReason is StopReason.ToolUse)
            return new Result.ExecuteTools(CompleteSpan, ToolCalls);
        
        // 서버에 문제가 있었으므로 다시 실행
        if (FinalStopReason is StopReason.PauseTurn)
            return new Result.Continue(CompleteSpan);
        
        // 정상 종료
        return new Result.EndSpan(CompleteSpan);
    }
}