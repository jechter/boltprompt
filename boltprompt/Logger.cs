using System.Text;
using NiceIO;

namespace boltprompt;

internal static class Logger
{
    public const string Gpt = "GPT";

    private static readonly Dictionary<NPath, FileStream> LogFiles = new();

    private static FileStream? GetFileStream(NPath path)
    {
        if (LogFiles.TryGetValue(path, out var stream))
            return stream;
        try
        {
            stream = File.Open(path.ToString(), FileMode.Create);
        }
        catch
        {
            // Rather don't write to a log then fail because of file permission issues.
            return null;
        }

        LogFiles[path] = stream;
        return stream;
    }

    public static NPath GetLogPath(string file)
    {
        var logDir = Paths.LogDir;
        logDir.CreateDirectory();
        var path = logDir.Combine(file);
        return path;
    }
    
    public static void Log(string file, string message)
    {
        var stream = GetFileStream(GetLogPath(file));
        if (stream == null)
            return;
        
        stream.Write(Encoding.UTF8.GetBytes(message + "\n"));
        stream.Flush();
    }
}