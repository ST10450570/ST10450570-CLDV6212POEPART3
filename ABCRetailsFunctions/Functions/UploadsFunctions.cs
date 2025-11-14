using System.Net;
using ABCRetailsFunctions.Helpers;
using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace ABCRetailsFunctions.Functions;
public class UploadsFunctions
{
    private readonly string _conn;
    private readonly string _proofs;
    private readonly string _share;
    private readonly string _shareDir;

    public UploadsFunctions(IConfiguration cfg)
    {
        _conn = cfg.GetConnectionString("STORAGE_CONNECTION") ?? throw new ArgumentNullException("STORAGE_CONNECTION");
        _proofs = cfg["BLOB_PAYMENT_PROOFS"] ?? "payment-proofs";
        _share = cfg["FILESHARE_CONTRACTS"] ?? "contracts";
        _shareDir = cfg["FILESHARE_DIR_PAYMENTS"] ?? "payments";
    }

    [Function("Uploads_ProofOfPayment")]
    public async Task<HttpResponseData> Proof(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "uploads/proof-of-payment")] HttpRequestData req)
    {
        var contentType = req.Headers.TryGetValues("Content-Type", out var ct) ? ct.First() : "";
        if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return HttpJson.Bad(req, "Expected multipart/form-data");

        var form = await MultipartHelper.ParseAsync(req.Body, contentType);
        var file = form.Files.FirstOrDefault(f => f.FieldName == "ProofOfPayment");
        if (file is null || file.Data.Length == 0) return HttpJson.Bad(req, "ProofOfPayment file is required");

        var orderId = form.Text.GetValueOrDefault("OrderId");
        var customerName = form.Text.GetValueOrDefault("CustomerName");

        // Blob
        var container = new BlobContainerClient(_conn, _proofs);
        await container.CreateIfNotExistsAsync();
        var blobName = $"{Guid.NewGuid():N}-{file.FileName}";
        var blob = container.GetBlobClient(blobName);

        // FIX: Reset stream position before upload and after
        await using (var s = file.Data)
        {
            // Ensure stream is at beginning for blob upload
            if (s.CanSeek) s.Seek(0, SeekOrigin.Begin);
            await blob.UploadAsync(s);
        }

        // Azure Files - Create new stream for metadata file
        var share = new ShareClient(_conn, _share);
        await share.CreateIfNotExistsAsync();
        var root = share.GetRootDirectoryClient();
        var dir = root.GetSubdirectoryClient(_shareDir);
        await dir.CreateIfNotExistsAsync();

        var fileClient = dir.GetFileClient(blobName + ".txt");
        var meta = $"UploadedAtUtc: {DateTimeOffset.UtcNow:O}\nOrderId: {orderId}\nCustomerName: {customerName}\nBlobUrl: {blob.Uri}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(meta);
        using var ms = new MemoryStream(bytes);
        await fileClient.CreateAsync(ms.Length);

        // FIX: Ensure metadata stream is at beginning
        ms.Seek(0, SeekOrigin.Begin);
        await fileClient.UploadAsync(ms);

        return HttpJson.Ok(req, new { fileName = blobName, blobUrl = blob.Uri.ToString() });
    }

    [Function("Uploads_List")]
    public async Task<HttpResponseData> List(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "uploads")] HttpRequestData req)
    {
        try
        {
            var share = new ShareClient(_conn, _share);
            await share.CreateIfNotExistsAsync();
            var root = share.GetRootDirectoryClient();
            var dir = root.GetSubdirectoryClient(_shareDir);

            var files = new List<object>();

            try
            {
                await foreach (var item in dir.GetFilesAndDirectoriesAsync())
                {
                    if (!item.IsDirectory && item.Name.EndsWith(".txt"))
                    {
                        var fileClient = dir.GetFileClient(item.Name);
                        var props = await fileClient.GetPropertiesAsync();

                        // Read metadata
                        var download = await fileClient.DownloadAsync();
                        using var reader = new StreamReader(download.Value.Content);
                        var content = await reader.ReadToEndAsync();

                        // Parse metadata
                        var lines = content.Split('\n');
                        var metadata = new Dictionary<string, string>();
                        foreach (var line in lines)
                        {
                            var parts = line.Split(':', 2);
                            if (parts.Length == 2)
                                metadata[parts[0].Trim()] = parts[1].Trim();
                        }

                        var fileName = item.Name.Replace(".txt", "");
                        files.Add(new
                        {
                            FileName = fileName,
                            CustomerName = metadata.GetValueOrDefault("CustomerName", "Unknown"),
                            OrderId = metadata.GetValueOrDefault("OrderId", "N/A"),
                            UploadDate = metadata.TryGetValue("UploadedAtUtc", out var date)
                                ? DateTime.Parse(date)
                                : props.Value.LastModified.DateTime,
                            FileSize = props.Value.ContentLength,
                            FileType = "application/octet-stream",
                            Status = "Pending",
                            BlobUrl = metadata.GetValueOrDefault("BlobUrl", "")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Directory might not exist yet
                return HttpJson.Ok(req, new List<object>());
            }

            return HttpJson.Ok(req, files);
        }
        catch (Exception ex)
        {
            return HttpJson.Bad(req, $"Error: {ex.Message}");
        }
    }

    [Function("Uploads_Get")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "uploads/{fileName}")] HttpRequestData req, string fileName)
    {
        try
        {
            var share = new ShareClient(_conn, _share);
            var root = share.GetRootDirectoryClient();
            var dir = root.GetSubdirectoryClient(_shareDir);
            var fileClient = dir.GetFileClient(fileName + ".txt");

            var props = await fileClient.GetPropertiesAsync();
            var download = await fileClient.DownloadAsync();
            using var reader = new StreamReader(download.Value.Content);
            var content = await reader.ReadToEndAsync();

            var lines = content.Split('\n');
            var metadata = new Dictionary<string, string>();
            foreach (var line in lines)
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                    metadata[parts[0].Trim()] = parts[1].Trim();
            }

            var file = new
            {
                FileName = fileName,
                CustomerName = metadata.GetValueOrDefault("CustomerName", "Unknown"),
                OrderId = metadata.GetValueOrDefault("OrderId", "N/A"),
                UploadDate = metadata.TryGetValue("UploadedAtUtc", out var date)
                    ? DateTime.Parse(date)
                    : props.Value.LastModified.DateTime,
                FileSize = props.Value.ContentLength,
                FileType = "application/octet-stream",
                Status = "Pending",
                BlobUrl = metadata.GetValueOrDefault("BlobUrl", "")
            };

            return HttpJson.Ok(req, file);
        }
        catch
        {
            return HttpJson.NotFound(req, "File not found");
        }
    }

    [Function("Uploads_Delete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "uploads/{fileName}")] HttpRequestData req, string fileName)
    {
        try
        {
            // Delete from file share
            var share = new ShareClient(_conn, _share);
            var root = share.GetRootDirectoryClient();
            var dir = root.GetSubdirectoryClient(_shareDir);
            var fileClient = dir.GetFileClient(fileName + ".txt");
            await fileClient.DeleteIfExistsAsync();

            // Delete from blob storage
            var container = new BlobContainerClient(_conn, _proofs);
            var blob = container.GetBlobClient(fileName);
            await blob.DeleteIfExistsAsync();

            return HttpJson.Ok(req, new { success = true });
        }
        catch (Exception ex)
        {
            return HttpJson.Bad(req, $"Error: {ex.Message}");
        }


    }
    [Function("Uploads_Download")]
    public async Task<HttpResponseData> Download(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "uploads/{fileName}/download")] HttpRequestData req, string fileName)
    {
        try
        {
            // Get the blob client
            var container = new BlobContainerClient(_conn, _proofs);
            var blob = container.GetBlobClient(fileName);

            // Check if blob exists
            if (!await blob.ExistsAsync())
            {
                return HttpJson.NotFound(req, "File not found");
            }

            // Download blob content
            var download = await blob.DownloadContentAsync();
            var content = download.Value.Content;

            // Create response with file content
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/octet-stream");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

            await response.WriteBytesAsync(content.ToArray());

            return response;
        }
        catch (Exception ex)
        {
            return HttpJson.Bad(req, $"Error downloading file: {ex.Message}");
        }
    }
}