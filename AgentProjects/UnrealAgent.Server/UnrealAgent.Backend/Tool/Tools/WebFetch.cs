using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Tool.Attributes;
using ReverseMarkdown;

namespace UnrealAgent.Backend.Tool.Tools;

/// <summary>
/// 웹 페이지를 가져와 Markdown으로 변환한 뒤, AI 요약을 통해 사용자 프롬프트에 답변합니다.
/// </summary>
[AgentTool("web_fetch", """
                        Fetches content from a specified URL and processes it using an AI model.
                        - Takes a URL and a prompt as input.
                        - Fetches the URL content, converts HTML to markdown.
                        - Processes the content with the prompt using a small, fast model.
                        - Returns the model's response about the content.
                        - Use this tool when you need to retrieve and analyze web content.

                        Usage notes:
                          - The URL must be a fully-formed valid URL.
                          - HTTP URLs will be automatically upgraded to HTTPS.
                          - The prompt should describe what information you want to extract from the page.
                          - This tool is read-only and does not modify any files.
                          - Results may be summarized if the content is very large.
                          - Includes a self-cleaning 15-minute cache for faster responses when repeatedly accessing the same URL.
                        """)]

public class WebFetch(AuthConfig Auth, IHttpClientFactory HttpClientFactory) : AgentTool<WebFetch.Input>
{
    public sealed record Input(
        [property: JsonPropertyName("url")]
        [property: Description("The URL to fetch content from")]
        string Url,
        
        [property: JsonPropertyName("prompt")]
        [property: Description("Instructions describing what information to extract or summarize from the fetched page")]
        string Prompt);

    /// <summary>URL 최대 길이.</summary>
    private const int MaxUrlLength = 2_000;

    /// <summary>HTTP 응답 최대 크기 (10MB).</summary>
    private const int MaxResponseBytes = 10 * 1024 * 1024;

    /// <summary>컨텐츠 자르기 임계값 (100K자).</summary>
    private const int MaxContentChars = 100_000;
    
    /// <summary>캐시 TTL (15분).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    
    /// <summary>캐시 최대 크기 (50MB).</summary>
    private const long MaxCacheBytes = 50 * 1024 * 1024;
    
    /// <summary>Haiku 요약 최대 출력 토큰.</summary>
    private const int SummaryMaxTokens = 4096;
    
    /// <summary>캐시 항목.</summary>
    private sealed record CacheEntry(string Content, DateTime ExpiresAt, long SizeBytes);
    
    /// <summary>URL → (컨텐츠, 만료시각) LRU 캐시.</summary>
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();

    /// <summary>신뢰 도메인 목록. 이 도메인의 컨텐츠는 비저작권 보호 프롬프트를 사용.</summary>
    private static readonly HashSet<string> TrustedDomains =
    [
        "docs.unrealengine.com",
        "learn.microsoft.com",
        "developer.mozilla.org",
        "docs.github.com",
        "docs.python.org",
        "docs.docker.com",
        "docs.aws.amazon.com",
        "cloud.google.com",
        "docs.oracle.com",
        "docs.unity3d.com",
        "docs.godotengine.org",
        "kubernetes.io",
        "react.dev",
        "vuejs.org",
        "angular.dev",
        "nextjs.org",
        "nuxt.com",
        "svelte.dev",
        "tailwindcss.com",
        "typescriptlang.org",
        "rust-lang.org",
        "go.dev",
        "dotnet.microsoft.com",
        "kotlinlang.org",
        "docs.swift.org",
        "docs.flutter.dev",
        "pytorch.org",
        "numpy.org",
        "pandas.pydata.org",
        "graphql.org",
        "www.terraform.io"
    ];
    
    /// <summary>HTML → Markdown 변환기.</summary>
    private static readonly Converter MarkdownConverter = new(new Config
    {
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true
    });
    
