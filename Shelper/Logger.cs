using System.Text;
using NiceIO;

namespace Shelper;

public class Logger
{
    public const string GPT = "GPT";

    private static Dictionary<NPath, FileStream> logFiles = new();

    static FileStream GetFileStream(NPath path)
    {
        if (logFiles.TryGetValue(path, out var stream))
            return stream;
        stream = File.Open(path.ToString(), FileMode.Truncate);
        logFiles[path] = stream;
        return stream;
    }
    public static void Log(string file, string message)
    {
        var logDir = new NPath("Logs").MakeAbsolute();
        logDir.CreateDirectory();
        var path = logDir.Combine(file);
        var stream = GetFileStream(path);
        stream.Write(Encoding.UTF8.GetBytes(message));
        stream.Flush();
    }
}