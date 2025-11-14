using ABCRetails.Models;
using ABCRetails.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace ABCRetails.Controllers
{
    public class ProductController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly ILogger<ProductController> _logger;

        public ProductController(IFunctionsApiService functionsApiService, ILogger<ProductController> logger)
        {
            _functionsApiService = functionsApiService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string searchTerm)
        {
            var products = string.IsNullOrEmpty(searchTerm)
                ? await _functionsApiService.GetAllProductsAsync()
                : await _functionsApiService.SearchProductsAsync(searchTerm);

            ViewBag.SearchTerm = searchTerm;
            ViewBag.IsCustomer = User.IsInRole("Customer");
            return View(products);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Product product, IFormFile? imageFile)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (product.Price <= 0)
                    {
                        ModelState.AddModelError("Price", "Price must be greater than 0.");
                        return View(product);
                    }

                    await _functionsApiService.CreateProductAsync(product, imageFile);
                    TempData["Success"] = $"Product '{product.ProductName}' created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating product");
                    ModelState.AddModelError("", $"Error creating product: {ex.Message}");
                }
            }
            return View(product);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }
            var product = await _functionsApiService.GetProductAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(Product product, IFormFile? imageFile)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var originalProduct = await _functionsApiService.GetProductAsync(product.RowKey);
                    if (originalProduct == null)
                    {
                        return NotFound();
                    }

                    // Preserve system properties
                    product.PartitionKey = originalProduct.PartitionKey;
                    product.RowKey = originalProduct.RowKey;
                    product.Timestamp = originalProduct.Timestamp;
                    product.ETag = originalProduct.ETag;

                    await _functionsApiService.UpdateProductAsync(product, imageFile);
                    TempData["Success"] = "Product updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating product");
                    ModelState.AddModelError("", $"Error updating product: {ex.Message}");
                }
            }
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _functionsApiService.DeleteProductAsync(id);
                TempData["Success"] = "Product deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting product: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<JsonResult> AddToCart(string productId, int quantity = 1)
        {
            try
            {
                if (quantity < 1)
                {
                    return Json(new { success = false, message = "Quantity must be at least 1." });
                }

                var product = await _functionsApiService.GetProductAsync(productId);
                if (product == null)
                {
                    return Json(new { success = false, message = "Product not found." });
                }

                if (product.StockAvailable < quantity)
                {
                    return Json(new { success = false, message = $"Insufficient stock. Available: {product.StockAvailable}" });
                }

                // Since we can't directly access CartController from here, return success
                // The actual cart addition will be handled by JavaScript
                return Json(new { success = true, message = "Product can be added to cart." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking product {ProductId}", productId);
                return Json(new { success = false, message = "Error checking product." });
            }
        }
    }
}