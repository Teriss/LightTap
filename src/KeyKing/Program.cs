namespace KeyKing;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--core-test")
        {
            CoreChecks.RunAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 2 && args[0] == "--layout-test")
        {
            LayoutChecks.Run(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--self-test")
        {
            Application.Run(new IntegrationChecks(args[1]));
            return;
        }
        using var mutex = new Mutex(true, "Local\\KeyKing.Portable", out bool first);
        if (!first)
        {
            MessageBox.Show("LightTap 已在运行，请查看已打开的窗口。", "LightTap");
            return;
        }
        Application.Run(new MainForm());
    }
}
