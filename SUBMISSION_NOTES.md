# Submission Note

## Approach

The solution uses C# Selenium with explicit waits to fill and verify First Name, Gender, and State. It identifies controls through visible labels, accessibility attributes, control type, and nearby context instead of fixed IDs, names, classes, XPath, or page order. It then reads the values back to verify the completed form.

The traversal supports the main document, same-origin iframes, and open Shadow DOM. When similar controls cannot be distinguished safely, the automation reports ambiguity rather than selecting a field at random.

The automation supports regular DOM, same-origin iframes, and open Shadow DOM.

## Limitations

- The CloudQA page recursively embeds itself through an iframe inside Shadow DOM. Because each copy creates another copy, `--max-recursion-depth` defines a safe, finite test scope.
- Closed Shadow DOM cannot be accessed by normal Selenium automation.
- Cross-origin iframes can be restricted by browser security rules.
- The solution requires meaningful visible labels or accessibility information for First Name, Gender, and State.
- If equally suitable controls exist in the same form context without useful surrounding context, the automation reports ambiguity rather than guessing.


No AI or LLM locator logic, additional packages, or fixed IDs/XPath locators are used.
