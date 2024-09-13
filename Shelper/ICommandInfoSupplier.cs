namespace Shelper;

public interface ICommandInfoSupplier
{
    int Order { get; }
    bool CanHandle(string command);
    Task<CommandInfo?> GetCommandInfoForCommand(string command);
}