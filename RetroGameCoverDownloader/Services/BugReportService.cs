using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace RetroGameCoverDownloader.Services;

public static class BugReportService
{
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string BugReportApiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";

    private const string ApplicationName = "RetroGameCoverDownloader";
    private static readonly HttpClient HttpClientInstance;

    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static readonly string ErrorLogFilePath = Path.Combine(BaseDirectory, "error.log");
    private static readonly string CriticalLogFilePath = Path.Combine(BaseDirectory, "critical_error.log");

    static BugReportService()
    {
        HttpClientInstance = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private static string FormatErrorMessage(Exception ex, string contextMessage)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        var osDescription = RuntimeInformation.OSDescription;
        var osArchitecture = RuntimeInformation.OSArchitecture.ToString();
        var frameworkDescription = RuntimeInformation.FrameworkDescription;

        var fullErrorMessage = new StringBuilder();
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Application: {ApplicationName}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Version: {version}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Context: {contextMessage}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"OS: {osDescription}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"OS Architecture: {osArchitecture}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Framework: {frameworkDescription}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Exception Type: {ex.GetType().Name}");
        fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Exception Message: {ex.Message}");
        fullErrorMessage.AppendLine("\n--- Stack Trace ---");
        fullErrorMessage.AppendLine(ex.StackTrace);
        if (ex.InnerException != null)
        {
            fullErrorMessage.AppendLine("\n--- Inner Exception ---");
            fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.InnerException.GetType().Name}");
            fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.InnerException.Message}");
            fullErrorMessage.AppendLine(CultureInfo.InvariantCulture, $"Stack Trace:\n{ex.InnerException.StackTrace}");
        }

        fullErrorMessage.AppendLine("--------------------------------------------------\n");
        return fullErrorMessage.ToString();
    }

    public static void LogErrorSync(Exception? ex, string? contextMessage = null)
    {
        if (ex == null)
        {
            ex = new Exception("BugReportService.LogErrorSync was called with a null exception object.");
            try
            {
                throw ex;
            }
            catch
            {
                /* ex now has a stack trace */
            }
        }

        contextMessage ??= "No additional context provided.";

        var logContent = FormatErrorMessage(ex, contextMessage);

        try
        {
            File.AppendAllText(ErrorLogFilePath, logContent, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            WriteToCriticalLog(writeEx, $"Failed to write main error to '{ErrorLogFilePath}'. Original error: {ex.Message}");
        }

        // Synchronously wait for the API call to ensure it completes before process termination
        try
        {
            SendLogToApiAsync(logContent).GetAwaiter().GetResult();
        }
        catch (Exception apiEx)
        {
            WriteToCriticalLog(apiEx, "Exception in synchronous SendLogToApiAsync from LogErrorSync.");
        }
    }


    public static async Task LogErrorAsync(Exception? ex, string? contextMessage = null)
    {
        if (ex == null)
        {
            ex = new Exception("BugReportService.LogErrorAsync was called with a null exception object.");
            try
            {
                throw ex;
            }
            catch
            {
                /* ex now has a stack trace */
            }
        }

        contextMessage ??= "No additional context provided.";

        var logContent = FormatErrorMessage(ex, contextMessage);

        try
        {
            await File.AppendAllTextAsync(ErrorLogFilePath, logContent, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            WriteToCriticalLog(writeEx, $"Failed to write main error to '{ErrorLogFilePath}'. Original error: {ex.Message}");
        }

        await SendLogToApiAsync(logContent);
    }

    private static async Task<bool> SendLogToApiAsync(string logContent)
    {
        try
        {
            var payload = new
            {
                message = logContent,
                applicationName = ApplicationName
            };
            var jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, BugReportApiUrl);
            request.Headers.Add("X-API-KEY", ApiKey);
            request.Content = httpContent;

            using var response = await HttpClientInstance.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                WriteToCriticalLog(
                    new HttpRequestException($"API request failed with status code {response.StatusCode}. Response: {responseContent}"),
                    "Error sending log to API.");
                return false;
            }
        }
        catch (Exception apiEx)
        {
            WriteToCriticalLog(apiEx, "Exception occurred while sending log to API.");
            return false;
        }
    }

    private static void WriteToCriticalLog(Exception ex, string contextMessage)
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            var criticalContent = new StringBuilder();
            criticalContent.AppendLine("--- CRITICAL LOGGING ERROR ---");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Application: {ApplicationName}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Version: {version}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Context: {contextMessage}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Exception Type: {ex.GetType().Name}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Exception Message: {ex.Message}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Stack Trace:\n{ex.StackTrace}");
            criticalContent.AppendLine("--------------------------------------------------\n");

            File.AppendAllText(CriticalLogFilePath, criticalContent.ToString(), Encoding.UTF8);
        }
        catch (Exception)
        {
            // Can't do much more here.
        }
    }
}