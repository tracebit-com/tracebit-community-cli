using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;

using NuGet.Packaging;

using Spectre.Console;

using Tracebit.Cli.Extensions;

namespace Tracebit.Cli.Commands;

public class Interactive
{
    private static readonly Command ExitCommand = new("exit");
    private static readonly Command BackCommand = new("back");
    private static readonly HashSet<Command> CommandsWithSubcommandThatInvokeDirectly = [Trigger.BaseCommand];
    // NOTE: don't use Hidden on the command as we want it to appear in the '--help' menu
    private static readonly HashSet<Command> CommandsThatDontShowInInteractiveMode = [Refresh.BaseCommand];

    private const string FontPath = "Tracebit.Cli.Resources.Fonts.dos-rebel.flf";

    public static async Task<int> InteractiveAsync(ParseResult parseResult, ConsoleCancelEventHandler mainCancelHandler, CancellationToken cancellationToken)
    {
        var rootCommand = parseResult.RootCommandResult.Command;
        var commandStack = new Stack<Command>([rootCommand]);  // used for back navigation and breadcrumbs

        await PrintAsciiArt();

        // Main menu loop
        while (true)
        {
            var command = commandStack.Peek();

            // Build breadcrumbs
            var segments = commandStack.Reverse()
                .Select(c => c is RootCommand ? "home" : c.Name)
                .ToList();
            var breadCrumbs = BreadCrumbsFromSegments(segments);

            // Prompt for action
            var commandChoice = await AnsiConsole.PromptAsync(
                SelectCommandPrompt(command.Subcommands.Append(command is RootCommand ? ExitCommand : BackCommand)
                        .Where(c => !CommandsThatDontShowInInteractiveMode.Contains(c))
                        .ToList())
                .Title($"[purple]{breadCrumbs}[/]"),
                cancellationToken
            );

            // Handle back/exit action
            if (commandChoice == ExitCommand)
            {
                break;
            }
            if (commandChoice == BackCommand)
            {
                commandStack.Pop();
                continue;
            }

            // If the chosen command has subcommands and isn't in the list of commands that should invoke directly
            // push it onto the stack and continue
            if (commandChoice.Subcommands.Count > 0 && !CommandsWithSubcommandThatInvokeDirectly.Contains(commandChoice))
            {
                commandStack.Push(commandChoice);
                continue;
            }

            // Add command to the breadcrumbs
            segments.Add(commandChoice.Name);
            breadCrumbs = BreadCrumbsFromSegments(segments);
            AnsiConsole.MarkupLine($"{breadCrumbs}\n");

            // Pass a new cancellation token to the subcommand to let ctrl+c cancel just the subcommand
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler subcommandCancelHandler = (_, consoleArgs) =>
            {
                consoleArgs.Cancel = true;
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    cts.Cancel();
                }
                catch (ObjectDisposedException) { }
            };
            Console.CancelKeyPress -= mainCancelHandler;
            Console.CancelKeyPress += subcommandCancelHandler;

            try
            {
                // Create a new root command with the union of the options expected by the standard root
                var commandToInvoke = Program.RootCommand();
                commandToInvoke.Options.AddRange(commandChoice.Options);
                commandToInvoke.Action = commandChoice.Action;
                // Invoke the subcommand in interactive mode with a new cancellation token
                List<string> tokens = [
                    parseResult.RootCommandResult.IdentifierToken.Value,
                    GlobalOptions.Interactive.Name,
                    .. parseResult.Tokens.Where(t => t.Type == TokenType.Option).Select(t => t.Value)
                ];
                var subcommandResult = commandToInvoke.Parse(tokens);
                subcommandResult.InvocationConfiguration.EnableDefaultExceptionHandler = false;

                await subcommandResult.InvokeAsync(cancellationToken: cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                var errorDetail = parseResult.GetRequiredValue(GlobalOptions.ErrorDetail);
                var stacktrace = parseResult.GetRequiredValue(GlobalOptions.Stacktrace);
                AnsiConsole.Console.WritePrettyException(e, errorDetail, stacktrace);
            }
            finally
            {
                Console.CancelKeyPress -= subcommandCancelHandler;
                Console.CancelKeyPress += mainCancelHandler;
            }

            // Go back to the root command
            commandStack = new Stack<Command>([rootCommand]);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("gray").DoubleBorder());
        }
        return 0;
    }

    private static SelectionPrompt<Command> SelectCommandPrompt(List<Command> commands)
    {
        var selectionPrompt = new SelectionPrompt<Command>()
            .PageSize(10)
            .MoreChoicesText("[grey](More)[/]")
            .WrapAround()
            .AddChoices(commands);
        selectionPrompt.Converter = c => new List<Command> { BackCommand, ExitCommand }.Contains(c) ? $"[red]{c.Name}[/]" : $"{c.Name} – [grey dim]{c.Description}[/]";
        selectionPrompt.HighlightStyle(new Style(foreground: Color.Purple, decoration: Decoration.Bold));

        return selectionPrompt;
    }

    private static string BreadCrumbsFromSegments(List<string> segments)
    {
        return string.Join(
            " [white dim]›[/] ",
            segments.Select((c, i) => i == segments.Count - 1
                ? $"[purple bold]{c}[/]"
                : $"[grey]{c}[/]")
        );
    }

    private static async Task PrintAsciiArt()
    {
        var assembly = Assembly.GetExecutingAssembly();

        await using var stream = assembly.GetManifestResourceStream(FontPath);
        if (stream == null)
            throw new FileNotFoundException($"Font not found in assembly: {FontPath}");

        var font = FigletFont.Load(stream);

        AnsiConsole.Write(
            new FigletText(font, "Tracebit CLI")
                .LeftJustified()
                .Color(Color.Purple));
    }
}
