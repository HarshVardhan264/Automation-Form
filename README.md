# CloudQA Resilient Form Automation

A C#/.NET 8 Selenium automation for the CloudQA Automation Practice Form. It enters and verifies three values in each supported form context:

- First Name
- Gender
- State

The implementation intentionally does not use page-specific IDs, names, classes, fixed XPath, or fixed CSS selectors to identify these fields. It uses visible labels, accessibility information, input type, and nearby form context instead.

## Prerequisites

- .NET 8 SDK
- Google Chrome

Selenium Manager, included with Selenium WebDriver, obtains the matching ChromeDriver when the program first runs.

## Run

From this project folder:

```powershell
dotnet restore
dotnet run
```

Visible Chrome is the default and remains open for 90 seconds after the run.

Useful options:

```powershell
# Keep the visible browser open for five minutes
dotnet run -- --visible --pause-seconds 300

# Run in the background
dotnet run -- --headless

# Configure the target URL and test data
dotnet run -- --url "https://app.cloudqa.io/home/AutomationPracticeForm" --first-name "Asha" --gender "Female" --state "India"

# Configure how many self-referencing iframe levels to visit
dotnet run -- --max-recursion-depth 3
```

The same values can be supplied through environment variables such as `CLOUDQA_URL`, `CLOUDQA_FIRST_NAME`, `CLOUDQA_GENDER`, `CLOUDQA_STATE`, `CLOUDQA_HEADLESS`, `CLOUDQA_PAUSE_SECONDS`, and `CLOUDQA_MAX_RECURSION_DEPTH`.

## Approach

The program uses explicit Selenium waits and a small semantic DOM matcher. A field is identified by its visible or accessible label, its control type, and—when choosing a gender option—its option caption plus nearby Gender context. Tied candidates are treated as ambiguous instead of selecting the first match.

It traverses the main document, regular iframes, and open Shadow DOM. When an iframe loads the same page again, `--max-recursion-depth` bounds the traversal so the program cannot run forever.

After filling each discovered form, the program reads back the First Name, selected Gender, and selected State. It prints a clear `PASSED` or `FAILED` result. On failure, it saves a timestamped PNG screenshot in the working folder.

## Limitations

- The CloudQA page embeds a copy of itself in an iframe inside Shadow DOM. This creates an infinite tree, so no finite program can fill every recursive copy. The configurable recursion limit is the explicit boundary.
- Closed Shadow DOM and cross-origin iframes cannot be inspected by normal browser automation.
- The page must preserve meaningful labels or accessible names for First Name, Gender, and State. If those meanings are removed entirely, safe identification is not possible.

See [SUBMISSION_NOTES.md](SUBMISSION_NOTES.md) for a concise reviewer note and [RECORDING_SCRIPT.md](RECORDING_SCRIPT.md) for a five-minute demo outline.
