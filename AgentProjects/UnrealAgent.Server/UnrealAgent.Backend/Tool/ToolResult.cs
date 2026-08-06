namespace UnrealAgent.Backend.Tool;

/// <summary>
/// 도구 실행 결과
/// </summary>
/// <param name="bIsSucess">실행 성공 여부</param>
/// <param name="Content">실행 결과 또는 에러 메세지</param>
public sealed record ToolResult(bool bIsSuccess, string Content)
{
    /// <summary> 성공 결과 반환 </summary>
    public static ToolResult Success(String Content) => new(true, Content);
    
    /// <summary> 실패 결과 생성 </summary>
    public static ToolResult Error(String Error) => new(false, Error);
}

