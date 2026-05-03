using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RetroGameCoverDownloader.Helpers;

namespace RetroGameCoverDownloader.Services;

public static class BugReportService
{
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string BugReportApiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";

    private const string ApplicationName = "RetroGameCoverDownloader";
    internal static HttpClient HttpClientInstance { get; set; }

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

    private static string GetEnvironmentDetails()
    {
        var version = AppInfo.VersionString;
        var osDescription = RuntimeInformation.OSDescription;
        var processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
        var osBitness = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        var processBitness = Environment.Is64BitProcess ? "64-bit" : "32-bit";
        var processorCount = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
        var baseDirectory = AppContext.BaseDirectory;
        var tempPath = Path.GetTempPath();

        var sb = new StringBuilder();
        sb.AppendLine("=== Environment Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application Name: {ApplicationName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application Version: {version}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {osDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {processArchitecture}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Bitness: {processBitness} (Process), {osBitness} (OS)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Windows Version: {osDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Processor Count: {processorCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Base Directory: {baseDirectory}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Temp Path: {tempPath}");
        return sb.ToString();
    }

    private static string GetExceptionDetails(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Exception Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.GetType().FullName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.Message}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.Source ?? "Unknown"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.StackTrace ?? "No stack trace available."}");

        if (ex.InnerException != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- Inner Exception ---");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.InnerException.GetType().FullName}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.InnerException.Message}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.InnerException.Source ?? "Unknown"}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.InnerException.StackTrace ?? "No stack trace available."}");
        }

        return sb.ToString();
    }

    private static string FormatErrorMessage(Exception ex, string contextMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine(GetEnvironmentDetails());
        sb.AppendLine();
        sb.AppendLine("=== Error Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Error message: {contextMessage}");
        sb.AppendLine();
        sb.AppendLine(GetExceptionDetails(ex));
        return sb.ToString();
    }

    public static void LogErrorSync(Exception? ex, string? contextMessage = null)
    {
        if (ex == null)
        {
            ex = new InvalidOperationException("BugReportService.LogErrorSync was called with a null exception object.");
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
            SendLogToApiAsync(ex, contextMessage).GetAwaiter().GetResult();
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
            ex = new InvalidOperationException("BugReportService.LogErrorAsync was called with a null exception object.");
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

        await SendLogToApiAsync(ex, contextMessage);
    }

    private static async Task<bool> SendLogToApiAsync(Exception ex, string contextMessage)
    {
        try
        {
            var version = AppInfo.VersionString;
            var message = FormatErrorMessage(ex, contextMessage);

            var payload = new
            {
                message,
                applicationName = ApplicationName,
                version,
                stackTrace = ex.StackTrace,
                userInfo = contextMessage,
                environment = RuntimeInformation.OSDescription
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, BugReportApiUrl);
            request.Headers.Add("X-API-KEY", ApiKey);
            request.Content = httpContent;

            using var response = await HttpClientInstance.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
            var version = AppInfo.VersionString;
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
