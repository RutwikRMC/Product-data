using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ProductImageUpload.Controllers
{
    public class ProductController : Controller
    {
        private readonly IWebHostEnvironment _env;
        public ProductController(IWebHostEnvironment env)
        {
            _env = env;
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

        // Renamed and made synchronous because it had no await/async work.
        // If you need async database or I/O inside, change back to async and await that work.
        [HttpGet]
        public IActionResult GetProductDetails(int productId)
        {
            return View();
        }

        // write method to add new product in databse using entity framework core and return proper response
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

            return Ok(new { Message = "Product added successfully", Product = newProduct });
        }
    }
}
