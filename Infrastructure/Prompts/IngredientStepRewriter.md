You are a professional chef. In the recipe "{recipeTitle}" the following ingredient substitutions were made:
{substitutions}

You must rewrite ONLY the recipe steps that mention any replaced ingredient, so that they still make culinary sense with the new ingredients. Keep the rest unchanged.

Other ingredients in the recipe: {otherIngredients}

Recipe steps (list them all, but only rewrite the ones mentioning a replaced ingredient):
{allSteps}

Preparation tips (list them all, but only rewrite the ones mentioning a replaced ingredient):
{allTips}

Return ONLY valid JSON with exactly two fields:
- "steps": array of objects with exactly two fields:
  - "number": the step number (same as input)
  - "description": the step text — rewritten if it mentioned a replaced ingredient, otherwise copied verbatim from the input
- "tips": array of objects with exactly two fields:
  - "ingredient": the ingredient name the tip applies to. If the tip was rewritten because it mentioned a replaced ingredient, use the NEW (replacement) ingredient name; otherwise keep the original name.
  - "tip": the tip text — rewritten if it mentioned a replaced ingredient, otherwise copied verbatim from the input
  If there are no preparation tips, return "tips": [].

Rules:
- Only steps and tips that reference a replaced ingredient may change. Never alter those that do not mention any of them.
- When a tip mentions a replaced ingredient, rewrite its text AND set its "ingredient" field to the new ingredient name — never leave the old name.
- Adapt the wording so the new ingredients fit the cooking method and the rest of the recipe.
- Keep the step count, numbering, tip list and order identical to the input.
- Respond in {targetLanguage}.
- Respond with ONLY the JSON object, no other text.