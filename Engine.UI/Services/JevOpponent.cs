using Engine.Chess.Play;

namespace Engine.UI.Services;

public static class JevOpponent {
    public static BotProfile Profile { get; } = new() {
        Name = "Jev", Elo = 0, Initials = "JV", Accent = "#9d76dc",
        Style = "System One chooses among legal moves. Requires the local Jev host.",
        Depth = 0, ThinkTimeMilliseconds = 0, AllowedLoss = 0, MistakeChance = 0,
        UseOpeningBook = false,
    };
}
