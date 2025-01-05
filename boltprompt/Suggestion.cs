namespace boltprompt;

public record Suggestion(string Text)
{
    public string? Icon;
    private string? _description;

    public string? Description
    {
        get => _description ?? SecondaryDescription;
        set => _description = value;
    }

    public virtual string? SecondaryDescription { get; set; }
    public CommandInfo.Argument? Argument { get; set; }

    public int Priority = 0;
}