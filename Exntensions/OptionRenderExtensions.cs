using Optional;

namespace Eggbox.Exntensions;

using Microsoft.AspNetCore.Components;

public static class OptionRenderExtensions
{
    public static RenderFragment Render<T>(this Option<T> option, Func<T, RenderFragment> render) =>
        option.Match(
            some => render(some),
            () => builder => { }
        );
    
    public static string Render(this Option<string> option)
    {
        return option.Match(
            some: v => v ?? "",
            none: () => ""
        );
    }
}