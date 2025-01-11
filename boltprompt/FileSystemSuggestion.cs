using Mono.Unix.Native;
using NiceIO;

namespace boltprompt;

internal record FileSystemSuggestion : Suggestion
{
    public static bool IsExecutable(NPath filePath)
    {
        if (Syscall.stat(filePath.ToString(), out var fileStat) == 0)
            return (fileStat.st_mode & FilePermissions.S_IXUSR) != 0;
        return false;
    }
    
    private static bool IsSymbolicLink(NPath path)
    {
        var fileInfo = new FileInfo(path.ToString());
        return (fileInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
    }

    private static string GetFileIcon(NPath path)
    {
        if (!TerminalUtility.CurrentTerminalHasNerdFont())
            return path.DirectoryExists() ? "📁" : path.Exists() ? "📄" : "";
        if (path.DirectoryExists())
        {
            if (IsSymbolicLink(path))
                return "\uf482"; //nf-oct-file_symlink_directory
            return path.FileName.StartsWith('.') || string.IsNullOrEmpty(path.FileName)
                ? "\uf413" //nf-oct-file_directory
                : "\uf4d3"; //nf-oct-file_directory_fill
//                    ? "\udb85\udf9e" //nf-md-folder_hidden
//                  : "\udb80\ude4b"; //nf-md-folder
        }

        if (!path.Exists()) return "";
        
        if (path.FileName.StartsWith('.'))
            return "\udb81\ude13";//nf-md-file_hidden

        if (IsSymbolicLink(path))
            return "\udb84\udd77";//nf-md-file_link

        if (IsExecutable(path))
            return "\udb82\udcc6";//nf-md-application
        
        return path.Extension.ToLowerInvariant() switch
        {
            "sh" or "js" or "json" or "c" or "cpp" or "cs" or "csproj" or "sln" or "py" or "java" or "html" or "pl" => 
                "\udb80\ude2e", //nf-md-file_code
            "zip" or "7z" or "tar" or "gz" => "\udb82\udeb6", //nf-md-file_cabinet
            "txt" or "md" or "pdf" or "log" => "\udb80\ude19", //nf-md-file_document
            "gif" or "jpg" or "jpeg" or "png" or "psd" or "tif" or "tiff" or "heic" => "\udb80\ude1f", //nf-md-file_image
            "mov" or "mpg" or "mpeg" or "avi" or "mp4" or "m4v" => "\udb80\ude2b", //nf-md-file_video
            "wav" or "mid" or "mp3" or "m4a" => "\udb80\ude23", //nf-md-file_music
            "xls" => "\udb80\ude1b", //nf-md-file_excel
            _ => "\udb80\ude14"
        };
    }
    
    public FileSystemSuggestion(string path) : base(path)
    {
        Icon = GetFileIcon(Suggestor.UnescapeFileName(path.Trim()));
    }
    
    public override string? SecondaryDescription => FileDescriptions.GetFileDescription(Text);
}