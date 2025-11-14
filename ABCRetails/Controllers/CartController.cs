using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Data;
using ABCRetails.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ABCRetails.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CartController : Controller
    {
        private readonly AuthDbContext _authContext;
        private readonly IFunctionsApiService _functionsApiService;
        private readonly ILogger<CartController> _logger;

        public CartController(AuthDbContext authContext, IFunctionsApiService functionsApiService, ILogger<CartController> logger)
        {
            _authContext = authContext;
            _functionsApiService = functionsApiService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Index", "Login");
            }

            var cartItems = await _authContext.Cart
                .Where(c => c.UserId == userId.Value)
                .ToListAsync();

            var viewModel = new List<CartItemViewModel>();

            foreach (var cartItem in cartItems)
            {
                var product = await _functionsApiService.GetProductAsync(cartItem.ProductId);
                if (product != null)
                {
                    viewModel.Add(new CartItemViewModel
                    {
                        CartId = cartItem.Id,
                        ProductId = product.ProductId,
                        ProductName = product.ProductName,
                        ProductImageUrl = product.ImageUrl,
                        Price = product.Price,
                        Quantity = cartItem.Quantity,
                        StockAvailable = product.StockAvailable
                    });
                }
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(string productId, int quantity = 1)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Json(new { success = false, message = "Please log in to add items to cart." });
            }

            try
            {
                if (quantity < 1)
                {
                    return Json(new { success = false, message = "Quantity must be at least 1." });
                }

                _logger.LogInformation("Attempting to add product {ProductId} to cart for user {UserId}", productId, userId);

                var product = await _functionsApiService.GetProductAsync(productId);
                if (product == null)
                {
                    _logger.LogWarning("Product {ProductId} not found", productId);
                    return Json(new { success = false, message = "Product not found." });
                }

                if (product.StockAvailable < quantity)
                {
                    return Json(new { success = false, message = $"Insufficient stock. Available: {product.StockAvailable}" });
                }

                var existingCartItem = await _authContext.Cart
                    .FirstOrDefaultAsync(c => c.UserId == userId.Value && c.ProductId == productId);

                if (existingCartItem != null)
                {
                    var newQuantity = existingCartItem.Quantity + quantity;
                    if (product.StockAvailable < newQuantity)
                    {
                        return Json(new { success = false, message = $"Cannot add more. Available stock: {product.StockAvailable}" });
                    }
                    existingCartItem.Quantity = newQuantity;
                    _logger.LogInformation("Updated existing cart item quantity to {Quantity}", newQuantity);
                }
                else
                {
                    var cartItem = new Cart
                    {
                        UserId = userId.Value,
                        ProductId = productId,
                        Quantity = quantity,
                        AddedAt = DateTime.UtcNow
                    };
                    _authContext.Cart.Add(cartItem);
                    _logger.LogInformation("Added new cart item");
                }

                await _authContext.SaveChangesAsync();
                _logger.LogInformation("Cart changes saved successfully");

                return Json(new { success = true, message = "Product added to cart successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding product {ProductId} to cart", productId);
                return Json(new { success = false, message = $"Error adding product to cart: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCartCount()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Json(new { count = 0 });
            }

            try
            {
                var count = await _authContext.Cart
                    .Where(c => c.UserId == userId.Value)
                    .SumAsync(c => c.Quantity);

                return Json(new { count = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cart count");
                return Json(new { count = 0 });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCartItem(int cartId, int quantity)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Json(new { success = false, message = "Please log in." });
            }

            try
            {
                if (quantity < 1)
                {
                    return await RemoveFromCart(cartId);
                }

                var cartItem = await _authContext.Cart
                    .FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId.Value);

                if (cartItem == null)
                {
                    return Json(new { success = false, message = "Cart item not found." });
                }

                var product = await _functionsApiService.GetProductAsync(cartItem.ProductId);
                if (product == null)
                {
                    return Json(new { success = false, message = "Product not found." });
                }

                if (product.StockAvailable < quantity)
                {
                    return Json(new { success = false, message = $"Insufficient stock. Available: {product.StockAvailable}" });
                }

                cartItem.Quantity = quantity;
                await _authContext.SaveChangesAsync();

                return Json(new { success = true, message = "Cart updated successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cart item {CartId}", cartId);
                return Json(new { success = false, message = "Error updating cart item." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart(int cartId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Json(new { success = false, message = "Please log in." });
            }

            try
            {
                var cartItem = await _authContext.Cart
                    .FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId.Value);

                if (cartItem != null)
                {
                    _authContext.Cart.Remove(cartItem);
                    await _authContext.SaveChangesAsync();
                }

                return Json(new { success = true, message = "Item removed from cart." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cart item {CartId}", cartId);
                return Json(new { success = false, message = "Error removing item from cart." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Index", "Login");
            }

            try
            {
                var user = await _authContext.Users.FindAsync(userId);
                if (user == null)
                {
                    return RedirectToAction("Index", "Login");
                }

                // Get the customer GUID from the API using the username
                var customers = await _functionsApiService.GetAllCustomersAsync();
                var customer = customers.FirstOrDefault(c => c.Username == user.Username);

                if (customer == null)
                {
                    TempData["Error"] = "Customer profile not found. Please contact support.";
                    return RedirectToAction("Index");
                }

                var cartItems = await _authContext.Cart
                    .Where(c => c.UserId == userId.Value)
                    .ToListAsync();

                if (!cartItems.Any())
                {
                    TempData["Error"] = "Your cart is empty.";
                    return RedirectToAction("Index");
                }

                // Create orders for each cart item
                foreach (var cartItem in cartItems)
                {
                    var product = await _functionsApiService.GetProductAsync(cartItem.ProductId);
                    if (product != null && product.StockAvailable >= cartItem.Quantity)
                    {
                        var order = new Order
                        {
                            RowKey = Guid.NewGuid().ToString(),
                            CustomerId = customer.CustomerId, // Use the GUID from the Customer table
                            Username = user.Username,
                            ProductId = product.ProductId,
                            ProductName = product.ProductName,
                            Quantity = cartItem.Quantity,
                            OrderDate = DateTime.UtcNow,
                            UnitPrice = product.Price,
                            TotalPrice = product.Price * cartItem.Quantity,
                            Status = "Submitted",
                            ProductImageUrl = product.ImageUrl
                        };

                        await _functionsApiService.CreateOrderAsync(order);
                    }
                }

                // Clear the cart
                _authContext.Cart.RemoveRange(cartItems);
                await _authContext.SaveChangesAsync();

                TempData["Success"] = "Order placed successfully!";
                return RedirectToAction("Confirmation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during checkout for user {UserId}", userId);
                TempData["Error"] = $"Error processing your order: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public IActionResult Confirmation()
        {
            return View();
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
            return null;
        }
    }
}