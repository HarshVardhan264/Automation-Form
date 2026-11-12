using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

var settings = RunSettings.Parse(args);

var options = new ChromeOptions();
if (settings.Headless)
    options.AddArgument("--headless=new");
options.AddArgument("--window-size=1440,1200");

ChromeDriver? driver = null;
var exitCode = 0;

try
{
    driver = new ChromeDriver(options);
    driver.Navigate().GoToUrl(settings.Url);

    var form = new ResilientPracticeForm(driver, TimeSpan.FromSeconds(15), settings.FormTitle, settings.MaxRecursionDepth);
    var filledFormCount = form.FillAll(settings.FirstName, settings.Gender, settings.State);
    var verifiedFormCount = form.AssertAllValues(settings.FirstName, settings.Gender, settings.State);
    if (filledFormCount != verifiedFormCount)
        throw new InvalidOperationException($"Filled {filledFormCount} forms but verified {verifiedFormCount} forms.");

    var formDescription = string.IsNullOrWhiteSpace(settings.FormTitle) ? "matching forms" : $"forms titled '{settings.FormTitle}'";
    Console.WriteLine($"PASSED {filledFormCount} {formDescription}: First Name='{settings.FirstName}', Gender='{settings.Gender}', State='{settings.State}'.");
}
catch (Exception exception)
{
    exitCode = 1;
    Console.Error.WriteLine($"FAILED: {exception.Message}");

    if (driver is ITakesScreenshot screenshotDriver)
    {
        try
        {
            var screenshotPath = Path.GetFullPath($"failure-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            screenshotDriver.GetScreenshot().SaveAsFile(screenshotPath);
            Console.Error.WriteLine($"Failure screenshot saved to: {screenshotPath}");
        }
        catch (Exception screenshotException)
        {
            Console.Error.WriteLine($"Could not save a failure screenshot: {screenshotException.Message}");
        }
    }
}
finally
{
    if (driver is not null && !settings.Headless && settings.PauseSeconds > 0)
    {
        Console.WriteLine($"Chrome will stay open for {settings.PauseSeconds} seconds. Use --pause-seconds 0 to close it immediately.");
        await Task.Delay(TimeSpan.FromSeconds(settings.PauseSeconds));
    }

    try
    {
        driver?.Quit();
    }
    catch (WebDriverException exception)
    {
        exitCode = 1;
        Console.Error.WriteLine($"Could not close Chrome cleanly: {exception.Message}");
    }
    finally
    {
        driver?.Dispose();
    }
}

Environment.ExitCode = exitCode;

internal sealed record RunSettings(
    string Url,
    string FormTitle,
    string FirstName,
    string Gender,
    string State,
    bool Headless,
    int PauseSeconds,
    int MaxRecursionDepth)
{
    private const string DefaultUrl = "https://app.cloudqa.io/home/AutomationPracticeForm";

    public static RunSettings Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
                continue;

            var key = args[index][2..];
            if (key.Equals("headless", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("visible", StringComparison.OrdinalIgnoreCase))
            {
                values[key] = "true";
                continue;
            }

            if (index + 1 >= args.Length)
                throw new ArgumentException($"Missing value for --{key}.");
            values[key] = args[++index];
        }

        string Get(string key, string fallback) =>
            values.TryGetValue(key, out var value)
                ? value
                : Environment.GetEnvironmentVariable($"CLOUDQA_{key.ToUpperInvariant().Replace('-', '_')}") ?? fallback;

        var commandRequestsHeadless = values.ContainsKey("headless");
        var commandRequestsVisible = values.ContainsKey("visible");
        if (commandRequestsHeadless && commandRequestsVisible)
            throw new ArgumentException("Use either --visible or --headless, not both.");

        var environmentRequestsHeadless = bool.TryParse(
            Environment.GetEnvironmentVariable("CLOUDQA_HEADLESS"),
            out var environmentHeadless) && environmentHeadless;
        var headless = commandRequestsHeadless || (!commandRequestsVisible && environmentRequestsHeadless);

        var defaultPauseSeconds = headless ? 0 : 90;
        if (!int.TryParse(Get("pause-seconds", defaultPauseSeconds.ToString()), out var pauseSeconds) || pauseSeconds < 0)
            throw new ArgumentException("--pause-seconds must be a non-negative whole number.");

        if (!int.TryParse(Get("max-recursion-depth", "3"), out var maxRecursionDepth) || maxRecursionDepth < 0)
            throw new ArgumentException("--max-recursion-depth must be a non-negative whole number.");

        return new RunSettings(
            Get("url", DefaultUrl),
            Get("form-title", ""),
            Get("first-name", "Asha"),
            Get("gender", "Female"),
            Get("state", "India"),
            headless,
            pauseSeconds,
            maxRecursionDepth);
    }
}

