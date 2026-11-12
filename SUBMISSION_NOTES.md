# Submission Note

This solution favors reliable, explainable Selenium automation over fragile page-specific locators. It discovers the required controls from user-visible and accessible semantics, handles reordered fields and changed technical attributes, and reports ambiguity instead of silently choosing a similar control.

The automation supports regular DOM, same-origin iframes, and open Shadow DOM. The supplied CloudQA page recursively embeds itself through an iframe inside Shadow DOM; this cannot be exhausted because each copy contains another copy. The `--max-recursion-depth` setting makes the finite test scope explicit while preventing an endless traversal.

No AI or LLM locator logic, additional packages, or fixed IDs/XPath locators are used.
