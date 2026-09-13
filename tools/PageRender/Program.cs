using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinPure.Models;
using WinPure.ViewModels;

// Renders WinPure's pages to PNG without ever showing a window.
//
// Why a tool instead of screenshots: the app cannot be navigated from an automated session, a capture of
// a real window comes back blank unless that window reached the foreground, and a real window on the
// owner's screen is a real app — on 2026-09-12 someone clicked one during a capture and changed real
// startup entries. This loads App.xaml's resources, takes MainWindow's content without showing it, and
// draws it: Dashboard, search, Windows Features, Install Apps, Startup Apps and Repair. Nothing is applied:
// toggles only change in memory, the Install page only reads winget, and Startup only reads the Run keys
// and Startup folders. Restore is left out on purpose: listing backups prepares their ProgramData folder.
//
// Usage: dotnet run --project tools\PageRender -- <output folder> [--es]
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // English unless asked: the app itself would follow this machine's Windows, which is in Spanish.
        WinPure.Services.Loc.Use(args.Contains("--es") ? "es" : "en");
        string outDir = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outDir);
        try
        {
            var app = new WinPure.App();
            app.InitializeComponent();                 // App.xaml resources; StartupUri only applies on Run()
            var window = new WinPure.MainWindow();     // never shown, so its Loaded scan never runs
            var root = (FrameworkElement)window.Content;
            window.Content = null;
            var vm = new MainViewModel();
            root.DataContext = vm;

            Render(root, Path.Combine(outDir, "01-dashboard.png"));

            foreach (var tweak in vm.AllTweaks.Where(t => t.Category == TweakCategory.Privacy).Take(2)) tweak.IsSelected = true;
            vm.AllTweaks.First(t => t.Category == TweakCategory.Features).IsSelected = true;
            vm.SearchText = "turn";
            Render(root, Path.Combine(outDir, "02-search.png"));

            vm.SearchText = "";
            vm.CurrentNav = vm.NavItems.First(n => n.Page is CategoryPageViewModel { Category: TweakCategory.Features });
            Render(root, Path.Combine(outDir, "03-features.png"));

            vm.CurrentNav = vm.NavItems.First(n => n.Page is InstallerViewModel);
            var installer = (InstallerViewModel)vm.CurrentPage;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while ((installer.IsChecking || !installer.HasChecked) && clock.Elapsed < TimeSpan.FromSeconds(90))
                Pump(TimeSpan.FromMilliseconds(500));
            Console.WriteLine($"installer check finished={installer.HasChecked} after {clock.Elapsed.TotalSeconds:0}s: {installer.Summary}");
            Render(root, Path.Combine(outDir, "04-install.png"));

            // Startup reads the Run keys and Startup folders only. No full scan: that would also open the backup
            // store in ProgramData, and this tool never touches anything the app keeps.
            vm.CurrentNav = vm.NavItems.First(n => n.Page is StartupViewModel);
            ((StartupViewModel)vm.CurrentPage).Load(new WinPure.Services.ScanContext());
            Render(root, Path.Combine(outDir, "05-startup.png"));

            vm.CurrentNav = vm.NavItems.First(n => n.Page is RepairViewModel);
            Render(root, Path.Combine(outDir, "06-repair.png"));

            // One removal ticked, so the apply bar shows the hint for changes that cannot be undone.
            vm.AllTweaks.First(t => !t.FullyReversible).IsSelected = true;
            vm.CurrentNav = vm.NavItems.First(n => n.Page is CategoryPageViewModel { Category: TweakCategory.RemoveApps });
            Render(root, Path.Combine(outDir, "07-remove-apps.png"));

            vm.CurrentNav = vm.NavItems.First(n => n.Page is CategoryPageViewModel { Category: TweakCategory.Edge });
            Render(root, Path.Combine(outDir, "08-edge.png"));

            // Cleanup measures folder sizes read-only when opened; wait for it, then draw the sizes.
            vm.CurrentNav = vm.NavItems.First(n => n.Page is CleanupViewModel);
            var cleanup = (CleanupViewModel)vm.CurrentPage;
            var cclock = System.Diagnostics.Stopwatch.StartNew();
            while (!cleanup.HasMeasured && cclock.Elapsed < TimeSpan.FromSeconds(40)) Pump(TimeSpan.FromMilliseconds(500));
            Console.WriteLine($"cleanup measured={cleanup.HasMeasured} after {cclock.Elapsed.TotalSeconds:0}s");
            Render(root, Path.Combine(outDir, "09-cleanup.png"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex);
            return 1;
        }
    }

    private static void Render(FrameworkElement root, string file)
    {
        const int width = 1280, height = 860;
        Pump(TimeSpan.FromMilliseconds(300));   // let bindings and data templates settle
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(300));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen()) dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
        bitmap.Render(backdrop);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
        Console.WriteLine("saved " + file);
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
