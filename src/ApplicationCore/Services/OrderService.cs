using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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
                return "";
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
        // await PostOrder(order, FunctionAppRequestUri);
        await _orderRepository.AddAsync(order);
    }

    public async Task<string> PostOrder(Order order, string requestUri)
    {
        string orderInJson = JsonConvert.SerializeObject(order);
        HttpClient httpclient = new HttpClient();
        HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequestMessage.Content = new StringContent(orderInJson);
        HttpResponseMessage httpResponseMessage = await httpclient.SendAsync(httpRequestMessage);

        var responseResults = httpResponseMessage.Content.ReadFromJsonAsync<dynamic>().ToString();
        return !string.IsNullOrEmpty(responseResults) ? responseResults : "Function App call failed!";
    }
}
