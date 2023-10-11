// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;
using OrderEventProcessor.Database;
using OrderEventProcessor.Model;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderEventProcessor;
public class Program
{
    private static void SendTestMessages()
    {
        var factory = new ConnectionFactory
        {
            HostName = "rabbitmq",
            UserName = "tomzo",
            Password = "tomzo",
            VirtualHost = "/"
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        
        channel.QueueDeclare(queue: "order-event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        
        // Send message to order-event-queue
        var orderEvent = new OrderEvent
        {
            Id = "4",
            Product = "1ABC",
            Total = 10.00m,
            Currency = "USD"
        };
        var orderEventJson = JsonSerializer.Serialize(orderEvent);
        var orderEventBody = Encoding.UTF8.GetBytes(orderEventJson);
        var orderEventProperties = channel.CreateBasicProperties();
        orderEventProperties.Headers = new Dictionary<string, object>();
        orderEventProperties.Headers.Add("X-MsgType", "OrderEvent");
        channel.BasicPublish(exchange: "",
            routingKey: "order-event-queue",
            basicProperties: orderEventProperties,
            body: orderEventBody);
        
        // Send message to payment-event-queue
        var paymentEvent = new PaymentEvent
        {
            Id = "1",
            OrderId = "4",
            Amount = 10.00m
        };
        var paymentEventJson = JsonSerializer.Serialize(paymentEvent);
        var paymentEventBody = Encoding.UTF8.GetBytes(paymentEventJson);
        var paymentEventProperties = channel.CreateBasicProperties();
        paymentEventProperties.Headers = new Dictionary<string, object>();
        paymentEventProperties.Headers.Add("X-MsgType", "PaymentEvent");
        channel.BasicPublish(exchange: "",
            routingKey: "payment-event-queue",
            basicProperties: paymentEventProperties,
            body: paymentEventBody);
    }
    public static void Main(string[] args)
    {
        SendTestMessages();
        var factory = new ConnectionFactory
        {
            HostName = "rabbitmq",
            UserName = "tomzo",
            Password = "tomzo",
            VirtualHost = "/"
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        
        channel.QueueDeclare(queue: "order-event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        
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
                var msgTypeBytes = ea.BasicProperties.Headers["X-MsgType"] as byte[];
                if (msgTypeBytes == null)
                {
                    return;
                }
                var msgType = Encoding.UTF8.GetString(msgTypeBytes);
                Console.WriteLine($"Message type: {msgType}");

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
                        Id = Guid.NewGuid().ToString(),
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
                
                    //check if order is fully paid
                    var order = db.OrderEvents.FirstOrDefault(o => o.Id == paymentEvent.OrderId);
                    if (order != null)
                    {
                        Console.WriteLine("Order for payment found.");
                        var paidAmount = db.PaymentEvents.Where(p => p.OrderId == paymentEvent.OrderId).Sum(p => p.Amount);
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
                Console.WriteLine("Message type header (X-MsgType) not found.");
            }
        };
        
        channel.BasicConsume(queue: "order-event-queue", autoAck: true, consumer: consumer);
        channel.BasicConsume(queue: "payment-event-queue", autoAck: true, consumer: consumer);
        
        Console.WriteLine("Press [enter] to exit.");
        Console.ReadLine();
    }
}
    
    