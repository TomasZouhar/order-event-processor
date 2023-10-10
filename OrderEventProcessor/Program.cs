// See https://aka.ms/new-console-template for more information

using System.Text;
using OrderEventProcessor.Database;
using OrderEventProcessor.Model;
using RabbitMQ.Client;

public class Program
{
    public static void Main(string[] args)
    {
        using (var db = new AppDbContextFactory().CreateDbContext(args))
        {
            // create a new order
            var order = new OrderEvent
            {
                Id = Guid.NewGuid().ToString(),
                Product = "Product",
                Total = 499.99m,
                Currency = "CZK"
            };

            // add the order to the database
            db.OrderEvents.Add(order);
            db.SaveChanges();

            // create a new payment
            var payment = new PaymentEvent
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = order.Id,
                Amount = order.Total
            };

            // add the payment to the database
            db.PaymentEvents.Add(payment);
            db.SaveChanges();

            // read the order from the database
            var orderFromDb = db.OrderEvents.Find(order.Id);
            Console.WriteLine(orderFromDb != null
                ? $"Order: {orderFromDb.Product} for {orderFromDb.Total} {orderFromDb.Currency}"
                : "Order not found");

            // read the payment from the database
            var paymentFromDb = db.PaymentEvents.Find(payment.Id);
            Console.WriteLine(paymentFromDb != null
                ? $"Payment: {paymentFromDb.Amount} for order {paymentFromDb.OrderId}"
                : "Payment not found");
        }
    }
}
    
    