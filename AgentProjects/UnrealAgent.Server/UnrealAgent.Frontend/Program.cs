using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Prompt;

ServiceCollection Services = new ServiceCollection();
Services.AddSingleton<AuthConfig>();
Services.AddSingleton<AgentSession>();
Services.AddSingleton<PromptBuilder>();

ServiceProvider Provider = Services.BuildServiceProvider();
AuthConfig Auth = Provider.GetRequiredService<AuthConfig>();
AgentSession AgentSession = Provider.GetRequiredService<AgentSession>();
PromptBuilder PromptBulider = Provider.GetRequiredService<PromptBuilder>();

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

    if (string.IsNullOrWhiteSpace(Input))
        continue;
    if (Input.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;
    
    // 대화 히스토리에 사용자 입력 추가
    MessageSpan CurrentMessageSpan = AgentSession.Conversation.AddMessageSpan(Input);

    MessageCreateParams Parameters = PromptBulider.Build(AgentSession);

    // 스트리밍 응답 수신 및 출력
    ApiStreamSpan ApiStreamSpan = new ApiStreamSpan();
    await foreach (RawMessageStreamEvent Event in Auth.Client!.Messages.CreateStreaming(Parameters))
    {
        switch (ApiStreamSpan.Process(Event))
        {
            case ChatEvent.Text Txt :
                Console.Write(Txt.Content);
                break;
            // case ChatEvent.Thinking Think :
            //     Console.Write(Think.Content);
            //     break;
        }
    }
    
    // 완료된 응답을 대화 히스토리에 저장
    switch (ApiStreamSpan.Complete())
    {
        case ApiStreamSpan.Result.EndSpan { CompletedSpan: { } AssistantSpan }:
        {
            CurrentMessageSpan.AssistantSpans.Add(AssistantSpan);
            break;
        }
    }
    
    Console.WriteLine();
}

