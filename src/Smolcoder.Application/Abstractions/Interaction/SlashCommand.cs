namespace Smolcoder.Application.Abstractions.Interaction;

public sealed class SlashCommand
{
    public SlashCommand(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }
}
