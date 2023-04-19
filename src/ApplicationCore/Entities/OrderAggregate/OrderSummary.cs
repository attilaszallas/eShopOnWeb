using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
public class OrderSummary
{
    public DateTimeOffset? id { get; set; }
    public Address? ShipToAddress { get; set; }
    public IReadOnlyCollection<OrderItem>? OrderItems { get; set; }
    public decimal Total { get; set; }

    public OrderSummary(DateTimeOffset id, Address shipToAddress, IReadOnlyCollection<OrderItem> orderItems, decimal total)
    {
        this.id = id;
        this.ShipToAddress = shipToAddress;
        this.OrderItems = orderItems;
        this.Total = total;
    }
}
