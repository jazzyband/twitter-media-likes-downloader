namespace MediaDownloader;

// Keep color limited to actionable prompts and important status messages.
// Ordinary explanations and filenames use the console's normal text color.
internal static class ConsoleUi
{
    private static readonly object ConsoleLock = new();

    public static void Heading(string message) => WriteColored(message, ConsoleColor.Cyan, newLine: true);
    public static void Prompt(string message) => WriteColored(message, ConsoleColor.Cyan, newLine: false);
    public static void Progress(string message) => WriteColored(message, ConsoleColor.DarkCyan, newLine: true);
    public static void Success(string message) => WriteColored(message, ConsoleColor.Green, newLine: true);
    public static void Warning(string message) => WriteColored(message, ConsoleColor.Yellow, newLine: true);
    public static void Error(string message) => WriteColored(message, ConsoleColor.Red, newLine: true);

    private static void WriteColored(string message, ConsoleColor color, bool newLine)
    {
        lock (ConsoleLock)
        {
            // Don't add escape sequences or change colors when output is redirected to a file.
            if (Console.IsOutputRedirected)
            {
                WriteText(message, newLine);
                return;
            }

            var original = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                WriteText(message, newLine);
            }
            finally
            {
                Console.ForegroundColor = original;
            }
        }
    }

    private static void WriteText(string message, bool newLine)
    {
        if (newLine)
            Console.WriteLine(message);
        else
            Console.Write(message);
    }
}
