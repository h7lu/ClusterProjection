using System.Collections.Generic;
using Verse;

namespace ClusterProjection;

internal static class CP_Debug
{
    private static readonly Dictionary<string, string> LastMessageByKey = new();
    private static readonly Dictionary<string, int> LastTickByKey = new();

    public static void Message(string key, string message, int minTickInterval = 0, bool onlyOnChange = false)
    {
        var tick = Find.TickManager?.TicksGame ?? -1;

        if (onlyOnChange && LastMessageByKey.TryGetValue(key, out var lastMessage) && lastMessage == message)
            return;

        if (minTickInterval > 0
            && tick >= 0
            && LastTickByKey.TryGetValue(key, out var lastTick)
            && tick - lastTick < minTickInterval)
        {
            return;
        }

        LastMessageByKey[key] = message;
        LastTickByKey[key] = tick;
        Log.Message($"[ClusterProjection] {key}: {message}");
    }
}