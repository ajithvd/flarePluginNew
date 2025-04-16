using System;
using System.IO;

namespace AILinguistic
{
    public static class ExceptionHandling
    {
        private static readonly string _log = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AI_Linguistic_Exception_Log.txt");

        public static void LogError(string message)
        {
            File.AppendAllText(_log, $"[ERROR] {DateTime.Now}: {message}\n");
        }

        public static void LogWarning(string message)
        {
            File.AppendAllText(_log, $"[WARN] {DateTime.Now}: {message}\n");
        }
    }
}
