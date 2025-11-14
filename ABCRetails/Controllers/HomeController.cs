using System.Diagnostics;
using System.Security.Claims;
using ABCRetails.Models;
using ABCRetails.Models.ViewModels;
using ABCRetails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ABCRetails.Controllers
{
    public class HomeController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IFunctionsApiService functionsApiService, ILogger<HomeController> logger)
        {
            _functionsApiService = functionsApiService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                if (role == "Admin")
                {
                    return RedirectToAction("AdminDashboard");
                }
                else
                {
                    return RedirectToAction("CustomerDashboard");
                }
            }

            var products = await _functionsApiService.GetAllProductsAsync();
            var customers = await _functionsApiService.GetAllCustomersAsync();
            var orders = await _functionsApiService.GetAllOrdersAsync();

            var viewModel = new HomeViewModel
            {
                FeaturedProducts = products.Take(5).ToList(),
                ProductCount = products.Count,
                CustomerCount = customers.Count,
                OrderCount = orders.Count
            };
            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminDashboard()
        {
            try
            {
                var products = await _functionsApiService.GetAllProductsAsync();
                var customers = await _functionsApiService.GetAllCustomersAsync();
                var orders = await _functionsApiService.GetAllOrdersAsync();

                ViewBag.TotalProducts = products.Count;
                ViewBag.TotalCustomers = customers.Count;
                ViewBag.TotalOrders = orders.Count;
                ViewBag.PendingOrders = orders.Count(o => o.Status == "Submitted" || o.Status == "Processing");

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin dashboard");
                ViewBag.TotalProducts = 0;
                ViewBag.TotalCustomers = 0;
                ViewBag.TotalOrders = 0;
                ViewBag.PendingOrders = 0;
                return View();
            }
        }

        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CustomerDashboard()
        {
            try
            {
                var products = await _functionsApiService.GetAllProductsAsync();
                var featuredProducts = products.Take(4).ToList();

                ViewBag.FeaturedProducts = featuredProducts;
                ViewBag.TotalProducts = products.Count;
                ViewBag.WelcomeMessage = $"Welcome back, {User.FindFirst(ClaimTypes.Name)?.Value}!";

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading customer dashboard");
                ViewBag.FeaturedProducts = new List<Product>();
                ViewBag.TotalProducts = 0;
                ViewBag.WelcomeMessage = $"Welcome back, {User.FindFirst(ClaimTypes.Name)?.Value}!";
                return View();
            }
        }

        [Authorize]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<JsonResult> GetRecentOrders()
        {
            try
            {
                var orders = await _functionsApiService.GetAllOrdersAsync();
                var recentOrders = orders
                    .OrderByDescending(o => o.OrderDate)
                    .Take(5)
                    .Select(o => new
                    {
                        orderId = o.OrderId,
                        customerName = o.Username,
                        productName = o.ProductName,
                        orderDate = o.OrderDate,
                        status = o.Status
                    })
                    .ToList();

                return Json(recentOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent orders");
                return Json(new List<object>());
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<JsonResult> GetLowStockProducts()
        {
            try
            {
                var products = await _functionsApiService.GetAllProductsAsync();
                var lowStockProducts = products
                    .Where(p => p.StockAvailable <= 5) // 5 or less is considered low stock
                    .OrderBy(p => p.StockAvailable)
                    .Take(5)
                    .Select(p => new
                    {
                        productName = p.ProductName,
                        stockAvailable = p.StockAvailable
                    })
                    .ToList();

                return Json(lowStockProducts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting low stock products");
                return Json(new List<object>());
            }
        }

        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<JsonResult> GetMyOrderCount()
        {
            try
            {
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
                var orders = await _functionsApiService.GetAllOrdersAsync();
                var myOrders = orders.Count(o => o.Username == currentUsername);

                return Json(new { count = myOrders });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user order count");
                return Json(new { count = 0 });
            }
        }

        [Authorize(Roles = "Customer")]
        [HttpGet]
        public async Task<JsonResult> GetMyRecentOrders()
        {
            try
            {
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
                var orders = await _functionsApiService.GetAllOrdersAsync();
                var myRecentOrders = orders
                    .Where(o => o.Username == currentUsername)
                    .OrderByDescending(o => o.OrderDate)
                    .Take(5)
                    .Select(o => new
                    {
                        orderId = o.OrderId,
                        productName = o.ProductName,
                        totalPrice = o.TotalPrice.ToString("C"),
                        orderDate = o.OrderDate,
                        status = o.Status
                    })
                    .ToList();

                return Json(myRecentOrders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user recent orders");
                return Json(new List<object>());
            }
        }

        [Authorize(Roles = "Customer")]
        [HttpPost]
        public async Task<JsonResult> AddToCartFromHome(string productId)
        {
            try
            {
                if (string.IsNullOrEmpty(productId))
                {
                    return Json(new { success = false, message = "Product ID is required." });
                }

                // Get the product to verify it exists
                var product = await _functionsApiService.GetProductAsync(productId);
                if (product == null)
                {
                    return Json(new { success = false, message = "Product not found." });
                }

                return Json(new
                {
                    success = true,
                    message = "Product found.",
                    productName = product.ProductName,
                    price = product.Price
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding product {ProductId}", productId);
                return Json(new { success = false, message = "Error finding product." });
            }
        }
    }
}