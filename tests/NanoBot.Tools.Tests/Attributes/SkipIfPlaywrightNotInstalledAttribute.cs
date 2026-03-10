using Microsoft.Playwright;
using Xunit;
using Xunit.Sdk;

namespace NanoBot.Tools.Tests.Attributes;

/// <summary>
/// Xunit attribute that skips a test if Playwright browsers are not installed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class SkipIfPlaywrightNotInstalledAttribute : FactAttribute
{
    private static readonly Lazy<bool> _playwrightInstalled = new(CheckPlaywrightInstalled, true);

    public override string? Skip
    {
        get
        {
            if (!_playwrightInstalled.Value)
            {
                return "Playwright browsers not installed. Run: pwsh bin/Debug/net10.0/playwright.ps1 install chromium";
            }
            return base.Skip;
        }
        set => base.Skip = value;
    }

    private static bool CheckPlaywrightInstalled()
    {
        try
        {
            // Quick check: try to create Playwright instance and access browser type
            var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            try
            {
                // Try to launch browser with a very short timeout
                var browser = playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Timeout = 5000
                }).GetAwaiter().GetResult();

                browser.CloseAsync().GetAwaiter().GetResult();
                return true;
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist") ||
                                                  ex.Message.Contains("could not find executable"))
            {
                return false;
            }
            catch
            {
                // Other errors might be environment issues, but browser exists
                return true;
            }
            finally
            {
                playwright.Dispose();
            }
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist") ||
                                              ex.Message.Contains("could not find executable"))
        {
            return false;
        }
        catch
        {
            // If we can't even create Playwright, assume not installed
            return false;
        }
    }
}

/// <summary>
/// Xunit attribute that skips a theory if Playwright browsers are not installed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class SkipIfPlaywrightNotInstalledTheoryAttribute : TheoryAttribute
{
    private static readonly Lazy<bool> _playwrightInstalled = new(CheckPlaywrightInstalled, true);

    public override string? Skip
    {
        get
        {
            if (!_playwrightInstalled.Value)
            {
                return "Playwright browsers not installed. Run: pwsh bin/Debug/net10.0/playwright.ps1 install chromium";
            }
            return base.Skip;
        }
        set => base.Skip = value;
    }

    private static bool CheckPlaywrightInstalled()
    {
        try
        {
            var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            try
            {
                var browser = playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Timeout = 5000
                }).GetAwaiter().GetResult();

                browser.CloseAsync().GetAwaiter().GetResult();
                return true;
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist") ||
                                                  ex.Message.Contains("could not find executable"))
            {
                return false;
            }
            catch
            {
                return true;
            }
            finally
            {
                playwright.Dispose();
            }
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist") ||
                                              ex.Message.Contains("could not find executable"))
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
