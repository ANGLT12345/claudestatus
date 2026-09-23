namespace ClaudeUsageBar;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Dev aid: render the strip + popup with sample data to PNGs.
        if (args.Length > 0 && args[0] == "--render")
        {
            Preview.Render(args.Length > 1 ? args[1] : ".");
            return 0;
        }

        using var mutex = new Mutex(true, @"Local\ClaudeUsageBar.SingleInstance", out bool created);
        if (!created) return 0;

        Application.Run(new AppController());
        return 0;
    }
}
