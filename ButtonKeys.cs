using System.Runtime.CompilerServices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.HwInfo;

/// <summary>
/// Identifies the button a command runs for, so per-button state (a paging tile's current page)
/// follows exactly the pressed button. Uses <c>CommandContext.ButtonKey</c> (SDK 1.26.0); an older
/// host has no such member, so it is only touched when the host's SDK is new enough, and the
/// caller's parameter-derived fallback is used otherwise.
/// </summary>
internal static class ButtonKeys
{
    private static readonly bool HostProvidesKey = SdkInfo.Version >= new Version(1, 26, 0);

    public static string For(CommandContext ctx, string fallback) =>
        (HostProvidesKey ? Read(ctx) : null) ?? fallback;

    // Kept out of line: the JIT resolves the member when it compiles this method, which must not
    // happen on a host whose SDK lacks it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Read(CommandContext ctx) => ctx.ButtonKey;
}
