using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Prompt;
using UnrealAgent.Backend.Tool;
using UnrealAgent.Backend.Tool.Tools;
using UnrealAgent.Frontend.Infrastructure;

// ── WebApplicationBuilder (서비스 등록 + 앱 설정을 담는 빌더) 생성 ──
WebApplicationBuilder Builder = WebApplication.CreateBuilder(args);

// ── Kestrel (요청을 받아서 넘겨주는 서버 엔진) 포트 설정 ──
Builder.WebHost.UseUrls("http://localhost:55558");

// ── 정적 웹 자산 강제 로드 ──
Builder.WebHost.UseStaticWebAssets();

// ── Blazor 서비스 등록 (Razor 컴포넌트 + 서버 측 인터랙티브 모드) ──
Builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// ── HTTP 클라이언트 등록 (외부 API 호출용) ──
Builder.Services.AddHttpClient("WebFetch");

// ── Auth 모듈 ──
Builder.Services.AddSingleton<AuthConfig>();

// ── Agent 모듈 (에이전트 루프 + 세션) ──
Builder.Services.AddSingleton<AgentSession>();

// ── Runtime 모듈 ──
Builder.Services.AddSingleton<PromptBuilder>();

// ── Tool 모듈 ──
Builder.Services.AddSingleton<ToolRegistry>();
Builder.Services.AddSingleton<ToolExecutor>();

// 여기까지 서비스 등록 단계. Build() 이후는 미들웨어/라우팅 설정 단계입니다.
WebApplication App = Builder.Build();

// ── Auth 설정 로드 ──
App.Services.GetRequiredService<AuthConfig>().Load();

// ── 어트리뷰트 기반 자동 스캔 ──
App.Services.GetRequiredService<ToolRegistry>().DiscoverTools(typeof(WebSearch).Assembly);

// ── 미들웨어 파이프라인 ──
App.UseStaticFiles();
App.UseAntiforgery();

// ── Blazor 엔드포인트 (Razor 컴포넌트 라우팅 + 서버 렌더 모드 적용) ──
App.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// ── 서버 실행 (요청 수신 대기 시작) - http://localhost:55558/ ──
App.Run();

/*
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Prompt;
using UnrealAgent.Backend.Tool;
using UnrealAgent.Backend.Tool.Tools;
using Block = UnrealAgent.Backend.Core.Block;

ServiceCollection Services = new ServiceCollection();
Services.AddHttpClient("WebFetch");

// ── Auth 모듈 ──
Services.AddSingleton<AuthConfig>();

// ── Agent 모듈 (에이전트 루프 + 세션) ──
Services.AddSingleton<AgentSession>();

// ── Runtime 모듈 ──
Services.AddSingleton<PromptBuilder>();

// ── Tool 모듈 ──
Services.AddSingleton<ToolRegistry>();
Services.AddSingleton<ToolExecutor>();

ServiceProvider Provider = Services.BuildServiceProvider();
AuthConfig Auth = Provider.GetRequiredService<AuthConfig>();
AgentSession AgentSession = Provider.GetRequiredService<AgentSession>();
PromptBuilder PromptBulider = Provider.GetRequiredService<PromptBuilder>();
Provider.GetRequiredService<ToolRegistry>().DiscoverTools(typeof(WebSearch).Assembly);
ToolExecutor ToolExecutor = Provider.GetRequiredService<ToolExecutor>();

Auth.Load();

if (!Auth.IsApiKeyConfigured())
{
    Console.Write("API Key를 입력하세요 : ");
    string? Key = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(Key))
    {
        Console.WriteLine("API Key가 입력되지 않았습니다.");
        return;
    }
    
    Auth.SetApiKey(Key);
    Console.WriteLine("API Key 저장 완료!");
}

while (true)
{
    // 사용자 입력 대기
    Console.Write("\n> ");
    string? Input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(Input)) continue;

    if (Input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
    
    // 대화 히스토리에 사용자 입력 추가.
    MessageSpan CurrentMessageSpan = AgentSession.Conversation.AddMessageSpan(Input);
    
    // 에이전트 루프 : 도구 실행이 필요하면 API를 반복 호출
    bool bContinue = true;
    while (bContinue)
    {
        // API 요청 파라미터 구성
        MessageCreateParams Parameters = PromptBulider.Build(AgentSession);
        
        // 스트리밍 응답 수신 및 출력
        ApiStreamSpan ApiStreamSpan = new ApiStreamSpan();
        await foreach (RawMessageStreamEvent Event in Auth.Client!.Messages.CreateStreaming(Parameters))
        {
            switch (ApiStreamSpan.Process(Event))
            {
                case ChatEvent.Text Txt:
                    Console.Write(Txt.Content);
                    break;
            }
        }
        
        // 완료된 응답을 대화 히스토리에 저장
        switch (ApiStreamSpan.Complete())
        {
            case ApiStreamSpan.Result.EndSpan { CompletedSpan: { } AssistantSpan }:
            {
                CurrentMessageSpan.AssistantSpans.Add(AssistantSpan);
                bContinue = false;
                break;
            }

            case ApiStreamSpan.Result.ExecuteTools { CompletedSpan : { } AssistantSpan, ToolCalls: { } ToolCalls }:
            {
                CurrentMessageSpan.AssistantSpans.Add(AssistantSpan);
                
                // 도구 실행
                foreach (Block.ToolUse ToolCall in ToolCalls)
                {
                    await foreach (ChatEvent Evt in ToolExecutor.ExecuteAsync(ToolCall, AssistantSpan, AgentSession))
                    {
                        if(Evt is ChatEvent.ToolStart Tool)
                            Console.WriteLine($"\n-- {Tool.Name} : {Tool.Input} 도구 사용 --");
                    }
                }
                
                // 도구 결과를 포함하여 다음 API 호출로 이어감
                break;
            }

            case ApiStreamSpan.Result.Continue { CompletedSpan: { } AssistantSpan }:
            {
                CurrentMessageSpan.AssistantSpans.Add(AssistantSpan);
                
                // 잘린 응답을 이어서 생성
                break;
            }
        }
    }

    Console.WriteLine();
}
*/
