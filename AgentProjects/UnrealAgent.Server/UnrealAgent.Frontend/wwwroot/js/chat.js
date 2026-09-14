// 채팅 입력창의 Enter/Shift+Enter 키 동작을 처리합니다.
// Enter: 즉시 제출, Shift+Enter: 줄바꿈 (textarea 기본 동작 유지).
window.chatInput = {
    registerEnterSubmit: function (textarea, dotNetRef) {
        function onKeyDown(e) {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("SubmitFromJs");
            }
        }

        textarea.addEventListener("keydown", onKeyDown);

        return {
            dispose: function () {
                textarea.removeEventListener("keydown", onKeyDown);
            }
        };
    }
};
