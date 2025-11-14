using System.Text.Json;
using System.Threading.Tasks;
using ABCRetailsFunctions.Entities;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ABCRetailsFunctions.Functions
{
    public class QueueProcessorFunctions
    {
        private readonly string _conn;
        private readonly string _ordersTable;
        private readonly string _productsTable;

        public QueueProcessorFunctions(IConfiguration cfg)
        {
            _conn = cfg.GetConnectionString("STORAGE_CONNECTION") ?? throw new ArgumentNullException("STORAGE_CONNECTION");
            _ordersTable = cfg["TABLE_ORDER"] ?? "Orders";
            _productsTable = cfg["TABLE_PRODUCT"] ?? "Products";
        }

        [Function("OrderNotifications_Processor")]
        public async Task OrderNotificationsProcessor(
    [QueueTrigger("%QUEUE_ORDER_NOTIFICATIONS%", Connection = "STORAGE_CONNECTION")] string message,
    FunctionContext ctx)
        {
            var log = ctx.GetLogger("OrderNotifications_Processor");
            log.LogInformation("Received order notification: {message}", message);

            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;

                if (!root.TryGetProperty("Type", out var typeProp) || typeProp.GetString() != "CreateOrder")
                {
                    log.LogWarning("Message is not of type 'CreateOrder'. Skipping.");
                    return;
                }

                log.LogInformation("Processing CreateOrder message...");

                var ordersTable = new TableClient(_conn, _ordersTable);
                var productsTable = new TableClient(_conn, _productsTable);
                await ordersTable.CreateIfNotExistsAsync();

                var productId = root.GetProperty("ProductId").GetString();
                var quantity = root.GetProperty("Quantity").GetInt32();

                // Create and save the order entity
                var order = new OrderEntity
                {
                    PartitionKey = "Order",
                    RowKey = Guid.NewGuid().ToString("N"),
                    CustomerId = root.GetProperty("CustomerId").GetString(),
                    ProductId = productId,
                    ProductName = root.GetProperty("ProductName").GetString(),
                   
                    Quantity = quantity,
                    UnitPrice = root.GetProperty("UnitPrice").GetDouble(),
                    OrderDateUtc = DateTimeOffset.UtcNow,
                    Status = "Submitted"
                };

                await ordersTable.AddEntityAsync(order);
                log.LogInformation("Order {RowKey} created successfully.", order.RowKey);

                // Update product stock
                var productResponse = await productsTable.GetEntityAsync<ProductEntity>("Product", productId);
                if (productResponse.HasValue)
                {
                    var product = productResponse.Value;
                    product.StockAvailable -= quantity;
                    await productsTable.UpdateEntityAsync(product, product.ETag, TableUpdateMode.Replace);
                    log.LogInformation("Product {ProductId} stock updated. New stock: {Stock}", productId, product.StockAvailable);
                }
                else
                {
                    log.LogError("Could not find product with ID {ProductId} to update stock.", productId);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing order notification message.");
                throw;
            }
        }
    }
}