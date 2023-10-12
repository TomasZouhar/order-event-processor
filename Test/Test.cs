using System.Text;
using System.Text.Json;
using OrderEventProcessor.Database;
using OrderEventProcessor.Model;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderEventProcessor.Tests;

[TestFixture]
public class Test
{
    [SetUp]
    /*
     * Method used for enqueuing test messages to RabbitQM queue
     * The messages are deliberately enqueued in wrong order
     */
    public void SendTestMessages()
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
        
        // Declare order-event-queue
        channel.QueueDeclare(queue: "test-event-queue",
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
            Id = "10",
            OrderId = "1",
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
            routingKey: "test-event-queue",
            basicProperties: paymentEventProperties,
            body: paymentEventBody);
        
        // Prepare second payment
        paymentEvent = new PaymentEvent
        {
            Id = "11",
            OrderId = "1",
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
            routingKey: "test-event-queue",
            basicProperties: paymentEventProperties,
            body: paymentEventBody);
        
        
        // Prepare first order
        var orderEvent = new OrderEvent
        {
            Id = "1",
            Product = "Testing product",
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
            routingKey: "test-event-queue",
            basicProperties: orderEventProperties,
            body: orderEventBody);
    }
    
    [Test]
    /*
     * Test if priority queue is working correctly
     */
    public void TestQueueOrder()
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "tomzo",
            Password = "tomzo",
            VirtualHost = "/"
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue: "test-event-queue",
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                { "x-max-priority", 2 }
            });
        
        // Check if queue contains 3 messages
        var queueInfo = channel.QueueDeclarePassive("test-event-queue");
        Assert.That(queueInfo.MessageCount, Is.EqualTo(3));
        
        // Get first 3 messages from queue
        var messages = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var message = channel.BasicGet("test-event-queue", true);
            // If priority is 1, add item to end of list, otherwise add to beginning (priority queue simulation)
            if (message.BasicProperties.Priority == 1)
            {
                messages.Add(Encoding.UTF8.GetString(message.Body.ToArray()));
            }
            else
            {
                messages.Insert(0, Encoding.UTF8.GetString(message.Body.ToArray()));
            }
        }
        
        // Check if order is first
        Assert.That(messages[0], Is.EqualTo("{\"Id\":\"1\",\"Product\":\"Testing product\",\"Total\":10000.00,\"Currency\":\"CZK\"}"));
    }
    
    [TearDown]
    /*
     * Method used for clearing RabbitMQ queue after finishing tests
     */
    public void ClearQueue()
    {
        var factory = new ConnectionFactory
        {
            HostName = "localhost",
            UserName = "tomzo",
            Password = "tomzo",
            VirtualHost = "/"
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueuePurge("test-event-queue");
        connection.Close();
    }
}