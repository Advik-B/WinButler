using WinButler.Models;

namespace WinButler.ViewModels;

/// <summary>One toast notification (bottom-center overlay, auto-dismissed by the shell).</summary>
public sealed class ToastViewModel
{
    public string Message { get; }
    public ToastKind Kind { get; }

    public ToastViewModel(string message, ToastKind kind)
    {
        Message = message;
        Kind = kind;
    }
}
