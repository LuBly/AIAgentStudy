using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using UnrealAgent.Backend.Conversation;

namespace UnrealAgent.Frontend.UI.Input;

public partial class ChatInput : IAsyncDisposable
{
    /// <summary>메시지 전송 콜백입니다.</summary>
    [Parameter] public EventCallback<UserInput> OnSend { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// <summary>현재 입력 텍스트입니다.</summary>
    private string InputText = "";

    /// <summary>메시지 입력 textarea 엘리먼트입니다.</summary>
    private ElementReference InputElement;

    /// <summary>Enter 제출 등록을 해제하기 위한 JS 참조입니다.</summary>
    private IJSObjectReference? KeyHandler;

    private DotNetObjectReference<ChatInput>? SelfRef;

    /// <summary>최초 렌더링 이후 Enter 제출 키 핸들러를 등록합니다.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            SelfRef = DotNetObjectReference.Create(this);
            KeyHandler = await JS.InvokeAsync<IJSObjectReference>(
                "chatInput.registerEnterSubmit", InputElement, SelfRef);
        }
    }

    /// <summary>Enter 키(Shift 없음) 입력 시 JS에서 호출됩니다.</summary>
    /// <remarks>JS interop 호출은 일반 Razor 이벤트와 달리 자동으로 리렌더링되지 않으므로 StateHasChanged가 필요합니다.</remarks>
    [JSInvokable]
    public async Task SubmitFromJs()
    {
        await HandleSubmit();
        StateHasChanged();
    }

    /// <summary>폼 제출 시 메시지를 전송합니다.</summary>
    private async Task HandleSubmit()
    {
        string Trimmed = InputText.Trim();

        if (string.IsNullOrEmpty(Trimmed))
            return;

        InputText = "";
        await OnSend.InvokeAsync(Trimmed);
    }

    public async ValueTask DisposeAsync()
    {
        if (KeyHandler is not null)
        {
            await KeyHandler.InvokeVoidAsync("dispose");
            await KeyHandler.DisposeAsync();
        }

        SelfRef?.Dispose();
    }
}