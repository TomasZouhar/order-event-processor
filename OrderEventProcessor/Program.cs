using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OrderEventProcessor.Database;
using OrderEventProcessor.Model;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderEventProcessor;
public class Program
{
    // Set VERBOSE to true for detailed logging
    const bool VERBOSE = false;
    private static void Log(string message)
    {
        if (VERBOSE)
        {
            Console.WriteLine(message);
        }
    }
    /*
     * Method used for testing connection to RabbitMQ and Postgres
     */
    private static void TestConnection(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
        
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = configuration.GetSection("RABBIT_HOST").Value,
                UserName = configuration.GetSection("RABBIT_USER").Value,
                Password = configuration.GetSection("RABBIT_PASSWORD").Value,
                VirtualHost = configuration.GetSection("RABBIT_VHOST").Value,
            };
            
            var testConnection = factory.CreateConnection();
            testConnection.Close();
            
            var testDb = new AppDbContextFactory().CreateDbContext(args);
            testDb.Database.EnsureCreated();
        }
        catch (Exception e)
        {
            Log(e.ToString());
            throw new Exception("RabbitMQ or Postgres not running properly");
        }
    }
    /*
     * Method used for sending test messages to RabbitQM queue
     * This method is used for initializing the app with data (for showcase purposes)
     */
    private static void SendTestMessages(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

        // Define connection factory
        var factory = new ConnectionFactory
        {
            HostName = configuration.GetSection("RABBIT_HOST").Value,
            UserName = configuration.GetSection("RABBIT_USER").Value,
            Password = configuration.GetSection("RABBIT_PASSWORD").Value,
            VirtualHost = configuration.GetSection("RABBIT_VHOST").Value,
        };
        using var connection = factory.CreateConnection();
        // Create channel
        using var channel = connection.CreateModel();
        
        // Get biggest order id from DB
        using var db = new AppDbContextFactory().CreateDbContext(args);
        var lastOrderId = db.OrderEvents.Max(o => o.Id);
        var newOrderId = lastOrderId != null ? int.Parse(lastOrderId) + 1 : 0;
        var productId = "Laptop " + newOrderId;
        
        // Declare event-queue
        channel.QueueDeclare(queue: "event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                { "x-max-priority", 2 }
            });
        
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
        paymentEventProperties.Priority = 1;
        
        // Send first payment
        channel.BasicPublish(exchange: "",
            routingKey: "event-queue",
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
        paymentEventProperties.Priority = 1;
        // Send second payment
        channel.BasicPublish(exchange: "",
            routingKey: "event-queue",
            basicProperties: paymentEventProperties,
            body: paymentEventBody);
        
        
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
        orderEventProperties.Priority = 2;
        
        // Send first order
        channel.BasicPublish(exchange: "",
            routingKey: "event-queue",
            basicProperties: orderEventProperties,
            body: orderEventBody);
    }
    
    /*
     * Main method for the app
     * This method is used for receiving messages from RabbitMQ queues and calling processing methods on them
     */
    public static void Main(string[] args)
    {
        // Test connection to RabbitMQ and Postgres
        TestConnection(args);
        
        // Send mock messages to RabbitMQ queue
        SendTestMessages(args);
        
        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();

        // Define connection factory
        var factory = new ConnectionFactory
        {
            HostName = configuration.GetSection("RABBIT_HOST").Value,
            UserName = configuration.GetSection("RABBIT_USER").Value,
            Password = configuration.GetSection("RABBIT_PASSWORD").Value,
            VirtualHost = configuration.GetSection("RABBIT_VHOST").Value,
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        
        // Declare queues
        channel.QueueDeclare(queue: "event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                { "x-max-priority", 2 }
            });
        
        // Define message consumer, which will be used for receiving messages from RabbitMQ queue
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (model, ea) =>
        {
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
                Log($"With type: {msgType}");

                if (msgType == "OrderEvent")
                {
                    ProcessOrderEvent(ea, args);
                }
                else if (msgType == "PaymentEvent")
                {
                    ProcessPaymentEvent(ea, args);
                }
            }
            else
            {
                Log("Invalid header type.");
            }
        };
        channel.BasicConsume(queue: "event-queue", autoAck: true, consumer: consumer);
        
        Console.WriteLine("Press any key to exit.");
        Console.ReadLine();
    }
    
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
    
    