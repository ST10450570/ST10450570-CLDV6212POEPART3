using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.IO;

namespace ABCRetailsFunctions.Functions
{
    public class BlobFunctions
    {
        [Function("OnProductImageUploaded")]
        public void OnProductImageUploaded(
            [BlobTrigger("%BLOB_PRODUCT_IMAGES%/{name}", Connection = "STORAGE_CONNECTION")] Stream blob,
            string name,
            FunctionContext ctx)
        {
            var log = ctx.GetLogger("OnProductImageUploaded");

            // Buffer the stream to get the length safely
            using (var memoryStream = new MemoryStream())
            {
                blob.CopyTo(memoryStream);
                log.LogInformation($"Product image uploaded: {name}, size={memoryStream.Length} bytes");

                // Now you can use memoryStream for any processing that requires seekable stream
                // memoryStream.Seek(0, SeekOrigin.Begin); // Reset position if needed for processing
            }

            // Add your image processing logic here if needed
            ProcessProductImage(name, log);
        }

        private void ProcessProductImage(string imageName, ILogger log)
        {
            // Add your image processing logic here
            // For example: generate thumbnails, update database records, etc.
            log.LogInformation($"Processing product image: {imageName}");

            // If you're updating product records but want to keep the same image URL
            // unless a new one is uploaded, make sure your product update logic
            // only changes the image URL when a new file is actually provided
        }
    }
}