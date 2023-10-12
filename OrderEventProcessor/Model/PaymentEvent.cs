using Microsoft.EntityFrameworkCore;

namespace OrderEventProcessor.Model;

public class PaymentEvent
{
    public string Id { get; set; }
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}