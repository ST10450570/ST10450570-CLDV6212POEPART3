// Services/ICustomerSyncService.cs
using ABCRetails.Models;

namespace ABCRetails.Services
{
    public interface ICustomerSyncService
    {
        Task SyncCustomerToTableStorageAsync(User user);
        Task DeleteCustomerFromTableStorageAsync(string tableStorageId);
        Task<bool> IsCustomerInTableStorageAsync(string email);
    }

    public class CustomerSyncService : ICustomerSyncService
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly ILogger<CustomerSyncService> _logger;

        public CustomerSyncService(IFunctionsApiService functionsApiService, ILogger<CustomerSyncService> logger)
        {
            _functionsApiService = functionsApiService;
            _logger = logger;
        }

        public async Task SyncCustomerToTableStorageAsync(User user)
        {
            if (user.Role != "Customer")
            {
                _logger.LogInformation("User {Username} is not a customer, skipping table storage sync", user.Username);
                return;
            }

            try
            {
                // Create customer model for table storage
                var customer = new Customer
                {
                    PartitionKey = "Customer",
                    RowKey = user.TableStorageId ?? Guid.NewGuid().ToString("N"),
                    Name = user.Name ?? user.Username,
                    Surname = user.Surname ?? "",
                    Username = user.Username,
                    Email = user.Email,
                    ShippingAddress = user.ShippingAddress ?? ""
                };

                if (string.IsNullOrEmpty(user.TableStorageId))
                {
                    // Create new customer in table storage
                    var createdCustomer = await _functionsApiService.CreateCustomerAsync(customer);
                    user.TableStorageId = createdCustomer.RowKey;
                    user.IsSyncedWithTableStorage = true;
                    user.LastSyncDate = DateTime.UtcNow;
                    _logger.LogInformation("Created customer {Username} in table storage with ID {Id}", user.Username, user.TableStorageId);
                }
                else
                {
                    // Update existing customer in table storage
                    customer.RowKey = user.TableStorageId;
                    await _functionsApiService.UpdateCustomerAsync(customer);
                    user.LastSyncDate = DateTime.UtcNow;
                    _logger.LogInformation("Updated customer {Username} in table storage", user.Username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing customer {Username} to table storage", user.Username);
                user.IsSyncedWithTableStorage = false;
                throw;
            }
        }

        public async Task DeleteCustomerFromTableStorageAsync(string tableStorageId)
        {
            if (string.IsNullOrEmpty(tableStorageId))
                return;

            try
            {
                await _functionsApiService.DeleteCustomerAsync(tableStorageId);
                _logger.LogInformation("Deleted customer with ID {Id} from table storage", tableStorageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer with ID {Id} from table storage", tableStorageId);
                throw;
            }
        }

        public async Task<bool> IsCustomerInTableStorageAsync(string email)
        {
            try
            {
                var customers = await _functionsApiService.GetAllCustomersAsync();
                return customers.Any(c => c.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if customer with email {Email} exists in table storage", email);
                return false;
            }
        }
    }
}