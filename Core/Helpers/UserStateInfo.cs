using Core.Enums;

namespace Core.Helpers
{
    public class UserStateInfo
    {
        public BotState State { get; set; } = BotState.None;
        public string LastPreferences { get; set; } = string.Empty;
        public DateOnly TargetDate { get; set; }
        public string? LastSubstituteIngredient { get; set; }
    }
}