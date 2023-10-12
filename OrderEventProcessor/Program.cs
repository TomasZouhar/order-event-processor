using System.Text;
using System.Text.Json;
using OrderEventProcessor.Database;
using OrderEventProcessor.Model;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderEventProcessor;
public class Program
{
    /*
     * Method used for sending two test messages to RabbitQM queue
     * This method is used for testing the app
     */
    private static void SendTestMessages(string[] args)
    {
        // Define connection factory
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "tomzo",
            Password = "tomzo",
            VirtualHost = "/"
        };
        using var connection = factory.CreateConnection();
        // Create channel
        using var channel = connection.CreateModel();
        
        // Get biggest order id from DB
        using var db = new AppDbContextFactory().CreateDbContext(args);
        var lastOrderId = db.OrderEvents.Max(o => o.Id);
        var newOrderId = lastOrderId != null ? int.Parse(lastOrderId) + 1 : 0;
        var productId = "Laptop " + newOrderId;
        
        // Declare order-event-queue
        channel.QueueDeclare(queue: "order-event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        
        // Prepare first order
        var orderEvent = new OrderEvent
        {
            Id = newOrderId.ToString(),
            Product = productId,
            Total = 10000.00m,
            Currency = "CZK"
        };
        var orderEventJson = JsonSerializer.Serialize(orderEvent);
        var orderEventBody = Encoding.UTF8.GetBytes(orderEventJson);
        var orderEventProperties = channel.CreateBasicProperties();
        orderEventProperties.Headers = new Dictionary<string, object>();
        orderEventProperties.Headers.Add("X-MsgType", "OrderEvent");
        
        // Send first order
        channel.BasicPublish(exchange: "",
            routingKey: "order-event-queue",
            basicProperties: orderEventProperties,
            body: orderEventBody);
        
        // Prepare first payment
        var paymentEvent = new PaymentEvent
        {
            OrderId = newOrderId.ToString(),
            Amount = 5000.00m
        };
        var paymentEventJson = JsonSerializer.Serialize(paymentEvent);
        var paymentEventBody = Encoding.UTF8.GetBytes(paymentEventJson);
        var paymentEventProperties = channel.CreateBasicProperties();
        paymentEventProperties.Headers = new Dictionary<string, object>();
        paymentEventProperties.Headers.Add("X-MsgType", "PaymentEvent");
        
        // Send first payment
        channel.BasicPublish(exchange: "",
            routingKey: "payment-event-queue",
            basicProperties: paymentEventProperties,
            body: paymentEventBody);
        
        // Prepare second payment
        paymentEvent = new PaymentEvent
        {
            OrderId = newOrderId.ToString(),
            Amount = 5000.00m
        };
        paymentEventJson = JsonSerializer.Serialize(paymentEvent);
        paymentEventBody = Encoding.UTF8.GetBytes(paymentEventJson);
        paymentEventProperties = channel.CreateBasicProperties();
        paymentEventProperties.Headers = new Dictionary<string, object>();
        paymentEventProperties.Headers.Add("X-MsgType", "PaymentEvent");
        // Send second payment
        channel.BasicPublish(exchange: "",
            routingKey: "payment-event-queue",
            basicProperties: paymentEventProperties,
            body: paymentEventBody);
    }
    
    /*
     * Main method for the app
     * This method is used for receiving messages from RabbitMQ queues and processing them
     */
    public static void Main(string[] args)
    {
        SendTestMessages(args);
        // Define connection factory
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "tomzo",
            Password = "tomzo",
            VirtualHost = "/"
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        
        // Declare queues
        channel.QueueDeclare(queue: "order-event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        
        channel.QueueDeclare(queue: "payment-event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        
        // Define message consumer, which will be used for receiving messages from RabbitMQ queue
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (model, ea) =>
        {
            Console.WriteLine("Received message");
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            Console.WriteLine($"Message: {message}");

            // Check if the "X-MsgType" header exists
            if (ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.ContainsKey("X-MsgType"))
            {
                // Get header value
                var msgTypeBytes = ea.BasicProperties.Headers["X-MsgType"] as byte[];
                if (msgTypeBytes == null)
                {
                    return;
                }
                var msgType = Encoding.UTF8.GetString(msgTypeBytes);
                Console.WriteLine($"With type: {msgType}");

                if (msgType == "OrderEvent")
                {
                    Console.WriteLine("Order event received.");
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
                    
                    Console.WriteLine($"Order: {orderModel.Id}, Product: {orderModel.Product}, Total: {orderModel.Total} {orderModel.Currency}");
                
                    //store order event in Postgres DB
                    using var db = new AppDbContextFactory().CreateDbContext(args);

                    try
                    {
                        db.OrderEvents.Add(orderModel);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                    db.SaveChanges();
                    Console.WriteLine("Order event saved to DB.");
                }
                else if (msgType == "PaymentEvent")
                {
                    Console.WriteLine("Payment event received.");
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
                    
                    Console.WriteLine($"Payment: {paymentModel.Id}, Order: {paymentModel.OrderId}, Amount: {paymentModel.Amount}");
                    using var db = new AppDbContextFactory().CreateDbContext(args);

                    db.PaymentEvents.Add(paymentModel);
                    db.SaveChanges();
                    Console.WriteLine("Payment event saved to DB.");
                
                    // Check if order is fully paid
                    var order = db.OrderEvents.FirstOrDefault(o => o.Id == paymentEvent.OrderId);
                    if (order != null)
                    {
                        Console.WriteLine("Order for payment found.");
                        var paidAmount = db.PaymentEvents.Where(p => p.OrderId == paymentEvent.OrderId).Sum(p => p.Amount);
                        Console.WriteLine($"Paid amount: {paidAmount}");
                        if (paidAmount >= order.Total)
                        {
                            Console.WriteLine($"Order: {order.Id}, Product: {order.Product}, Total: {order.Total} {order.Currency}, Status: PAID");
                        }
                        else
                        {
                            Console.WriteLine("Order not fully paid.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Order: {paymentEvent.OrderId} not found.");
                    }
                }
            }
            else
            {
                Console.WriteLine("Invalid header type.");
            }
        };
        channel.BasicConsume(queue: "order-event-queue", autoAck: true, consumer: consumer);
        channel.BasicConsume(queue: "payment-event-queue", autoAck: true, consumer: consumer);
        
        Console.WriteLine("Press any key to exit.");
        Console.ReadLine();
    }
}
    
    