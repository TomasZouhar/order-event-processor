using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OrderEventProcessor.Migrations;
using OrderEventProcessor.Model;

namespace OrderEventProcessor.Database
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // DbSet properties
        public DbSet<OrderEvent> OrderEvents { get; set; }
        public DbSet<PaymentEvent> PaymentEvents { get; set; }
    }
    
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContextFactory()
        {
        }
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            //optionsBuilder.UseNpgsql("Host=localhost;Database=postgres;Username=postgres;Password=tomzo");
            // use the connection string from appsettings.json, which is located in the project root directory (unlike this file)
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
            
            optionsBuilder.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}