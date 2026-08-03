using System.Text;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;

namespace UnrealAgent.Backend.Prompt;
/// <summary>
/// Claude API 시스템 프롬프트 구성과 MessageCreateParams 생성을 담당.
/// 시스템 프롬프트는 최초 호출 시 생성되고 이후 캐싱됩니다.
/// </summary>
public sealed class PromptBuilder
{
    /// <summary> 빌더 체인의 각 섹션. 토큰 측정 시 특정 섹션을 제외할 수 있다. </summary>
    [Flags]
    public enum Section
    {
        None = 0,
        Identity = 1 << 0,
        System = 1 << 1,
        DoingTasks = 1 << 2,
        Proactiveness = 1 << 3,
        ToneAndStyle = 1 << 4,
        OutputEfficiency = 1 << 5,
    }
    // ── API 파라미터 생성 ──
    public MessageCreateParams Build(AgentSession? session) => new()
    {
      Model = "claude-opus-4-6",
      MaxTokens = 1024,
      CacheControl = new CacheControlEphemeral(),
      System = new List<TextBlockParam>
      {
        new() { Text = BuildSystemPrompt(session) }
      },
      Messages = session.Conversation.ToAnthropicMessages(),
      Thinking = new ThinkingConfigAdaptive(),
      OutputConfig = new OutputConfig()
      {
        Effort = Effort.High
      }
    };
    
    // ── 시스템 프롬프트 구성 ──
    private string BuildSystemPrompt(AgentSession? session = null)
    {
      return BuildInternal(Section.None, session);
    }

    private string BuildInternal(Section skip, AgentSession? session)
    {
      StringBuilder sb = new();
      if(!skip.HasFlag(Section.Identity))
        sb.AppendLine(Identity());
      
      if(!skip.HasFlag(Section.System))
        sb.AppendLine(System());
      
      if(!skip.HasFlag(Section.DoingTasks))
        sb.AppendLine(DoingTasks());
      
      if(!skip.HasFlag(Section.Proactiveness))
        sb.AppendLine(Proactiveness());
      
      if(!skip.HasFlag(Section.ToneAndStyle))
        sb.AppendLine(ToneAndStyle());
      
      if(!skip.HasFlag(Section.OutputEfficiency))
        sb.AppendLine(OutputEfficiency());
      
      return sb.ToString();
    }


    // ── 시스템 프롬프트 ──
    
    /// <summary>
    /// AI 어시스턴트의 역할과 핵심 규칙을 정의합니다.
    /// </summary>
    private static string Identity() => """
                                        You are UnrealAgent, an AI assistant that controls Unreal Editor.

                                        You are an interactive agent that helps users with Unreal Engine level design,
                                        asset management, and editor automation tasks. Use the instructions below and
                                        the tools available to you to assist the user.

                                        IMPORTANT: You must NEVER guess actor names, asset paths, or property values.
                                        Always query the current state first.
                                        IMPORTANT: Before deleting assets or making bulk destructive changes (100+
                                        actors, project settings), always confirm with the user first.
                                        """;
    
    /// <summary>
    /// 시스템 레벨 동작 규칙을 정의합니다.
    /// </summary>
    private static string System() => """
                                      # System
                                      - All text you output outside of tool use is displayed to the user in the
                                        chat panel. Output text to communicate with the user.
                                      - Tool results may be truncated. If output is cut off, use more specific
                                        queries or pagination.
                                      - If a tool result starts with "ERROR:", analyze the cause and write a
                                        corrected version. Do not retry the same code.
                                      """;

    /// <summary>
    /// 작업 수행 시 단계별 절차를 정의합니다.
    /// </summary>
    private static string DoingTasks() => """
                                          # Doing tasks
                                          1. Query current state — always verify before making changes
                                          2. Plan the approach — for complex tasks, break into small steps
                                          3. Execute one step at a time — one logical operation per tool call
                                          4. Verify the result — confirm the operation succeeded
                                          5. Report back concisely

                                          - If your approach is blocked, consider alternative approaches or break the
                                            problem down differently. Do not repeat the same failing code.
                                          - Avoid over-engineering. Only do what the user asked.
                                          """;
    
    /// <summary>
    /// 능동적 행동의 범위를 정의합니다.
    /// </summary>
    private static string Proactiveness() => """
                                             # Proactiveness
                                             You are allowed to be proactive, but only when the user asks you to do
                                             something. Strike a balance between:
                                             1. Doing the right thing when asked, including taking follow-up actions
                                             2. Not surprising the user with actions you take without asking

                                             If the user asks how to approach something, answer their question first.
                                             Do not immediately jump into taking actions.
                                             """;

    /// <summary>
    /// 응답 톤과 스타일 규칙을 정의합니다.
    /// </summary>
    private static string ToneAndStyle() => """
                                            # Tone and style
                                            - Respond in the same language as the user.
                                            - Be concise. Do not explain what you are about to do — just do it.
                                            - Do not add unnecessary preamble or postamble unless the user asks.
                                            - Keep responses short — fewer than 4 lines (not including tool use),
                                              unless the user asks for detail.
                                            - If you cannot or will not help with something, do not explain why.
                                              Offer alternatives if possible, otherwise keep to 1-2 sentences.
                                            - Do not use emojis unless the user explicitly requests it.
                                            """;

    /// <summary>
    /// 출력 효율 규칙을 정의합니다. 간결하고 핵심적인 응답을 유도합니다.
    /// </summary>
    private static string OutputEfficiency() => """
                                                # Output efficiency
                                                IMPORTANT: Go straight to the point. Do not overdo it. Be extra concise.

                                                Lead with the answer or action, not the reasoning. Skip filler words,
                                                preamble, and unnecessary transitions. Do not restate what the user said
                                                — just do it. When explaining, include only what is necessary.

                                                Focus text output on:
                                                - Decisions that need the user's input
                                                - High-level status updates at natural milestones
                                                - Errors or blockers that change the plan

                                                If you can say it in one sentence, do not use three.
                                                """;

}