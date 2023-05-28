using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.Azure.ServiceBus;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Newtonsoft.Json;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;

    string FunctionAppRequestUri
    {
        get {
            string? functionAppRequestUri = Environment.GetEnvironmentVariable("OrderItemsReserverFunctionAppLink");

            if (!string.IsNullOrEmpty(functionAppRequestUri))
            {
                return functionAppRequestUri;
            }
            else
            {
                return "https://orderitemreserver.azurewebsites.net";
            }
        }
    }

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        OrderSummary orderSummary = new OrderSummary(order.OrderDate, order.ShipToAddress, order.OrderItems, order.Total());
        // await PostJson(orderSummary, FunctionAppRequestUri);

        await PostJsonViaServiceBus(orderSummary);
        await _orderRepository.AddAsync(order);
    }

    public async Task PostJsonViaServiceBus(OrderSummary orderSummary)
    {
        const string QueueName = "OrderItemReserverBus";

        // Service Bus Namespace / Shared access policies / RootManageSharedAccessKey -> Primary Connection String
        const string ServiceBusConnectionString = "Endpoint=sb://szallaseshoponweb.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=hGeGiEIgtjWwnEehUgAK3s47oZjdFvXPR+ASbP6h/yg=";
        
        IQueueClient queueClient = new QueueClient(ServiceBusConnectionString, QueueName);

        if (orderSummary.OrderItems == null)
        {
            throw new Exception($"{nameof(orderSummary.OrderItems)} is null here.");
        }

        var json = OrderSummaryReduce(orderSummary.OrderItems);
        string orderInJson = JsonConvert.SerializeObject(json);
        var message = new Message(Encoding.UTF8.GetBytes(orderInJson));

        await queueClient.SendAsync(message);
        await queueClient.CloseAsync();
    }

    public async Task<string> PostJson(object json, string requestUri)
    {
        string orderInJson = JsonConvert.SerializeObject(json);
        HttpClient httpclient = new HttpClient();
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequestMessage.Content = new StringContent(orderInJson);
        HttpResponseMessage httpResponseMessage = await httpclient.SendAsync(httpRequestMessage);

        var responseResults = httpResponseMessage.Content.ReadFromJsonAsync<dynamic>().ToString();
        return !string.IsNullOrEmpty(responseResults) ? responseResults : "Function App call failed!";
    }

    private IReadOnlyCollection<ReducedOrderSummary> OrderSummaryReduce(IReadOnlyCollection<OrderItem> orderItems)
    {
        if (orderItems == null)
        {
            throw new Exception($"{nameof(orderItems)} is null.");
        }

        Collection < ReducedOrderSummary > reducedOrderSummaryCollection = new Collection<ReducedOrderSummary>();

        int orderCount = orderItems.Count;

        ReducedOrderSummary reducedOrderSummary;
        for (int i = 0; i < orderCount; i++)
        {
            reducedOrderSummary = new ReducedOrderSummary();

            reducedOrderSummary.ItemId = orderItems.ElementAt(i).ItemOrdered.CatalogItemId;
            reducedOrderSummary.Quantity = orderItems.ElementAt(i).Units;

            reducedOrderSummaryCollection.Add(reducedOrderSummary);
        }

        return reducedOrderSummaryCollection;
    }
}

public class ReducedOrderSummary
{ 
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}
