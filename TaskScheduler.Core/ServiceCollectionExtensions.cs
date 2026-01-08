using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace TaskScheduler.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        // if you are using persistent job store, you might want to alter some options
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.IgnoreDuplicates = true; // default: false
            options.Scheduling.OverWriteExistingData = true; // default: true
            configuration.GetSection("Quartz").Bind(options); // base configuration from appsettings.json
        });

        services.AddQuartz(q =>
        {
            q.UsePersistentStore(s =>
            {
                s.PerformSchemaValidation = true; // default
                s.UseProperties = true; // preferred, but not default
                s.RetryInterval = TimeSpan.FromSeconds(15);
                s.UseSQLite(options =>
                {
                    options.ConnectionString = "some connection string";
                    // this is the default
                    options.TablePrefix = "QRTZ_";
                });
                s.UseSystemTextJsonSerializer();
                s.UseClustering(c =>
                {
                    c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                    c.CheckinInterval = TimeSpan.FromSeconds(10);
                });
            });
        });

        // Quartz.Extensions.Hosting allows you to fire background service that handles scheduler lifecycle
        services.AddQuartzHostedService(options =>
        {
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });
        return services;
    }
}