using ABCRetails.Models;
using ABCRetails.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace ABCRetails.Controllers
{
    [Authorize]
    public class UploadController : Controller
    {
        private readonly IFunctionsApiService _functionsApiService;
        private readonly ILogger<UploadController> _logger;

        public UploadController(IFunctionsApiService functionsApiService, ILogger<UploadController> logger)
        {
            _functionsApiService = functionsApiService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            if (User.IsInRole("Customer"))
            {
                return View(new FileUploadModel());
            }
            else
            {
                // For admin, redirect to uploaded files management page
                return RedirectToAction("UploadedFiles");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Index(FileUploadModel model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (model.ProofOfPayment != null && model.ProofOfPayment.Length > 0)
                    {
                        var currentUsername = User.Identity?.Name ?? "Unknown";
                        var fileName = await _functionsApiService.UploadProofOfPaymentAsync(
                            model.ProofOfPayment, model.OrderId, currentUsername);

                        TempData["Success"] = $"File uploaded successfully! File name: {fileName}";
                        return View(new FileUploadModel());
                    }
                    else
                    {
                        ModelState.AddModelError("ProofOfPayment", "Please select a file to upload.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading file");
                    ModelState.AddModelError("", $"Error uploading file: {ex.Message}");
                }
            }
            return View(model);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UploadedFiles(string searchTerm, string statusFilter = "")
        {
            try
            {
                var uploadedFiles = await _functionsApiService.GetUploadedFilesAsync();

                // Apply search filter
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    uploadedFiles = uploadedFiles.Where(f =>
                        f.FileName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        f.CustomerName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        f.OrderId.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Apply status filter
                if (!string.IsNullOrEmpty(statusFilter))
                {
                    uploadedFiles = uploadedFiles.Where(f =>
                        f.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                ViewBag.SearchTerm = searchTerm;
                ViewBag.StatusFilter = statusFilter;
                ViewBag.TotalFiles = uploadedFiles.Count;
                ViewBag.VerifiedCount = uploadedFiles.Count(f => f.Status == "Verified");
                ViewBag.PendingCount = uploadedFiles.Count(f => f.Status == "Pending");
                ViewBag.RejectedCount = uploadedFiles.Count(f => f.Status == "Rejected");

                return View(uploadedFiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading uploaded files");
                TempData["Error"] = "Error loading uploaded files. Please try again.";
                return View(new List<UploadedFile>());
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> UpdateFileStatus(string fileName, string newStatus)
        {
            try
            {
                // In a real implementation, you would call your API to update the file status
                // For now, we'll simulate the update
                var file = await _functionsApiService.GetUploadedFileAsync(fileName);
                if (file != null)
                {
                    // Here you would typically call an API endpoint to update the file status
                    // await _functionsApiService.UpdateFileStatusAsync(fileName, newStatus);

                    return Json(new { success = true, message = $"File status updated to {newStatus}" });
                }
                return Json(new { success = false, message = "File not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating file status for {FileName}", fileName);
                return Json(new { success = false, message = "Error updating file status" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> DeleteFile(string fileName)
        {
            try
            {
                var success = await _functionsApiService.DeleteUploadedFileAsync(fileName);
                if (success)
                {
                    return Json(new { success = true, message = "File deleted successfully" });
                }
                return Json(new { success = false, message = "Failed to delete file" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file {FileName}", fileName);
                return Json(new { success = false, message = "Error deleting file" });
            }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<JsonResult> GetFileDetails(string fileName)
        {
            try
            {
                var file = await _functionsApiService.GetUploadedFileAsync(fileName);
                if (file != null)
                {
                    return Json(new { success = true, file = file });
                }
                return Json(new { success = false, message = "File not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file details for {FileName}", fileName);
                return Json(new { success = false, message = "Error getting file details" });
            }
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DownloadFile(string fileName)
        {
            try
            {
                var file = await _functionsApiService.GetUploadedFileAsync(fileName);
                if (file == null || string.IsNullOrEmpty(file.BlobUrl))
                {
                    TempData["Error"] = "File not found.";
                    return RedirectToAction("UploadedFiles");
                }

                // Download the file from blob storage via your Functions API
                var fileBytes = await _functionsApiService.DownloadFileAsync(fileName);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    TempData["Error"] = "Failed to download file.";
                    return RedirectToAction("UploadedFiles");
                }

                // Determine content type based on file extension
                var contentType = GetContentType(fileName);

                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file {FileName}", fileName);
                TempData["Error"] = "Error downloading file.";
                return RedirectToAction("UploadedFiles");
            }
        }

        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }
    }
}