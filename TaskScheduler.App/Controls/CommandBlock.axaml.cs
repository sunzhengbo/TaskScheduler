using Avalonia;
using Avalonia.Controls;

namespace TaskScheduler.App.Controls;

public partial class CommandBlock : UserControl
{
    public static readonly StyledProperty<string> CommandNameProperty =
        AvaloniaProperty.Register<CommandBlock, string>(nameof(CommandName), "Command");

    public static readonly StyledProperty<string> CommandTypeProperty =
        AvaloniaProperty.Register<CommandBlock, string>(nameof(CommandType), "Bash");

    public static readonly StyledProperty<string> CommandBodyProperty =
        AvaloniaProperty.Register<CommandBlock, string>(nameof(CommandBody), string.Empty);

    public string CommandName
    {
        get => GetValue(CommandNameProperty);
        set => SetValue(CommandNameProperty, value);
    }

    public string CommandType
    {
        get => GetValue(CommandTypeProperty);
        set => SetValue(CommandTypeProperty, value);
    }

    public string CommandBody
    {
        get => GetValue(CommandBodyProperty);
        set => SetValue(CommandBodyProperty, value);
    }

    public CommandBlock()
    {
        InitializeComponent();
    }
}