    /// <summary> URL에서 컨텐츠를 가져와서 AI 요약을 수행 </summary>
    protected override async Task<ToolResult> ExecuteAsync(Input Args, AgentSession Session, CancellationToken Ct)
    {
        // 1. 인증 검증
        if (Auth.Client is null)
            return ToolResult.Error("Authentication is not configured");
        
        // 2. URL 검증 및 HTTPS 승격
        string? validateUrl = ValidateUrl(Args.Url, out string? UrlError);
        if(validateUrl is null)
            return ToolResult.Error(UrlError!);
        
        // 3. 캐시 조회 - 히트 시 Fetch 건너뛰고 바로 요약
        if(TryGetCatched(validateUrl, out string? CatchedContent))
            return await ApplyWithHaikuAsync(CatchedContent, Args.Prompt, validateUrl, Ct);
        
        // 4. HTTPS Fetch
        string Content;
        try
        {
            Content = await FetchAsync(validateUrl, Ct);
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Error($"Request timed out : {validateUrl}");
        }
        catch (HttpRequestException Ex)
        {
            return ToolResult.Error($"HTTP request failed : {Ex.Message}");
        }
        
        // 5. 캐시 저장
        SetCache(validateUrl, Content);
        
        // 6. AI 요약
        return await ApplyWithHaikuAsync(Content, Args.Prompt, validateUrl, Ct);

    }

