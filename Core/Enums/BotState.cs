namespace Core.Enums
{
    public enum BotState
    {
        None,
        WaitingForCustomDate,
        WaitingForAiPreferences,
        WaitingForSearchIngredients,
        WaitingForSubstituteIngredient,
        WaitingForSubstituteRecipe,
        WaitingForServings
    }
}