using ABCRetails.Models;
using System.Text;
using System.Text.Json;

namespace ABCRetails.Services
{
    public interface IFunctionsApiService
    {
        // Customer operations
        Task<List<Customer>> GetAllCustomersAsync();
        Task<Customer?> GetCustomerAsync(string id);
        Task<Customer> CreateCustomerAsync(Customer customer);
        Task<Customer> UpdateCustomerAsync(Customer customer);
        Task DeleteCustomerAsync(string id);

        // Product operations
        Task<List<Product>> GetAllProductsAsync();
        Task<Product?> GetProductAsync(string id);
        Task<Product> CreateProductAsync(Product product, IFormFile? imageFile = null);
        Task<Product> UpdateProductAsync(Product product, IFormFile? imageFile = null);
        Task DeleteProductAsync(string id);

        // Order operations
        Task<List<Order>> GetAllOrdersAsync();
        Task<Order?> GetOrderAsync(string id);
        Task<Order> CreateOrderAsync(Order order);
        Task<Order> UpdateOrderAsync(Order order);
        Task DeleteOrderAsync(string id);
        Task<bool> UpdateOrderStatusAsync(string id, string newStatus);

        // Upload operations
        Task<string> UploadProofOfPaymentAsync(IFormFile file, string? orderId = null, string? customerName = null);
        
        Task<List<UploadedFile>> GetUploadedFilesAsync();
        Task<UploadedFile?> GetUploadedFileAsync(string fileName);
        Task<bool> DeleteUploadedFileAsync(string fileName);
        Task<byte[]> DownloadFileAsync(string fileName);

        // Search operations
        Task<List<Customer>> SearchCustomersAsync(string searchTerm);
        Task<List<Product>> SearchProductsAsync(string searchTerm);
        Task<List<Order>> SearchOrdersAsync(string searchTerm);


    }



    public class UploadedFile
    {
        public string FileName { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public DateTime UploadDate { get; set; }
        public long FileSize { get; set; }
        public string FileType { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string BlobUrl { get; set; } = string.Empty;
    }



}