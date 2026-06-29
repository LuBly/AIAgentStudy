using Microsoft.Extensions.DependencyInjection;
using UnrealAgent.Backend.Auth;

ServiceCollection Services = new ServiceCollection();
Services.AddSingleton<AuthConfig>();

ServiceProvider Provider = Services.BuildServiceProvider();
AuthConfig Auth = Provider.GetRequiredService<AuthConfig>();

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