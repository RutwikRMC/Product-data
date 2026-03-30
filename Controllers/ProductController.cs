using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ProductImageUpload.Controllers
{
    public class ProductController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _config;

        public ProductController(IWebHostEnvironment env, IAmazonS3 s3Client, IConfiguration config)
        {
            _env = env;
            _s3Client = s3Client;
            _config = config;
        }               

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadProduct(string ProductName, IFormFile ProductImage)
        {
            if (ProductImage == null || ProductImage.Length == 0)
            {
                ModelState.AddModelError(nameof(ProductImage), "Please provide a non-empty image file.");
                return BadRequest(ModelState);
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath ?? string.Empty, "Products");
            Directory.CreateDirectory(uploadsFolder);

            var safeFileName = Path.GetFileName(ProductImage.FileName) ?? Guid.NewGuid().ToString();
            var extension = Path.GetExtension(safeFileName);
            var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await ProductImage.CopyToAsync(stream);
            }

            var relativePath = Path.Combine("Products", uniqueFileName).Replace("\\", "/");
            return Ok(new { ProductName, ImageUrl = "/" + relativePath });
        }

        [HttpGet]
        public IActionResult GetProductDetails(int productId)
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddProduct(string ProductName, string ProductDescription, decimal ProductPrice)
        {
            var newProduct = new
            {
                Id = Guid.NewGuid(),
                Name = ProductName,
                Description = ProductDescription,
                Price = ProductPrice
            };

            return Ok(new { Message = "new comment", Product = newProduct });
        }

        // Adds product data as a JSON object into an S3 bucket.
        // Expects AWS config (bucket name, region, credentials) to be configured for IAmazonS3 DI.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProductToS3(string ProductName, string ProductDescription, decimal ProductPrice)
        {
            // Basic input validation
            if (string.IsNullOrWhiteSpace(ProductName))
            {
                ModelState.AddModelError(nameof(ProductName), "Product name is required.");
                return BadRequest(ModelState);
            }

            var product = new
            {
                Id = Guid.NewGuid(),
                Name = ProductName,
                Description = ProductDescription,
                Price = ProductPrice,
                CreatedAt = DateTime.UtcNow
            };

            // Read bucket name from configuration; fallback to placeholder if not set.
            var bucketName = _config["AWS:BucketName"] ?? "your-bucket-name";

            // Create a key (path) for the object in the bucket.
            var key = $"products/{product.Id}.json";

            try
            {
                var json = JsonSerializer.Serialize(product);
                var contentBytes = Encoding.UTF8.GetBytes(json);

                using var memStream = new MemoryStream(contentBytes);

                var putRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    InputStream = memStream,
                    ContentType = "application/json"
                };

                var response = await _s3Client.PutObjectAsync(putRequest);

                // Build a public URL (works for objects with public read or presigned URL scenarios)
                var objectUrl = $"https://{bucketName}.s3.amazonaws.com/{key}";

                return Ok(new
                {
                    Message = "Product uploaded to S3 successfully.",
                    Product = product,
                    S3 = new
                    {
                        Bucket = bucketName,
                        Key = key,
                        HttpStatusCode = response.HttpStatusCode,
                        Url = objectUrl
                    }
                });
            }
            catch (AmazonS3Exception s3Ex)
            {
                // AWS-specific errors (e.g., access denied, bucket not found)
                return StatusCode((int)System.Net.HttpStatusCode.BadGateway, new
                {
                    Error = "S3 upload failed.",
                    Message = s3Ex.Message,
                    s3Ex.ErrorCode,
                    s3Ex.StatusCode
                });
            }
            catch (Exception ex)
            {
                // General errors
                return StatusCode(500, new
                {
                    Error = "An unexpected error occurred while uploading product to S3.",
                    Message = ex.Message
                });
            }
        }
    }
}
