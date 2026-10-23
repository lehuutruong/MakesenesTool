using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MakesenesTool.Handlers;
using MakesenesTool.Services;
using MakesenesTool.ViewModels;
using MigrationCommon.Abstractions;
using MigrationCommon.Data;
using MigrationCommon.Services;

namespace MakesenesTool;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        _services = services.BuildServiceProvider();

        _services.GetRequiredService<MigrationTrackingContext>().Database.EnsureCreated();

        var window = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MigrationDbContext>(
            o => o.UseSqlServer(configuration.GetConnectionString("PortalDb")),
            ServiceLifetime.Singleton, ServiceLifetime.Singleton);

        services.AddDbContext<MigrationTrackingContext>(
            o => o.UseSqlite(configuration.GetConnectionString("TrackingDb")),
            ServiceLifetime.Singleton, ServiceLifetime.Singleton);

        services.AddTransient<ExcelReaderService>();
        services.AddTransient<BackupService>();
        services.AddTransient<IReportExportService, ReportExportService>();
        services.AddSingleton<WizardState>();
        services.AddTransient<Step1ViewModel>();
        services.AddTransient<Step2ViewModel>();
        services.AddTransient<Step3ViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}
