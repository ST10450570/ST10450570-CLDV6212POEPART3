using System.Threading.Tasks;
using ABCRetails.Models;
using ABCRetails.Services;
using ABCRetails.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ABCRetails.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CustomerController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly AuthDbContext _context;
        private readonly ICustomerSyncService _customerSyncService;

        public CustomerController(
            IFunctionsApiService functionsApiService,
            AuthDbContext context,
            ICustomerSyncService customerSyncService)
        {
            _functionsApiService = functionsApiService;
            _context = context;
            _customerSyncService = customerSyncService;
        }

        public async Task<IActionResult> Index(string searchTerm)
        {
            var customers = string.IsNullOrEmpty(searchTerm)
                ? await _functionsApiService.GetAllCustomersAsync()
                : await _functionsApiService.SearchCustomersAsync(searchTerm);

            ViewBag.SearchTerm = searchTerm;
            return View(customers);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Create customer in table storage
                    var createdCustomer = await _functionsApiService.CreateCustomerAsync(customer);

                    // Also create corresponding user in database
                    var user = new User
                    {
                        Username = customer.Username,
                        Email = customer.Email,
                        Name = customer.Name,
                        Surname = customer.Surname,
                        ShippingAddress = customer.ShippingAddress,
                        Role = "Customer",
                        PasswordHash = "default_password_hash", // You might want to generate a temporary password
                        CreatedAt = DateTime.UtcNow,
                        TableStorageId = createdCustomer.RowKey,
                        IsSyncedWithTableStorage = true,
                        LastSyncDate = DateTime.UtcNow
                    };

                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Customer created successfully in both systems!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error creating customer: {ex.Message}");
                }
            }
            return View(customer);
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }
            var customer = await _functionsApiService.GetCustomerAsync(id);
            if (customer == null)
            {
                return NotFound();
            }
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, Customer customer)
        {
            if (id != customer.RowKey)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Get the existing customer to preserve system properties
                    var existingCustomer = await _functionsApiService.GetCustomerAsync(id);
                    if (existingCustomer == null)
                    {
                        TempData["Error"] = "Customer not found. It may have been deleted.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Preserve the system properties
                    customer.PartitionKey = existingCustomer.PartitionKey;
                    customer.RowKey = existingCustomer.RowKey;
                    customer.Timestamp = existingCustomer.Timestamp;
                    customer.ETag = existingCustomer.ETag;

                    // Update customer in table storage
                    await _functionsApiService.UpdateCustomerAsync(customer);

                    // Also update the database user if exists
                    var dbUser = await _context.Users.FirstOrDefaultAsync(u => u.TableStorageId == id);
                    if (dbUser != null)
                    {
                        dbUser.Name = customer.Name;
                        dbUser.Surname = customer.Surname;
                        dbUser.ShippingAddress = customer.ShippingAddress;
                        dbUser.Email = customer.Email;
                        dbUser.Username = customer.Username;
                        dbUser.LastSyncDate = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }

                    TempData["Success"] = "Customer updated successfully in both systems!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error updating customer: {ex.Message}");
                }
            }
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                // Delete from table storage
                await _functionsApiService.DeleteCustomerAsync(id);

                // Also remove the table storage reference from the database user
                var dbUser = await _context.Users.FirstOrDefaultAsync(u => u.TableStorageId == id);
                if (dbUser != null)
                {
                    // Option 1: Remove the sync reference but keep the user
                    dbUser.TableStorageId = null;
                    dbUser.IsSyncedWithTableStorage = false;

                    // Option 2: Delete the user entirely (uncomment if you want to delete both)
                    // _context.Users.Remove(dbUser);

                    await _context.SaveChangesAsync();
                }

                TempData["Success"] = "Customer deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting customer: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // Additional method to sync existing database users to table storage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncToTableStorage(string id)
        {
            try
            {
                var dbUser = await _context.Users.FirstOrDefaultAsync(u => u.Id.ToString() == id || u.TableStorageId == id);
                if (dbUser == null)
                {
                    TempData["Error"] = "User not found in database.";
                    return RedirectToAction(nameof(Index));
                }

                if (dbUser.Role != "Customer")
                {
                    TempData["Error"] = "Only customer users can be synced to table storage.";
                    return RedirectToAction(nameof(Index));
                }

                await _customerSyncService.SyncCustomerToTableStorageAsync(dbUser);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Customer synced to table storage successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error syncing customer: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // Method to view database users that are customers
        public async Task<IActionResult> DatabaseCustomers(string searchTerm)
        {
            var query = _context.Users.Where(u => u.Role == "Customer");

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(u =>
                    u.Username.Contains(searchTerm) ||
                    u.Email.Contains(searchTerm) ||
                    (u.Name != null && u.Name.Contains(searchTerm)) ||
                    (u.Surname != null && u.Surname.Contains(searchTerm)));
            }

            var customers = await query.ToListAsync();
            ViewBag.SearchTerm = searchTerm;
            return View(customers);
        }
    }
}