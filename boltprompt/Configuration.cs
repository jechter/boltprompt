using System.Reflection;
using System.Text.Json;

namespace boltprompt;

internal class Configuration
{
    public int NumSuggestions { get; set; } = 10;
    public string SuggestionBackgroundColor { get; set; } = "d0d0d0";
    public string SelectedSuggestionBackgroundColor { get; set; } = "30c0ff";
    public string SuggestionTextColor { get; set; } = "303030";
    public string AutocompleteTextColor { get; set; } = "a0a0a0";
    public string PromptPrefix { get; set; } = "\u001b[48;5;0m\u001b[38;5;7m{working_directory_name}{prompt_symbol}\u001b[0m\u001b[38;5;0m\uE0B0\u001b[0m ";
    public string? OpenAiApiKey { get; set; } = null;
    
    private static Configuration Load()
    {
        if (Paths.Configuration.FileExists())
            return JsonSerializer.Deserialize<Configuration>(Paths.Configuration.ReadAllText()) ?? new(); 

        return new();
    }

    private void Write()
    {
        Paths.Configuration.WriteAllText(JsonSerializer.Serialize(this));
    }

    public static void ListProperties()
    {
        foreach (var pi in typeof(Configuration).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            Console.WriteLine(pi.Name);
    }
    
    public string Get(string propertyName) => 
        typeof(Configuration).GetProperty(propertyName)?.GetValue(this)?.ToString() ?? $"Property {propertyName} not found.";

    public void Set(string propertyName, string value)
    {
        var prop = typeof(Configuration).GetProperty(propertyName);
        if (prop == null)
            throw new InvalidDataException($"Invalid config property name: {propertyName}");

        object propValue;
        if (prop.PropertyType == typeof(string))
            propValue = value;
        else if (prop.PropertyType == typeof(int))
            propValue = int.Parse(value);
        else if (prop.PropertyType == typeof(BufferedConsole.ConsoleColor))
            propValue = (BufferedConsole.ConsoleColor)int.Parse(value);
        else
            propValue = Convert.ChangeType(value, prop.PropertyType);
        prop.SetValue(this, propValue);
        Write();
    }

    public static Configuration Instance { get; } = Load();

    public static void ListPropertyValues(string propertyName)
    {
        var prop = typeof(Configuration).GetProperty(propertyName);
        if (prop == null)
            throw new InvalidDataException($"Invalid config property name: {propertyName}");
        if (prop.Name == nameof(PromptPrefix))
        {
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{working_directory_name}{prompt_symbol}\u001b[0m\u001b[38;5;0m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{working_directory_short_path}{prompt_symbol}\u001b[0m\u001b[38;5;0m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{working_directory_path}{prompt_symbol}\u001b[0m\u001b[38;5;0m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{user_name}\u001b[38;5;0m\u001b[48;5;240m\uE0B0\u001b[38;5;7m {working_directory_name}{prompt_symbol}\u001b[0m\u001b[38;5;240m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{user_name}\u001b[38;5;0m\u001b[48;5;240m\uE0B0\u001b[38;5;7m {working_directory_short_path}{prompt_symbol}\u001b[0m\u001b[38;5;240m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{user_name}\u001b[38;5;0m\u001b[48;5;240m\uE0B0\u001b[38;5;7m {working_directory_path}{prompt_symbol}\u001b[0m\u001b[38;5;240m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{host_name}\u001b[38;5;0m\u001b[48;5;240m\uE0B0\u001b[38;5;7m {user_name}\u001b[48;5;245m\u001b[38;5;240m\uE0B0\u001b[38;5;7m {working_directory_name}{prompt_symbol}\u001b[0m\u001b[38;5;245m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{host_name}\u001b[38;5;0m\u001b[48;5;240m\uE0B0\u001b[38;5;7m {user_name}\u001b[48;5;245m\u001b[38;5;240m\uE0B0\u001b[38;5;7m {working_directory_short_path}{prompt_symbol}\u001b[0m\u001b[38;5;245m\uE0B0\u001b[0m \": ");
            Console.WriteLine("\"\u001b[48;5;0m\u001b[38;5;7m{host_name}\u001b[38;5;0m\u001b[48;5;240m\uE0B0\u001b[38;5;7m {user_name}\u001b[48;5;245m\u001b[38;5;240m\uE0B0\u001b[38;5;7m {working_directory_path}{prompt_symbol}\u001b[0m\u001b[38;5;245m\uE0B0\u001b[0m \": ");
        } 
        else if (prop.PropertyType == typeof(string) && prop.Name.EndsWith("Color"))
        {
            void PrintColor(string html, string name) => Console.WriteLine($"{html}:\u001b[38;5;{(int)BufferedConsole.ColorForHtml(html)}m{name}");    

            PrintColor("000000", "Black");	
            PrintColor("FFFFFF", "White");	
            PrintColor("FF0000", "Red");	    
            PrintColor("008000", "Green");	
            PrintColor("0000FF", "Blue");	
            PrintColor("FFFF00", "Yellow");	
            PrintColor("00FFFF", "Cyan");	
            PrintColor("FF00FF", "Magenta");	
            PrintColor("808080", "Gray");	
            PrintColor("C0C0C0", "Silver");	
            PrintColor("800000", "Maroon");	
            PrintColor("808000", "Olive");	
            PrintColor("000080", "Navy");	
            PrintColor("800080", "Purple");	
            PrintColor("008080", "Teal");	
            PrintColor("00FF00", "Lime");	
            PrintColor("FFA500", "Orange");	
            PrintColor("FFC0CB", "Pink");	
            PrintColor("A52A2A", "Brown");	
            PrintColor("FFD700", "Gold");
        }
    }
}