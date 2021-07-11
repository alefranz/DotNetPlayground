using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using System;

namespace Configuration
{
    class Program
    {
        static void Main(string[] args)
        {
            Environment.SetEnvironmentVariable("APP_Section__", "1");

            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.overrides.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("APP_");

            if (args != null)
            {
                configurationBuilder.AddCommandLine(args);
            }

            var config = configurationBuilder.Build();
            foreach (var configSetting in config.AsEnumerable())
            {
                Console.Write(configSetting.Key);
                Console.Write("=");
                Console.Write(configSetting.Value);
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("Section");

            var section = config.GetSection("Section");
            foreach (var configSetting in section.GetChildren())
            {
                Console.Write(configSetting.Key);
                Console.Write("=");
                Console.Write(configSetting.Value);
                Console.WriteLine();
            }
        }

        
    }
}
