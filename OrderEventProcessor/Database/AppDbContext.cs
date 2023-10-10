using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
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
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql("Host=localhost;Database=postgres;Username=postgres;Password=tomzo");

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}