# Five-Minute Recording Outline

1. **0:00–0:30 — Introduction**
   - Show the repository and explain that this is a C# Selenium solution for First Name, Gender, and State.

2. **0:30–1:20 — Configuration**
   - Open `Program.cs` and point out the configurable URL, test values, visible/headless mode, pause duration, and recursion depth.

3. **1:20–2:20 — Resilient matching**
   - Explain that the solution uses labels, accessibility attributes, input type, and nearby context rather than IDs, classes, XPath, or page order.
   - Mention ambiguity handling: equally suitable fields cause a clear failure instead of a guess.

4. **2:20–3:20 — Iframe and Shadow DOM support**
   - Explain that the practice page has regular frames and open Shadow DOM forms.
   - Explain that the page recursively embeds itself, so `--max-recursion-depth` sets a deliberate finite boundary.

5. **3:20–4:30 — Run the automation**
   - Run:

     ```powershell
     dotnet run -- --visible --pause-seconds 90 --max-recursion-depth 3
     ```

   - Show Chrome filling First Name, selecting Female, and selecting India.
   - Show the terminal `PASSED` message.

6. **4:30–5:00 — Limitations and close**
   - State that meaningful labels/accessibility are required and closed Shadow DOM/cross-origin frames are outside normal Selenium access.
   - Point to the README for setup and commands.
