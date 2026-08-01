Organize the grocery list provided below into logical supermarket departments.
- Translate all category names into {targetLanguage} (e.g. "*Молоко и яйца*", "*Мясо и рыба*", "*Овощи и фрукты*", "*Бакалея*", "*Специи и приправы*", "*Хлеб и выпечка*", "*Замороженное*", "*Напитки и соки*", "*Соусы*"). Groceries (flour, pasta, canned goods) and spices/seasonings (salt, pepper, herbs) are separate categories.
- Format the output as a clean bullet-point list (•) under each category header.

- MERGE near-duplicate items that describe the same single product the shopper should buy once:
  - Same product in singular and plural (e.g. "Помидор" + "Помидоры" → "Помидоры"), with quantities summed.
  - Same base product used in different preparations (e.g. "Яйца", "Отварные Яйца", "Яйца Для Кляра" → "Яйца"), because the user buys one raw product (raw eggs) and prepares it at home. Quantities are summed.
  - Splitting a combined item into its parts when they belong to different products (e.g. "Соль И Черный Перец" → separate "Соль" and "Перец" items), then merging those parts with any standalone items of the same product (e.g. "Соль" + part from "Соль, Перец" → one "Соль" item).
  - Same product listed with slightly different names (e.g. "Укроп" + "Укроп" across items) — single item.
  - Spices and seasonings follow the same rule: merge all salt into one "Соль" item, all pepper into one "Перец" item, etc.
- When merging, use a natural product name in {targetLanguage}, write quantities in that language, and add up numeric quantities across merged items (e.g. "2 шт" + "3 шт" + "2 шт" → "7 шт"). For "по вкусу"/"по необходимости" amounts, keep them as-is.
- Keep names and amounts in {targetLanguage} (do not translate into English); minor wording normalization for merging is allowed and expected.
- Every distinct product from the input must still appear exactly once — do not drop anything.

- Do not write any introductory text, greetings, explanations, or concluding remarks. Start directly with the first category header.
- Omit categories that have no items.

{flatListString}