/// <summary>
/// Locates controls from what a user sees: a form title, label text, accessible name, role and input type.
/// It deliberately avoids page-specific id, name, class, XPath, and CSS values.
/// </summary>
internal sealed class ResilientPracticeForm
{
    private readonly IWebDriver _driver;
    private readonly IJavaScriptExecutor _javascript;
    private readonly WebDriverWait _wait;
    private readonly string _formTitle;
    private readonly int _maxRecursionDepth;

    public ResilientPracticeForm(
        IWebDriver driver,
        TimeSpan timeout,
        string formTitle,
        int maxRecursionDepth)
    {
        _driver = driver;
        _javascript = (IJavaScriptExecutor)driver;
        _formTitle = formTitle;
        _maxRecursionDepth = maxRecursionDepth;
        _wait = new WebDriverWait(driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(150)
        };
        _wait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));
    }

    public int FillAll(string firstName, string gender, string state) =>
        VisitAllForms(scope => Fill(scope, firstName, gender, state));

    public int AssertAllValues(string firstName, string gender, string state) =>
        VisitAllForms(scope => AssertValues(scope, firstName, gender, state));

    private void Fill(IWebElement scope, string firstName, string gender, string state)
    {
        var firstNameBox = WaitFor(() => FindInScope(scope, "first-name", null), "First Name text box");
        ScrollIntoView(firstNameBox);
        firstNameBox.Clear();
        firstNameBox.SendKeys(firstName);

        var genderRadio = WaitFor(() => FindInScope(scope, "gender", gender), $"Gender option '{gender}'");
        ScrollIntoView(genderRadio);
        if (!genderRadio.Selected)
            Click(genderRadio);

        var stateControl = WaitFor(() => FindInScope(scope, "state", null), "State dropdown");
        ScrollIntoView(stateControl);
        SelectState(stateControl, state);
    }

    private void AssertValues(IWebElement scope, string firstName, string gender, string state)
    {
        var actualFirstName = WaitFor(() => FindInScope(scope, "first-name", null), "First Name text box").GetDomProperty("value");
        if (!string.Equals(actualFirstName, firstName, StringComparison.Ordinal))
            throw new InvalidOperationException("The First Name value was not retained.");

        var selectedGender = WaitFor(() => FindInScope(scope, "gender", gender), $"Gender option '{gender}'");
        if (!selectedGender.Selected && !string.Equals(selectedGender.GetAttribute("aria-checked"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Gender '{gender}' is not selected.");

        var stateControl = WaitFor(() => FindInScope(scope, "state", null), "State dropdown");
        var actualState = stateControl.TagName.Equals("select", StringComparison.OrdinalIgnoreCase)
            ? new SelectElement(stateControl).SelectedOption.Text.Trim()
            : (stateControl.GetDomProperty("value") ?? stateControl.Text).Trim();
        if (!string.Equals(actualState, state, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"State is '{actualState}', not '{state}'.");
    }

    private IWebElement WaitFor(Func<IWebElement?> find, string description)
    {
        try
        {
            return _wait.Until(_ => find() ?? throw new NoSuchElementException());
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new InvalidOperationException(
                $"Could not find a unique visible {description} within {_wait.Timeout.TotalSeconds:0} seconds.",
                exception);
        }
    }

    private IWebElement? FindInScope(IWebElement? scope, string field, string? option) =>
        scope is null
            ? null
            : (IWebElement?)_javascript.ExecuteScript(SemanticScripts.FindControl, scope, field, option);

    private int VisitAllForms(Action<IWebElement> action)
    {
        _driver.SwitchTo().DefaultContent();
        try
        {
            var urlAncestry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var formCount = VisitCurrentDocument(action, urlAncestry, recursionDepth: 0);
            if (formCount == 0)
                throw new InvalidOperationException("Could not find any visible forms containing First Name, Gender, and State.");
            return formCount;
        }
        finally
        {
            _driver.SwitchTo().DefaultContent();
        }
    }

    private int VisitCurrentDocument(
        Action<IWebElement> action,
        HashSet<string> urlAncestry,
        int recursionDepth,
        bool waitForForm = false)
    {
        var currentUrl = (string?)_javascript.ExecuteScript("return document.location.href;") ?? string.Empty;
        var addedToAncestry = urlAncestry.Add(currentUrl);
        var nextRecursionDepth = recursionDepth;
        if (!addedToAncestry)
        {
            if (recursionDepth >= _maxRecursionDepth)
                return 0;
            nextRecursionDepth++;
        }

        try
        {
            var formCount = ProcessScopes(waitForForm ? WaitForScopes() : FindScopes(null), action);

            foreach (var shadowHost in FindShadowHosts(null))
                formCount += VisitShadowRoot(shadowHost, action, urlAncestry, nextRecursionDepth);

            foreach (var frame in FindFrames(null))
                formCount += VisitFrame(frame, action, urlAncestry, nextRecursionDepth);

            return formCount;
        }
        finally
        {
            if (addedToAncestry)
                urlAncestry.Remove(currentUrl);
        }
    }

    private int VisitShadowRoot(
        IWebElement shadowHost,
        Action<IWebElement> action,
        HashSet<string> urlAncestry,
        int recursionDepth)
    {
        var formCount = ProcessScopes(FindScopes(shadowHost), action);

        foreach (var frame in FindFrames(shadowHost))
            formCount += VisitFrame(frame, action, urlAncestry, recursionDepth);

        return formCount;
    }

    private int VisitFrame(
        IWebElement frame,
        Action<IWebElement> action,
        HashSet<string> urlAncestry,
        int recursionDepth)
    {
        var switchedToFrame = false;
        try
        {
            _driver.SwitchTo().Frame(frame);
            switchedToFrame = true;
            return VisitCurrentDocument(action, urlAncestry, recursionDepth, waitForForm: true);
        }
        finally
        {
            if (switchedToFrame)
                _driver.SwitchTo().ParentFrame();
        }
    }

    private static int ProcessScopes(IEnumerable<IWebElement> scopes, Action<IWebElement> action)
    {
        var formCount = 0;
        foreach (var scope in scopes)
        {
            action(scope);
            formCount++;
        }
        return formCount;
    }

    private IReadOnlyCollection<IWebElement> FindScopes(IWebElement? shadowHost) =>
        FindElements(SemanticScripts.FindScopes, shadowHost, _formTitle);

    private IReadOnlyCollection<IWebElement> WaitForScopes()
    {
        var frameWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(2))
        {
            PollingInterval = TimeSpan.FromMilliseconds(150)
        };
        frameWait.IgnoreExceptionTypes(typeof(StaleElementReferenceException), typeof(NoSuchElementException));

        try
        {
            return frameWait.Until(_ =>
            {
                var scopes = FindScopes(null);
                return scopes.Count > 0 ? scopes : null;
            }) ?? Array.Empty<IWebElement>();
        }
        catch (WebDriverTimeoutException)
        {
            return Array.Empty<IWebElement>();
        }
    }

    private IReadOnlyCollection<IWebElement> FindShadowHosts(IWebElement? shadowHost) =>
        FindElements(SemanticScripts.FindShadowHosts, shadowHost);

    private IReadOnlyCollection<IWebElement> FindFrames(IWebElement? shadowHost) =>
        FindElements(SemanticScripts.FindFrames, shadowHost);

    private IReadOnlyCollection<IWebElement> FindElements(string script, params object?[] arguments)
    {
        var result = _javascript.ExecuteScript(script, arguments);
        return result is IEnumerable<object> elements
            ? elements.OfType<IWebElement>().ToArray()
            : Array.Empty<IWebElement>();
    }

    private void SelectState(IWebElement control, string state)
    {
        if (control.TagName.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            new SelectElement(control).SelectByText(state);
            return;
        }

        Click(control);
        var option = WaitFor(
            () => (IWebElement?)_javascript.ExecuteScript(SemanticScripts.FindVisibleOption, control, state),
            $"State option '{state}' associated with the opened dropdown");
        Click(option);
    }

    private void ScrollIntoView(IWebElement element) =>
        _javascript.ExecuteScript("arguments[0].scrollIntoView({ block: 'center', inline: 'nearest' });", element);

    private void Click(IWebElement element)
    {
        try
        {
            element.Click();
        }
        catch (ElementClickInterceptedException)
        {
            // Some forms overlap visually on this practice page. The native click remains the
            // preferred path; this fallback updates native controls and raises normal events.
            _javascript.ExecuteScript("""
                const control = arguments[0];
                if ('checked' in control) {
                  control.checked = true;
                  control.dispatchEvent(new Event('input', { bubbles: true }));
                  control.dispatchEvent(new Event('change', { bubbles: true }));
                } else {
                  control.click();
                }
                """, element);
        }
    }
}

internal static class SemanticScripts
{
    // These are generic DOM/ARIA primitives, rather than selectors for this page's markup.
    // Each scope represents one complete form, even when the page contains duplicates.
    public const string FindScopes = """
        const [shadowHost, wantedFormTitle] = arguments;
        const root = shadowHost ? shadowHost.shadowRoot : document;
        if (!root) return [];
        const wantedTitle = (wantedFormTitle || '').replace(/\s+/g, ' ').trim().toLowerCase();
        const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
        const norm = s => (s || '').replace(/\s+/g, ' ').trim().toLowerCase();
        const has = (text, words) => words.every(word => new RegExp('(^|\\s)' + word + '(?=\\s|$)', 'i').test(text));
        const visitComposed = (node, visitor, seen = new Set()) => {
          if (!node || seen.has(node)) return;
          seen.add(node);
          if (node.nodeType === Node.ELEMENT_NODE) {
            visitor(node);
            if (node.shadowRoot) visitComposed(node.shadowRoot, visitor, seen);
            if (node.tagName === 'SLOT') node.assignedElements({ flatten: true }).forEach(child => visitComposed(child, visitor, seen));
          }
          [...(node.children || [])].forEach(child => visitComposed(child, visitor, seen));
        };
        const controlsIn = e => {
          const controls = [];
          const addControl = control => {
            if (control.matches?.('input,textarea,select,[role="radio"],[role="combobox"]') && visible(control) &&
              !(control.tagName === 'INPUT' && /^(hidden|submit|reset|button)$/i.test(control.type))) controls.push(control);
          };
          if (shadowHost) visitComposed(e, addControl);
          else e.querySelectorAll('input,textarea,select,[role="radio"],[role="combobox"]').forEach(addControl);
          return controls;
        };
        const textIn = e => {
          if (!shadowHost) return norm(e.innerText);
          const text = [];
          const collectText = node => {
            if (node.nodeType === Node.TEXT_NODE) text.push(node.textContent);
          };
          const walk = (node, seen = new Set()) => {
            if (!node || seen.has(node)) return;
            seen.add(node);
            node.childNodes.forEach(child => {
              collectText(child);
              if (child.nodeType === Node.ELEMENT_NODE || child.nodeType === Node.DOCUMENT_FRAGMENT_NODE) walk(child, seen);
            });
            if (node.nodeType === Node.ELEMENT_NODE && node.shadowRoot) walk(node.shadowRoot, seen);
            if (node.nodeType === Node.ELEMENT_NODE && node.tagName === 'SLOT')
              node.assignedNodes({ flatten: true }).forEach(child => {
                collectText(child);
                if (child.nodeType === Node.ELEMENT_NODE || child.nodeType === Node.DOCUMENT_FRAGMENT_NODE) walk(child, seen);
              });
          };
          walk(e);
          return norm(text.join(' '));
        };
        const isTextEntry = control => control.tagName === 'TEXTAREA' ||
          (control.tagName === 'INPUT' && /^(|text|search|email|tel|url|password)$/i.test(control.type));
        const isRadio = control => (control.tagName === 'INPUT' && control.type === 'radio') || control.getAttribute('role') === 'radio';
        const isSelect = control => control.tagName === 'SELECT' || control.getAttribute('role') === 'combobox';
        if (shadowHost) {
          const controls = controlsIn(root);
          const text = textIn(root);
          return has(text, ['first', 'name']) && has(text, ['gender']) && has(text, ['state']) &&
            controls.some(isTextEntry) && controls.some(isRadio) && controls.some(isSelect) ? [shadowHost] : [];
        }
        const candidates = [...root.querySelectorAll('*')]
          .filter(visible)
          .map(e => ({ e, text: textIn(e), controls: controlsIn(e) }))
          .filter(x => has(x.text, ['first', 'name']) && has(x.text, ['gender']) && has(x.text, ['state']))
          .filter(x => x.controls.some(isTextEntry) && x.controls.some(isRadio) && x.controls.some(isSelect));
        // A supplied title limits the forms to fill. Otherwise, keep every matching form.
        const titled = wantedTitle
          ? candidates.filter(x => x.text.includes(wantedTitle) || norm(x.e.getAttribute('aria-label')) === wantedTitle)
          : [];
        const matchingCandidates = titled.length ? titled : candidates;
        // A form and its wrapper can both contain all controls. Keep only innermost scopes,
        // without discarding independent forms that have the same labels.
        return matchingCandidates
          .filter(candidate => !matchingCandidates.some(other => other.e !== candidate.e && candidate.e.contains(other.e)))
          .map(candidate => candidate.e);
        """;

    public const string FindShadowHosts = """
        const shadowHost = arguments[0];
        const root = shadowHost ? shadowHost.shadowRoot : document;
        return root ? [...root.querySelectorAll('*')].filter(element => element.shadowRoot) : [];
        """;

    public const string FindFrames = """
        const shadowHost = arguments[0];
        const root = shadowHost ? shadowHost.shadowRoot : document;
        if (!root) return [];
        if (!shadowHost) return [...root.querySelectorAll('iframe,frame')];
        const frames = [];
        const visit = (node, seen = new Set()) => {
          if (!node || seen.has(node)) return;
          seen.add(node);
          if (node.nodeType === Node.ELEMENT_NODE) {
            if (node.matches('iframe,frame')) frames.push(node);
            if (node.shadowRoot) visit(node.shadowRoot, seen);
            if (node.tagName === 'SLOT') node.assignedElements({ flatten: true }).forEach(child => visit(child, seen));
          }
          [...(node.children || [])].forEach(child => visit(child, seen));
        };
        visit(root);
        return frames;
        """;

    // Field names and selected option are passed in as arguments. No id/name/class value is used.
    public const string FindControl = """
        const [rootOrControls, kind, requestedOption] = arguments;
        const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
        const norm = s => (s || '').replace(/\s+/g, ' ').trim().toLowerCase();
        const exactWords = (text, phrase) => new RegExp('(^|\\s)' + phrase.split('-').join('\\s+') + '(?=\\s|$)', 'i').test(norm(text));
        const visitComposed = (node, visitor, seen = new Set()) => {
          if (!node || seen.has(node)) return;
          seen.add(node);
          if (node.nodeType === Node.ELEMENT_NODE) {
            visitor(node);
            if (node.shadowRoot) visitComposed(node.shadowRoot, visitor, seen);
            if (node.tagName === 'SLOT') node.assignedElements({ flatten: true }).forEach(child => visitComposed(child, visitor, seen));
          }
          [...(node.children || [])].forEach(child => visitComposed(child, visitor, seen));
        };
        const shadowLabelsFor = e => {
          if (!rootOrControls?.shadowRoot || !e.id) return [];
          const labels = [];
          visitComposed(rootOrControls.shadowRoot, element => {
            if (element.tagName === 'LABEL' && element.htmlFor === e.id) labels.push(element.innerText);
          });
          return labels;
        };
        const textNodes = e => [...e.childNodes].filter(n => n.nodeType === Node.TEXT_NODE).map(n => n.textContent).join(' ');
        const labelText = e => {
          const pieces = [e.getAttribute('aria-label'), e.getAttribute('placeholder'), e.getAttribute('title')];
          if (e.labels) pieces.push(...[...e.labels].map(l => l.innerText));
          pieces.push(...shadowLabelsFor(e));
          const wrappingLabel = e.closest('label');
          if (wrappingLabel) pieces.push(wrappingLabel.innerText);
          const ids = (e.getAttribute('aria-labelledby') || '').split(/\\s+/).filter(Boolean);
          const root = e.getRootNode();
          pieces.push(...ids.map(id => root.getElementById?.(id)?.innerText || document.getElementById(id)?.innerText));
          // A label can be a plain preceding/sibling text node, not a <label> element.
          let box = e.parentElement;
          for (let i = 0; box && i < 2; i++, box = box.parentElement) {
            pieces.push(textNodes(box));
            pieces.push(...[...box.children].filter(x => x !== e && x.tagName === 'LABEL').map(x => x.innerText));
            // Also support a visual label made from a span/div beside the control.  Restrict
            // this to small, control-free siblings so text from the whole form cannot match.
            pieces.push(...[...box.children]
              .filter(x => x !== e && !x.querySelector('input,select,textarea,[role="radio"],[role="combobox"]'))
              .map(x => x.innerText)
              .filter(text => text && text.length <= 80));
          }
          return norm(pieces.filter(Boolean).join(' '));
        };
        // Radio choices must use their own accessible label. Including the whole surrounding
        // group would make every option appear to match when the group contains "Female".
        const radioOptionText = e => {
          const pieces = [e.getAttribute('aria-label'), e.getAttribute('title')];
          if (e.labels) pieces.push(...[...e.labels].map(label => label.innerText));
          const wrappingLabel = e.closest('label');
          if (wrappingLabel) pieces.push(wrappingLabel.innerText);
          const ids = (e.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean);
          const root = e.getRootNode();
          pieces.push(...ids.map(id => root.getElementById?.(id)?.innerText || document.getElementById(id)?.innerText));
          const directCaption = node => {
            if (node?.nodeType === Node.TEXT_NODE) return node.textContent;
            return node && !node.querySelector('input,textarea,select,[role="radio"],[role="combobox"]')
              ? node.innerText
              : '';
          };
          // This page's captions are adjacent to their radio inputs. Prefer the next caption;
          // otherwise use the preceding one for markup that places captions before inputs.
          const nextCaption = directCaption(e.nextSibling) || directCaption(e.nextElementSibling);
          const previousCaption = directCaption(e.previousSibling) || directCaption(e.previousElementSibling);
          pieces.push(nextCaption || previousCaption);
          let optionContainer = e.parentElement;
          for (let i = 0; optionContainer && i < 3; i++, optionContainer = optionContainer.parentElement) {
            const radioCount = optionContainer.querySelectorAll('input[type="radio"],[role="radio"]').length;
            if (radioCount === 1) {
              pieces.push(optionContainer.innerText);
              break;
            }
          }
          // Native radio values are semantic option data, not page-specific locators. Use this
          // only after accessible and visible captions, for pages that expose neither.
          pieces.push(e.value);
          return norm(pieces.filter(Boolean).join(' '));
        };
        const candidates = (() => {
          if (Array.isArray(rootOrControls)) return rootOrControls;
          if (rootOrControls?.shadowRoot) {
            const composed = [];
            visitComposed(rootOrControls.shadowRoot, element => {
              if (element.matches?.('input,textarea,select,button,[role="radio"],[role="combobox"]')) composed.push(element);
            });
            return composed;
          }
          return [...rootOrControls.querySelectorAll('input,textarea,select,button,[role="radio"],[role="combobox"]')];
        })();
        const controls = candidates
          .filter(visible)
          .filter(e => !(e.tagName === 'INPUT' && /^(hidden|submit|reset|button)$/i.test(e.type)));
        const isRadio = e => (e.tagName === 'INPUT' && e.type === 'radio') || e.getAttribute('role') === 'radio';
        const isSelect = e => e.tagName === 'SELECT' || e.getAttribute('role') === 'combobox';
        const isTextEntry = e => e.tagName === 'TEXTAREA' ||
          (e.tagName === 'INPUT' && /^(|text|search|email|tel|url|password)$/i.test(e.type));
        const scored = controls.map(e => {
          const label = labelText(e);
          let score = 0;
          if (kind === 'first-name' && isTextEntry(e) && exactWords(label, 'first-name')) score = 100;
          if (kind === 'state' && isSelect(e) && exactWords(label, 'state')) score = 100;
          if (kind === 'gender' && isRadio(e) && exactWords(radioOptionText(e), requestedOption)) {
            score = 100;
            // A component may expose both an ARIA wrapper and its native radio input for the
            // same visual choice. Prefer the native input, which is the control Selenium can
            // reliably inspect with Selected.
            if (e.tagName === 'INPUT' && e.type === 'radio') score += 20;
            else if (e.getAttribute('role') === 'radio') score += 10;
            // A radio's own label must be the requested value AND its nearby group must say Gender.
            let group = e.parentElement;
            for (let i = 0; group && i < 4; i++, group = group.parentElement)
              if (exactWords(group.innerText, 'gender')) { score += 50; break; }
          }
          return { e, score };
        }).filter(x => x.score > 0).sort((a, b) => b.score - a.score);
        if (!scored.length) {
          if (kind === 'gender') {
            const summary = controls.filter(isRadio).map(e =>
              `<${e.tagName.toLowerCase()} type="${e.type || ''}" role="${e.getAttribute('role') || ''}" label="${radioOptionText(e)}">`).join(', ');
            throw new Error(`No Gender option matching "${requestedOption}" was found. Visible radio candidates: ${summary}`);
          }
          return null;
        }
        const best = scored.filter(x => x.score === scored[0].score);
        if (best.length > 1) {
          const summary = best.map(x =>
            `<${x.e.tagName.toLowerCase()} type="${x.e.type || ''}" role="${x.e.getAttribute('role') || ''}" label="${kind === 'gender' ? radioOptionText(x.e) : labelText(x.e)}">`).join(', ');
          throw new Error(`Ambiguous ${kind}: ${best.length} equally suitable visible controls were found in the selected form: ${summary}`);
        }
        return best[0].e;
        """;

    public const string FindVisibleOption = """
        const [control, requestedState] = arguments;
        const wanted = (requestedState || '').trim().toLowerCase();
        const visible = e => !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
        const isWanted = e => visible(e) && (e.innerText || e.textContent || '').trim().toLowerCase() === wanted;
        const relatedIds = [control.getAttribute('aria-controls'), control.getAttribute('aria-owns')]
          .filter(Boolean).flatMap(value => value.split(/\s+/));
        const relatedRoots = relatedIds.map(id => document.getElementById(id)).filter(Boolean);
        const findIn = root => [...root.querySelectorAll('[role="option"],option,li,[role="listitem"]')].find(isWanted);
        for (const root of relatedRoots) {
          const option = findIn(root);
          if (option) return option;
        }
        // Without an ARIA relationship, interact only when a single visible list is open.
        // Returning null on ambiguity is safer than choosing an identically named option elsewhere.
        const openLists = [...document.querySelectorAll('[role="listbox"],[role="menu"]')]
          .filter(visible).map(root => ({ root, option: findIn(root) })).filter(x => x.option);
        return openLists.length === 1 ? openLists[0].option : null;
        """;
}
