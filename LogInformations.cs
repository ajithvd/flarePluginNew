using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AILinguistic
{
    public static class LogInformations
    {
        private static readonly string _logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Linguistic_Log.txt");


        public static void InitializeLogFile()
        {
            if (!File.Exists(_logPath))
            {
                File.WriteAllText(_logPath, "Timestamp|Action|OriginalText|SuggestedText\n");
            }
        }
        public static void LogAction(string action, string original, string suggestion)
        {

            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{action}|{original}|{suggestion ?? "N/A"}\n";
            File.AppendAllText(_logPath, logEntry);
        }

    }
}
