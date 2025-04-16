using System;
using System.IO;

namespace AILinguistic
{
    public static class LogInformations
    {
        private static readonly string _logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Linguistic_Log.txt");

        public static void InitializeLogFile()
        {
            try
            {
                // Creates the log file and writes the header if it doesn't exist
                if (!File.Exists(_logPath))
                {
                    // Header row for a CSV-like structure
                    File.WriteAllText(_logPath, "Timestamp|Action|OriginalText|SuggestedText
");
                }
            }
            catch (Exception ex)
            {
                // Log initialization errors to the exception log file
                ExceptionHandling.LogError($"Failed to initialize action log file '{_logPath}': {ex.Message}");
            }
        }

        public static void LogAction(string action, string original, string suggestion)
        {
            try
            {
                // Basic sanitization to prevent breaking the log format
                // Replace pipe, newline, and carriage return characters
                string sanitizedOriginal = original?.Replace("|", " ").Replace("
", " ").Replace("", "");
                string sanitizedSuggestion = suggestion?.Replace("|", " ").Replace("
", " ").Replace("", "");

                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{action}|{sanitizedOriginal}|{sanitizedSuggestion ?? "N/A"}
";
                File.AppendAllText(_logPath, logEntry);
            }
            catch (Exception ex)
            {
                // Log errors during action logging to the exception log file
                ExceptionHandling.LogError($"Failed to write to action log file '{_logPath}': {ex.Message}");
            }
        }
    }
}
