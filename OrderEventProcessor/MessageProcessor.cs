using System.Text;
using System.Text.Json;
using OrderEventProcessor.Database;
using OrderEventProcessor.Model;
using RabbitMQ.Client.Events;
using static OrderEventProcessor.Program;

namespace OrderEventProcessor;

/*
 * Class for processing messages received from RabbitMQ queue
 */
public class MessageProcessor
{
    /*
     * Method for processing order events
     * This method is used for processing order events received from RabbitMQ queue
     */
    public static void ProcessOrderEvent(BasicDeliverEventArgs ea, String[] args)
    {
        Log("Received message");
        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        Log($"Message: {message}");
        
        Log("Order event received.");
        var orderEvent = JsonSerializer.Deserialize<OrderEvent>(message);
                    
        if(orderEvent == null)
        {
            return;
        }
                    
        var orderModel = new OrderEvent()
        {
            Id = orderEvent.Id,
            Product = orderEvent.Product,
            Total = orderEvent.Total,
            Currency = orderEvent.Currency
        };
                    
        Log($"Order: {orderModel.Id}, Product: {orderModel.Product}, Total: {orderModel.Total} {orderModel.Currency}");
                
        //store order event in Postgres DB
        using var db = new AppDbContextFactory().CreateDbContext(args);

        try
        {
            db.OrderEvents.Add(orderModel);
        }
        catch (Exception e)
        {
            Log(e.ToString());
            throw;
        }
        db.SaveChanges();
        Log("Order event saved to DB.");
    }

    /*
     * Method for processing payment events
     * This method is used for processing payment events received from RabbitMQ queue
     */
    public static void ProcessPaymentEvent(BasicDeliverEventArgs ea, String[] args)
    {
        Log("Received message");
        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        Log($"Message: {message}");
        
        Log("Payment event received.");
        var paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(message);
        
        if(paymentEvent == null)
        {
            return;
        }

        var paymentModel = new PaymentEvent()
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = paymentEvent.OrderId,
            Amount = paymentEvent.Amount
        };
        
        Log($"Payment: {paymentModel.Id}, Order: {paymentModel.OrderId}, Amount: {paymentModel.Amount}");
        using var db = new AppDbContextFactory().CreateDbContext(args);

        db.PaymentEvents.Add(paymentModel);
        db.SaveChanges();
        Log("Payment event saved to DB.");
    
        // Check if order is fully paid
        var order = db.OrderEvents.FirstOrDefault(o => o.Id == paymentEvent.OrderId);
        if (order != null)
        {
            Log("Order for payment found.");
            var paidAmount = db.PaymentEvents.Where(p => p.OrderId == paymentEvent.OrderId).Sum(p => p.Amount);
            Log($"Paid amount: {paidAmount}");
            if (paidAmount >= order.Total)
            {
                Console.WriteLine($"Order: {order.Id}, Product: {order.Product}, Total: {order.Total} {order.Currency}, Status: PAID");
            }
            else
            {
                Log("Order not fully paid.");
            }
        }
        else
        {
            Log($"Order: {paymentEvent.OrderId} not found.");
        }
    }
}