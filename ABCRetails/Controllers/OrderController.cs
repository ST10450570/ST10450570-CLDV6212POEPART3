using System.Security.Claims;
using System.Threading.Tasks;
using ABCRetails.Data;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ABCRetails.Controllers
{
    public class OrderController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly AuthDbContext _authContext;
        private readonly ILogger<OrderController> _logger;

        public OrderController(
            IFunctionsApiService functionsApiService,
            AuthDbContext authContext,
            ILogger<OrderController> logger)
        {
            _functionsApiService = functionsApiService;
            _authContext = authContext;
            _logger = logger;
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string searchTerm)
        {
            var orders = string.IsNullOrEmpty(searchTerm)
                ? await _functionsApiService.GetAllOrdersAsync()
                : await _functionsApiService.SearchOrdersAsync(searchTerm);

            ViewBag.SearchTerm = searchTerm;
            return View("Index", orders);
        }

        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> MyOrders(string searchTerm)
        {
            var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
            var allOrders = await _functionsApiService.GetAllOrdersAsync();
            var myOrders = allOrders.Where(o => o.Username?.Equals(currentUsername, StringComparison.OrdinalIgnoreCase) == true).ToList();

            // Apply search filter if provided
            if (!string.IsNullOrEmpty(searchTerm))
            {
                myOrders = myOrders.Where(o =>
                    (o.ProductName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                    (o.Status?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true) ||
                    (o.RowKey?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true)
                ).ToList();
            }

            ViewBag.SearchTerm = searchTerm;
            return View("MyOrders", myOrders);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create()
        {
            var customers = await _functionsApiService.GetAllCustomersAsync();
            var products = await _functionsApiService.GetAllProductsAsync();

            var viewModel = new OrderCreateViewModel
            {
                Customers = customers,
                Products = products,
                OrderDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc)
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(OrderCreateViewModel model)
        {
            ModelState.Remove("Customers");
            ModelState.Remove("Products");

            if (ModelState.IsValid)
            {
                try
                {
                    var customer = await _functionsApiService.GetCustomerAsync(model.CustomerId);
                    var product = await _functionsApiService.GetProductAsync(model.ProductId);

                    if (customer == null || product == null)
                    {
                        ModelState.AddModelError("", "Invalid customer or product selected.");
                        await PopulateDropdowns(model);
                        return View(model);
                    }

                    if (product.StockAvailable < model.Quantity)
                    {
                        ModelState.AddModelError("Quantity", $"Insufficient stock. Available: {product.StockAvailable}");
                        await PopulateDropdowns(model);
                        return View(model);
                    }

                    var utcOrderDate = DateTime.SpecifyKind(model.OrderDate, DateTimeKind.Utc);

                    var order = new Order
                    {
                        RowKey = Guid.NewGuid().ToString(),
                        CustomerId = model.CustomerId,
                        Username = customer.Username,
                        ProductId = model.ProductId,
                        ProductName = product.ProductName,
                        Quantity = model.Quantity,
                        OrderDate = utcOrderDate,
                        UnitPrice = product.Price,
                        TotalPrice = product.Price * model.Quantity,
                        Status = model.Status,
                    };

                    _logger.LogInformation("Creating order with status: {Status}", order.Status);

                    await _functionsApiService.CreateOrderAsync(order);
                    TempData["Success"] = "Order created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating order");
                    ModelState.AddModelError("", $"Error creating order: {ex.Message}");
                }
            }

            await PopulateDropdowns(model);
            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var order = await _functionsApiService.GetOrderAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            // Check if user has permission to view this order
            if (!await CanAccessOrder(order))
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            return View(order);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> UpdateOrderStatus(string id, string newStatus)
        {
            try
            {
                var success = await _functionsApiService.UpdateOrderStatusAsync(id, newStatus);
                if (success)
                {
                    _logger.LogInformation("Order {OrderId} status updated to {Status}", id, newStatus);
                    return Json(new { success = true, message = "Order status updated successfully!" });
                }
                return Json(new { success = false, message = "Failed to update order status" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order {OrderId} status to {Status}", id, newStatus);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _functionsApiService.DeleteOrderAsync(id);
                TempData["Success"] = "Order deleted successfully!";
                _logger.LogInformation("Order {OrderId} deleted successfully", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order {OrderId}", id);
                TempData["Error"] = $"Error deleting order: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize]
        public async Task<JsonResult> GetProductPrice(string productId)
        {
            try
            {
                var product = await _functionsApiService.GetProductAsync(productId);
                if (product != null)
                {
                    return Json(new
                    {
                        success = true,
                        price = product.Price,
                        stock = product.StockAvailable,
                        productName = product.ProductName,
                    });
                }

                return Json(new { success = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product price for {ProductId}", productId);
                return Json(new { success = false });
            }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var order = await _functionsApiService.GetOrderAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            var viewModel = new OrderEditViewModel
            {
                Id = order.RowKey,
                
                Order = order,
                Customers = await _functionsApiService.GetAllCustomersAsync(),
                Products = await _functionsApiService.GetAllProductsAsync(),
                StatusOptions = new List<string> { "Submitted", "Processing", "Completed", "Cancelled" }
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(string id, OrderEditViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            // Remove ModelState validation for specific fields
            ModelState.Remove("Customers");
            ModelState.Remove("Products");
            ModelState.Remove("StatusOptions");
            ModelState.Remove("Order.ProductImageUrl");

            if (ModelState.IsValid)
            {
                try
                {
                    var originalOrder = await _functionsApiService.GetOrderAsync(id);
                    if (originalOrder == null)
                    {
                        TempData["Error"] = "Order not found. It may have been deleted.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Update only the editable fields
                    originalOrder.Quantity = model.Order.Quantity;
                    originalOrder.Status = model.Order.Status;
                    originalOrder.OrderDate = DateTime.SpecifyKind(model.Order.OrderDate, DateTimeKind.Utc);
                    originalOrder.TotalPrice = originalOrder.UnitPrice * model.Order.Quantity;

                    _logger.LogInformation("Updating order {OrderId} with status: {Status}", id, originalOrder.Status);

                    var updatedOrder = await _functionsApiService.UpdateOrderAsync(originalOrder);

                    _logger.LogInformation("Order {OrderId} updated successfully. New status: {Status}", id, updatedOrder.Status);
                    TempData["Success"] = "Order updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating order {OrderId}", id);
                    ModelState.AddModelError("", $"Error updating order: {ex.Message}");
                }
            }
            else
            {
                // Log model state errors for debugging
                var errors = ModelState.Values.SelectMany(v => v.Errors);
                foreach (var error in errors)
                {
                    _logger.LogWarning("Model validation error: {Error}", error.ErrorMessage);
                }
            }

            // Repopulate dropdowns if we need to return to the form
            model.Customers = await _functionsApiService.GetAllCustomersAsync();
            model.Products = await _functionsApiService.GetAllProductsAsync();
            model.StatusOptions = new List<string> { "Submitted", "Processing", "Completed", "Cancelled" };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ProcessOrder(string id)
        {
            try
            {
                var success = await _functionsApiService.UpdateOrderStatusAsync(id, "PROCESSED");
                if (success)
                {
                    _logger.LogInformation("Order {OrderId} marked as PROCESSED", id);
                    TempData["Success"] = "Order marked as PROCESSED successfully!";
                }
                else
                {
                    TempData["Error"] = "Failed to update order status.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order {OrderId}", id);
                TempData["Error"] = $"Error processing order: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropdowns(OrderCreateViewModel model)
        {
            model.Customers = await _functionsApiService.GetAllCustomersAsync();
            model.Products = await _functionsApiService.GetAllProductsAsync();
        }

        private async Task<bool> CanAccessOrder(Order order)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            if (User.IsInRole("Customer"))
            {
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
                return order.Username?.Equals(currentUsername, StringComparison.OrdinalIgnoreCase) == true;
            }

            return false;
        }
    }
}