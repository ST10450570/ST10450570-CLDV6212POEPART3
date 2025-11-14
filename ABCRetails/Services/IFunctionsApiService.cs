using System.Globalization;
using System.Text;
using System.Text.Json;
using ABCRetails.Models;

namespace ABCRetails.Services
{
    public class FunctionsApiService : IFunctionsApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FunctionsApiService> _logger;

        public FunctionsApiService(HttpClient httpClient, ILogger<FunctionsApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        // Customer operations
        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/customers");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Customers API Response: {Content}", content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var customerDtos = JsonSerializer.Deserialize<List<CustomerDto>>(content, options);
                    return customerDtos?.Select(MapToCustomer).ToList() ?? new List<Customer>();
                }
                _logger.LogWarning("Failed to get customers: {StatusCode}", response.StatusCode);
                return new List<Customer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting customers from Functions API");
                return new List<Customer>();
            }
        }

        public async Task<Customer?> GetCustomerAsync(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/customers/{id}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Customer API Response: {Content}", content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var customerDto = JsonSerializer.Deserialize<CustomerDto>(content, options);
                    return customerDto != null ? MapToCustomer(customerDto) : null;
                }
                _logger.LogWarning("Failed to get customer {Id}: {StatusCode}", id, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting customer {Id} from Functions API", id);
                return null;
            }
        }

        public async Task<Customer> CreateCustomerAsync(Customer customer)
        {
            try
            {
                var customerDto = MapToCustomerDto(customer);
                var content = new StringContent(JsonSerializer.Serialize(customerDto), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/customers", content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var createdDto = JsonSerializer.Deserialize<CustomerDto>(responseContent, options);
                    return MapToCustomer(createdDto!);
                }
                throw new Exception($"Failed to create customer: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating customer via Functions API");
                throw;
            }
        }



        public async Task<Customer> UpdateCustomerAsync(Customer customer)
        {
            try
            {
                var customerDto = MapToCustomerDto(customer);
                var content = new StringContent(JsonSerializer.Serialize(customerDto), Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"api/customers/{customer.RowKey}", content);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var updatedDto = JsonSerializer.Deserialize<CustomerDto>(responseContent, options);
                    return MapToCustomer(updatedDto!);
                }
                throw new Exception($"Failed to update customer: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating customer via Functions API");
                throw;
            }
        }

        public async Task DeleteCustomerAsync(string id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/customers/{id}");
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to delete customer: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer via Functions API");
                throw;
            }
        }

        // Product operations
        public async Task<List<Product>> GetAllProductsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/products");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Products API Response: {Content}", content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                    // Try to deserialize as array first
                    try
                    {
                        var productDtos = JsonSerializer.Deserialize<List<ProductDto>>(content, options);
                        return productDtos?.Select(MapToProduct).ToList() ?? new List<Product>();
                    }
                    catch (JsonException)
                    {
                        // If array fails, try single object
                        try
                        {
                            var productDto = JsonSerializer.Deserialize<ProductDto>(content, options);
                            return productDto != null ? new List<Product> { MapToProduct(productDto) } : new List<Product>();
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Failed to deserialize products response");
                            return new List<Product>();
                        }
                    }
                }
                _logger.LogWarning("Failed to get products: {StatusCode}", response.StatusCode);
                return new List<Product>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting products from Functions API");
                return new List<Product>();
            }
        }

        public async Task<Product?> GetProductAsync(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    _logger.LogWarning("GetProductAsync called with null or empty ID");
                    return null;
                }

                var response = await _httpClient.GetAsync($"api/products/{id}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Product API Response for {Id}: {Content}", id, content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                    try
                    {
                        var productDto = JsonSerializer.Deserialize<ProductDto>(content, options);
                        if (productDto != null)
                        {
                            return MapToProduct(productDto);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "JSON deserialization error for product {Id}", id);

                        // Try to parse as a simple object with basic properties
                        try
                        {
                            var jsonDoc = JsonDocument.Parse(content);
                            var product = new Product
                            {
                                RowKey = id,
                                ProductName = jsonDoc.RootElement.GetProperty("productName").GetString() ?? "Unknown",
                                Description = jsonDoc.RootElement.GetProperty("description").GetString() ?? "",
                                Price = jsonDoc.RootElement.GetProperty("price").GetDouble(),
                                StockAvailable = jsonDoc.RootElement.GetProperty("stockAvailable").GetInt32(),
                                ImageUrl = jsonDoc.RootElement.GetProperty("imageUrl").GetString() ?? ""
                            };
                            return product;
                        }
                        catch (Exception parseEx)
                        {
                            _logger.LogError(parseEx, "Failed to parse product {Id} with manual parsing", id);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to get product {Id}: {StatusCode}", id, response.StatusCode);
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product {Id} from Functions API", id);
                return null;
            }
        }

        public async Task<Product> CreateProductAsync(Product product, IFormFile? imageFile = null)
        {
            try
            {
                var productDto = MapToProductDto(product);

                if (imageFile != null)
                {
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(productDto.ProductName), "ProductName");
                    form.Add(new StringContent(productDto.Description), "Description");
                    form.Add(new StringContent(productDto.Price.ToString(CultureInfo.InvariantCulture)), "Price");
                    form.Add(new StringContent(productDto.StockAvailable.ToString()), "StockAvailable");

                    var fileContent = new StreamContent(imageFile.OpenReadStream());
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(imageFile.ContentType);
                    form.Add(fileContent, "ImageFile", imageFile.FileName);

                    var response = await _httpClient.PostAsync("api/products", form);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var createdDto = JsonSerializer.Deserialize<ProductDto>(responseContent, options);
                        return MapToProduct(createdDto!);
                    }
                }
                else
                {
                    var content = new StringContent(JsonSerializer.Serialize(productDto), Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync("api/products", content);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var createdDto = JsonSerializer.Deserialize<ProductDto>(responseContent, options);
                        return MapToProduct(createdDto!);
                    }
                }

                throw new Exception($"Failed to create product");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product via Functions API");
                throw;
            }
        }



        public async Task<Product> UpdateProductAsync(Product product, IFormFile? imageFile = null)
        {
            try
            {
                using var formData = new MultipartFormDataContent();

                // Add product data
                formData.Add(new StringContent(product.ProductName ?? ""), "ProductName");
                formData.Add(new StringContent(product.Description ?? ""), "Description");
                formData.Add(new StringContent(product.Price.ToString(CultureInfo.InvariantCulture)), "Price");
                formData.Add(new StringContent(product.StockAvailable.ToString()), "StockAvailable");

                // Only add image file if provided
                if (imageFile != null && imageFile.Length > 0)
                {
                    var fileContent = new StreamContent(imageFile.OpenReadStream());
                    fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(imageFile.ContentType);
                    formData.Add(fileContent, "ImageFile", imageFile.FileName);
                }

                var response = await _httpClient.PutAsync($"api/products/{product.RowKey}", formData);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to update product: {response.StatusCode} - {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var updatedDto = JsonSerializer.Deserialize<ProductDto>(responseContent, options);
                return MapToProduct(updatedDto!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product via Functions API");
                throw new Exception("Failed to update product", ex);
            }
        }

        public async Task DeleteProductAsync(string id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/products/{id}");
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to delete product: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product via Functions API");
                throw;
            }
        }

        // Order operations
        public async Task<List<Order>> GetAllOrdersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/orders");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Orders API Response: {Content}", content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var orderDtos = JsonSerializer.Deserialize<List<OrderDto>>(content, options);
                    return orderDtos?.Select(MapToOrder).ToList() ?? new List<Order>();
                }
                _logger.LogWarning("Failed to get orders: {StatusCode}", response.StatusCode);
                return new List<Order>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting orders from Functions API");
                return new List<Order>();
            }
        }

        public async Task<Order?> GetOrderAsync(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/orders/{id}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Order API Response for {Id}: {Content}", id, content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var orderDto = JsonSerializer.Deserialize<OrderDto>(content, options);
                    return orderDto != null ? MapToOrder(orderDto) : null;
                }
                _logger.LogWarning("Failed to get order {Id}: {StatusCode}", id, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order {Id} from Functions API", id);
                return null;
            }
        }

        public async Task<Order> CreateOrderAsync(Order order)
        {
            try
            {
                // The API expects only CustomerId, ProductId, and Quantity
                var orderCreate = new
                {
                    CustomerId = order.CustomerId,
                    ProductId = order.ProductId,
                    Quantity = order.Quantity
                };

                _logger.LogInformation("Creating order: CustomerId={CustomerId}, ProductId={ProductId}, Quantity={Quantity}",
                    order.CustomerId, order.ProductId, order.Quantity);

                var content = new StringContent(JsonSerializer.Serialize(orderCreate), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/orders", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var createdDto = JsonSerializer.Deserialize<OrderDto>(responseContent, options);
                    return MapToOrder(createdDto!);
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create order: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new Exception($"Failed to create order: {response.StatusCode} - {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order via Functions API");
                throw;
            }
        }

        public async Task<Order> UpdateOrderAsync(Order order)
        {
            try
            {
                // Create a proper update DTO with all order fields
                var orderUpdate = new
                {
                    CustomerId = order.CustomerId,
                    ProductId = order.ProductId,
                    ProductName = order.ProductName,
                    ProductImageUrl = order.ProductImageUrl,
                    Quantity = order.Quantity,
                    UnitPrice = order.UnitPrice,
                    TotalPrice = order.TotalPrice,
                    OrderDate = order.OrderDate,
                    Status = order.Status
                };

                var content = new StringContent(JsonSerializer.Serialize(orderUpdate), Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"api/orders/{order.RowKey}", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var updatedDto = JsonSerializer.Deserialize<OrderDto>(responseContent, options);
                    return MapToOrder(updatedDto!);
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to update order: {response.StatusCode} - {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order via Functions API");
                throw;
            }
        }

        public async Task DeleteOrderAsync(string id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/orders/{id}");
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to delete order: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order via Functions API");
                throw;
            }
        }

        public async Task<bool> UpdateOrderStatusAsync(string id, string newStatus)
        {
            try
            {
                var statusUpdate = new { Status = newStatus };
                var content = new StringContent(JsonSerializer.Serialize(statusUpdate), Encoding.UTF8, "application/json");

                var response = await _httpClient.PatchAsync($"api/orders/{id}/status", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order status via Functions API");
                return false;
            }
        }

        // Upload operations
        public async Task<string> UploadProofOfPaymentAsync(IFormFile file, string? orderId = null, string? customerName = null)
        {
            try
            {
                using var form = new MultipartFormDataContent();

                if (!string.IsNullOrEmpty(orderId))
                    form.Add(new StringContent(orderId), "OrderId");

                if (!string.IsNullOrEmpty(customerName))
                    form.Add(new StringContent(customerName), "CustomerName");

                var fileContent = new StreamContent(file.OpenReadStream());
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                form.Add(fileContent, "ProofOfPayment", file.FileName);

                var response = await _httpClient.PostAsync("api/uploads/proof-of-payment", form);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<UploadResult>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return result?.FileName ?? string.Empty;
                }
                throw new Exception($"Failed to upload file: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file via Functions API");
                throw;
            }
        }

        public async Task<List<UploadedFile>> GetUploadedFilesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/uploads");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Uploaded files API Response: {Content}", content);

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                    try
                    {
                        var uploadedFiles = JsonSerializer.Deserialize<List<UploadedFile>>(content, options);
                        return uploadedFiles ?? new List<UploadedFile>();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize uploaded files response");
                        // Return empty list if API returns different format or error
                        return new List<UploadedFile>();
                    }
                }

                _logger.LogWarning("Failed to get uploaded files: {StatusCode}", response.StatusCode);
                return new List<UploadedFile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting uploaded files from Functions API");
                return new List<UploadedFile>();
            }
        }

        public async Task<UploadedFile?> GetUploadedFileAsync(string fileName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/uploads/{fileName}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<UploadedFile>(content, options);
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting uploaded file {FileName}", fileName);
                return null;
            }
        }

        public async Task<bool> DeleteUploadedFileAsync(string fileName)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/uploads/{fileName}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting uploaded file {FileName}", fileName);
                return false;
            }
        }

        public async Task<byte[]> DownloadFileAsync(string fileName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/uploads/{fileName}/download");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                _logger.LogWarning("Failed to download file {FileName}: {StatusCode}", fileName, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file {FileName}", fileName);
                return null;
            }
        }

        // Search operations
        public async Task<List<Customer>> SearchCustomersAsync(string searchTerm)
        {
            var allCustomers = await GetAllCustomersAsync();
            if (string.IsNullOrWhiteSpace(searchTerm))
                return allCustomers;

            return allCustomers.Where(c =>
                (c.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Surname?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Username?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Email?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }

        public async Task<List<Product>> SearchProductsAsync(string searchTerm)
        {
            var allProducts = await GetAllProductsAsync();
            if (string.IsNullOrWhiteSpace(searchTerm))
                return allProducts;

            return allProducts.Where(p =>
                (p.ProductName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }

        public async Task<List<Order>> SearchOrdersAsync(string searchTerm)
        {
            var allOrders = await GetAllOrdersAsync();
            if (string.IsNullOrWhiteSpace(searchTerm))
                return allOrders;

            return allOrders.Where(o =>
                (o.ProductName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Username?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Status?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }

        // Mapping methods
        private Customer MapToCustomer(CustomerDto dto) => new Customer
        {
            PartitionKey = "Customer",
            RowKey = dto.Id ?? Guid.NewGuid().ToString(),
            Name = dto.Name ?? string.Empty,
            Surname = dto.Surname ?? string.Empty,
            Username = dto.Username ?? string.Empty,
            Email = dto.Email ?? string.Empty,
            ShippingAddress = dto.ShippingAddress ?? string.Empty
        };

        private CustomerDto MapToCustomerDto(Customer customer) => new CustomerDto(
            customer.Name ?? string.Empty,
            customer.Surname ?? string.Empty,
            customer.Username ?? string.Empty,
            customer.Email ?? string.Empty,
            customer.ShippingAddress ?? string.Empty
        )
        {
            Id = customer.RowKey
        };

        private Product MapToProduct(ProductDto dto) => new Product
        {
            PartitionKey = "Product",
            RowKey = dto.Id ?? Guid.NewGuid().ToString(),
            ProductName = dto.ProductName ?? string.Empty,
            Description = dto.Description ?? string.Empty,
            Price = (double)dto.Price,
            StockAvailable = dto.StockAvailable,
            ImageUrl = dto.ImageUrl ?? string.Empty
        };

        private ProductDto MapToProductDto(Product product) => new ProductDto(
            product.ProductName ?? string.Empty,
            product.Description ?? string.Empty,
            (decimal)product.Price,
            product.StockAvailable,
            product.ImageUrl ?? string.Empty
        )
        {
            Id = product.RowKey
        };

        private Order MapToOrder(OrderDto dto) => new Order
        {
            PartitionKey = "Order",
            RowKey = dto.Id ?? Guid.NewGuid().ToString(),
            CustomerId = dto.CustomerId ?? string.Empty,
            ProductId = dto.ProductId ?? string.Empty,
            ProductName = dto.ProductName ?? string.Empty,
            Quantity = dto.Quantity,
            UnitPrice = (double)dto.UnitPrice,
            TotalPrice = (double)dto.TotalAmount,
            OrderDate = dto.OrderDateUtc.DateTime,
            Status = dto.Status ?? "Submitted",
            Username = dto.Username ?? dto.CustomerId ?? string.Empty, // Use username from DTO
           
        };

        // DTO records for Functions API
        private record CustomerDto(string Name, string Surname, string Username, string Email, string ShippingAddress)
        {
            public string Id { get; set; } = string.Empty;
        }

        private record ProductDto(string ProductName, string Description, decimal Price, int StockAvailable, string ImageUrl)
        {
            public string Id { get; set; } = string.Empty;
        }

        // In FunctionsApiService.cs, update the private OrderDto record:
        private record OrderDto(
            string Id, string CustomerId, string ProductId, string ProductName,
            int Quantity, decimal UnitPrice, decimal TotalAmount, DateTimeOffset OrderDateUtc, string Status, string Username);


        private record OrderCreateDto(string CustomerId, string ProductId, int Quantity);

        private record UploadResult(string FileName, string BlobUrl);
    }
}