    /// <summary> URL을 검증하고 HTTPS로 승격 실패 시 null과 에러 메시지를 반환 </summary>
    private string? ValidateUrl(string RawUrl, out string? Error)
    {
        Error = null;
        
        // 빈 URL 차단
        if (string.IsNullOrWhiteSpace(RawUrl))
        {
            Error = "URL is Empty";
            return null;
        }
        
        // 길이 제한
        if (RawUrl.Length > MaxUrlLength)
        {
            Error = $"URL is too long (max {MaxUrlLength} characters)";
            return null;
        }
        
        // HTTP -> HTTPS 승격
        string Url = RawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + RawUrl[7..]
            : RawUrl;
        
        // URL 형식 검증
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? Parsed))
        {
            Error = $"Invalid URL : {RawUrl}";
            return null;
        }
        
        // HTTPS만 허용 (ftp:// 등 차단)
        if (Parsed.Scheme != "https")
        {
            Error = "Only HTTPS URLs are supported";
            return null;
        }
        
        // 인증정보 포함 차단 ( 예: https://user:pass@host)
        if (!string.IsNullOrEmpty(Parsed.UserInfo))
        {
            Error = "URLs with credentials are not supported";
            return null;
        }

        return Url;
    }
    
    /// <summary> 캐시에서 컨텐츠를 조회. 만료된 항목은 제거 </summary>
    private bool TryGetCatched(string Url, out string Content)
    {
        if (Cache.TryGetValue(Url, out CacheEntry? Entry) && Entry.ExpiresAt > DateTime.UtcNow)
        {
            Content = Entry.Content;
            return true;
        }

        Content = null;
        return false;
    }

    /// <summary>
    /// Url에서 컨텐츠를 가지고 와서 Markdown으로 변환
    /// text/html이면 ReverseMarkdown으로 변환하고, 나머지는 패스
    /// </summary>
    private async Task<string> FetchAsync(string Url, CancellationToken Ct)
    {
        HttpClient Client = HttpClientFactory.CreateClient("WebFetch");
        
        // 스트리밍으로 크기 제한을 적용
        using HttpResponseMessage Response = await Client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, Ct);
        Response.EnsureSuccessStatusCode();

        string? ContentType = Response.Content.Headers.ContentType?.MediaType;
        
        // 텍스트가 아닌 경우 거부
        bool bIsText = ContentType is null
                       || ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                       || ContentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                       || ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase);

        if (!bIsText)
            throw new HttpRequestException($"Non-text content type : {ContentType}");
        
        // 크기 제한을 적용하면서 읽는다.
        await using Stream Stream = await Response.Content.ReadAsStreamAsync(Ct);
        using StreamReader Reader = new(Stream);
        char[] Buffer = new char[MaxResponseBytes];
        int TotalRead = 0;
        int Read;
        
        while(TotalRead < Buffer.Length && (Read = await Reader.ReadAsync(Buffer.AsMemory(TotalRead, Buffer.Length - TotalRead), Ct)) > 0)
            TotalRead += Read;

        string RawContent = new(Buffer, 0, TotalRead);
        
        // text/html이면 Markdown으로 변환
        if (ContentType is not null &&
            ContentType.Contains("html", StringComparison.OrdinalIgnoreCase)) 
            return ConvertToMarkdown(RawContent);
        
        return RawContent;
    }

    private string ConvertToMarkdown(string rawContent)
    {
        return MarkdownConverter.Convert(rawContent);
    }
    
    /// <summary> 컨텐츠를 캐시에 저장하고, 만료된 항목을 정리 </summary>
    private void SetCache(string url, string content)
    {
        long SizeBytes = content.Length * sizeof(char);
        CacheEntry Entry = new(content, DateTime.UtcNow.Add(CacheTtl), SizeBytes);
        Cache[url] = Entry;

        CleanExpired();
    }
    
    /// <summary> 만료된 캐시 항목을 제거, 총 크기가 제한을 초과하면 오래된 것부터 제거 </summary>
    private void CleanExpired()
    {
        DateTime Now = DateTime.UtcNow;
        // 만료 항목 제거
        foreach (KeyValuePair<string, CacheEntry> pair in Cache)
        {
            if(pair.Value.ExpiresAt <= Now)
                Cache.TryRemove(pair.Key, out _);
        }
        
        // 크기 제한 초과 시 오래된 것부터 제거
        long totalSize = 0;
        foreach(CacheEntry entry in Cache.Values)
            totalSize += entry.SizeBytes;

        if (totalSize <= MaxCacheBytes) return;
        List<KeyValuePair<string, CacheEntry>> sorted = [.. Cache.OrderBy(P => P.Value.ExpiresAt)];
        foreach (KeyValuePair<string, CacheEntry> pair in sorted)
        {
            if (totalSize <= MaxCacheBytes) break;
            if(Cache.TryRemove(pair.Key, out CacheEntry? removed))
                totalSize -= removed.SizeBytes;
        }
    }

    /// <summary>
    /// Haiku 4.5를 사용하여 컨텐츠를 요약
    /// 신뢰 도메인이고 100K자 미만이면 AI 요약을 건너뛴다.
    /// </summary>
    private async Task<ToolResult> ApplyWithHaikuAsync(string content, string prompt, string url, CancellationToken ct)
    {
        // 1. 신뢰 도메인 여부 확인
        bool bIsTrusted = IsTrustedDomain(url);
        
        // 2. 100K자 초과 시 자르기
        bool bTruncated = content.Length > MaxContentChars;
        if(bTruncated)
            content = content[..MaxContentChars] + "\n\n[Content truncated due to Length...]";
        
        // 3. 신뢰 도메인 + 100K 미만이면 원문 그대로 반환 (Haiku 호출 생략)
        if (bIsTrusted && !bTruncated)
            return ToolResult.Success($"Web page content :\n---\n{content}\n---");
        
        // 4.저작권 보호 프롬프트 구성 (신뢰/비신뢰에 따라 다른 지침)
        string CopyrightGuidance = bIsTrusted
            ? "Provide a concise response based on the content above. Include relevant details, code examples, and documentation excerpts as needed."
            : """
              Provide a concise response based only on the content above. In your response:
               - Enforce a strict 125-character maximum for quotes from any source document.
               - Use quotation marks for exact language from articles; any language outside of the quotation should never be word-for-word the same.
               - Never produce or reproduce exact song lyrics.
              """;
        
        // 5. 페이지 내용 + 사용자 프롬프트 + 저작권 지침을 합쳐서 Haiku에 전달
        string UserPrompt = $"""
                             Web page content:
                             ---
                             {content}
                             ---

                             {prompt}

                             {CopyrightGuidance}
                             """;

        try
        {
            // 6. Haiku 4.5 서브 호출로 요약 생성
            Message Response = await Auth.Client!.Messages.Create(new MessageCreateParams
            {
                Model = "claude-haiku-4.5-20251001",
                MaxTokens = SummaryMaxTokens,
                System = new List<TextBlockParam>(),
                Messages =
                [
                    new MessageParam
                    {
                        Role = Role.User,
                        Content = UserPrompt
                    }
                ]
            }, ct);

            // 7. 응답에서 텍스트 블록만 추출
            string resultText = string.Join("", Response.Content
                .Where(B => B.TryPickText(out _))
                .Select(B =>
                {
                    B.TryPickText(out TextBlock? T);
                    return T!.Text;
                }));

            return ToolResult.Success(resultText);
        }
        catch (Exception e)
        {
            return ToolResult.Error($"AI summarization failed: {e.Message}");
        }
        
        // 7.
    }
    
    /// <summary> url이 신뢰 도메인에 속하는지 확인 </summary>
    private bool IsTrustedDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
            return false;
        
        string Host = parsed.Host;
        
        // 정확히 일치하거나 서브도메인 일치
        foreach (string domain in TrustedDomains)
        {
            if (Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }
}