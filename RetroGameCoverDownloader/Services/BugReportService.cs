using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RetroGameCoverDownloader.Helpers;

namespace RetroGameCoverDownloader.Services;

public class BugReportService : IBugReportService
{
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string BugReportApiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";
    internal Func<HttpClient> HttpClientFactory { get; set; } = CreateDefaultHttpClient;

    internal static IBugReportService Current { get; set; } = new BugReportService();

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    private readonly Lock _httpClientLock = new();
    private HttpClient? _httpClientInstance;

    private HttpClient GetHttpClient()
    {
        lock (_httpClientLock)
        {
            return _httpClientInstance ??= HttpClientFactory();
        }
    }

    internal void InvalidateHttpClient()
    {
        lock (_httpClientLock)
        {
            _httpClientInstance?.Dispose();
            _httpClientInstance = null;
        }
    }

    private readonly string _baseDirectory = AppInfo.LocalAppDataFolderPath;
    private string ErrorLogFilePath => Path.Combine(_baseDirectory, "error.log");
    private string CriticalLogFilePath => Path.Combine(_baseDirectory, "critical_error.log");

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
        sb.AppendLine(CultureInfo.InvariantCulture, $"Application Name: {AppInfo.AppName}");
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

    private static string FormatContextOnlyMessage(string contextMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine(GetEnvironmentDetails());
        sb.AppendLine();
        sb.AppendLine("=== Error Details ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Error message: {contextMessage}");
        sb.AppendLine();
        sb.AppendLine("No exception object was provided; recorded locally only, no bug report sent to the API.");
        return sb.ToString();
    }

    void IBugReportService.LogErrorSync(Exception? ex, string? contextMessage)
    {
        CoreLogErrorSync(ex, contextMessage);
    }

    public static void LogErrorSync(Exception? ex, string? contextMessage = null)
    {
        Current.LogErrorSync(ex, contextMessage);
    }

    Task IBugReportService.LogErrorAsync(Exception? ex, string? contextMessage)
    {
        return CoreLogErrorAsync(ex, contextMessage);
    }

    public static Task LogErrorAsync(Exception? ex, string? contextMessage = null)
    {
        return Current.LogErrorAsync(ex, contextMessage);
    }

    private void CoreLogErrorSync(Exception? ex, string? contextMessage)
    {
        contextMessage ??= "No additional context provided.";

        if (ex == null)
        {
            WriteContextOnlyErrorLogSync(contextMessage);
            return;
        }

        var logContent = FormatErrorMessage(ex, contextMessage);

        try
        {
            Directory.CreateDirectory(_baseDirectory);
            File.AppendAllText(ErrorLogFilePath, logContent, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            WriteToCriticalLog(writeEx, $"Failed to write main error to '{ErrorLogFilePath}'. Original error: {ex.Message}");
        }

        try
        {
            Task.Run(async () => await SendLogToApiAsync(ex, contextMessage).ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        catch (Exception apiEx)
        {
            WriteToCriticalLog(apiEx, "Exception in synchronous SendLogToApiAsync from LogErrorSync.");
        }
    }

    private void WriteContextOnlyErrorLogSync(string contextMessage)
    {
        try
        {
            Directory.CreateDirectory(_baseDirectory);
            File.AppendAllText(ErrorLogFilePath, FormatContextOnlyMessage(contextMessage), Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            WriteToCriticalLog(writeEx, $"Failed to write context-only error to '{ErrorLogFilePath}'.");
        }
    }

    private async Task CoreLogErrorAsync(Exception? ex, string? contextMessage)
    {
        contextMessage ??= "No additional context provided.";

        if (ex == null)
        {
            await WriteContextOnlyErrorLogAsync(contextMessage);
            return;
        }

        var logContent = FormatErrorMessage(ex, contextMessage);

        try
        {
            Directory.CreateDirectory(_baseDirectory);
            await File.AppendAllTextAsync(ErrorLogFilePath, logContent, Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            WriteToCriticalLog(writeEx, $"Failed to write main error to '{ErrorLogFilePath}'. Original error: {ex.Message}");
        }

        await SendLogToApiAsync(ex, contextMessage);
    }

    private async Task WriteContextOnlyErrorLogAsync(string contextMessage)
    {
        try
        {
            Directory.CreateDirectory(_baseDirectory);
            await File.AppendAllTextAsync(ErrorLogFilePath, FormatContextOnlyMessage(contextMessage), Encoding.UTF8);
        }
        catch (Exception writeEx)
        {
            WriteToCriticalLog(writeEx, $"Failed to write context-only error to '{ErrorLogFilePath}'.");
        }
    }

    private async Task<bool> SendLogToApiAsync(Exception ex, string contextMessage)
    {
        try
        {
            var version = AppInfo.VersionString;
            var message = FormatErrorMessage(ex, contextMessage);

            var payload = new
            {
                message,
                applicationName = AppInfo.AppName,
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

            using var response = await GetHttpClient().SendAsync(request).ConfigureAwait(false);

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

    private void WriteToCriticalLog(Exception ex, string contextMessage)
    {
        try
        {
            var version = AppInfo.VersionString;
            var criticalContent = new StringBuilder();
            criticalContent.AppendLine("--- CRITICAL LOGGING ERROR ---");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Application: {AppInfo.AppName}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Version: {version}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Context: {contextMessage}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Exception Type: {ex.GetType().Name}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Exception Message: {ex.Message}");
            criticalContent.AppendLine(CultureInfo.InvariantCulture, $"Stack Trace:\n{ex.StackTrace}");
            criticalContent.AppendLine("--------------------------------------------------\n");

            Directory.CreateDirectory(_baseDirectory);
            File.AppendAllText(CriticalLogFilePath, criticalContent.ToString(), Encoding.UTF8);
        }
        catch (Exception logEx)
        {
            Debug.WriteLine($"CRITICAL: Failed to write to critical log file '{CriticalLogFilePath}'. Context: {contextMessage}. Error: {logEx.Message}");
        }
    }
}
