namespace UFSWebApp.Infrastructure
{
    public class FileStorage
    {
        public const long MaxBytes = 5 * 1024 * 1024;
        private static readonly string[] Allowed = { ".jpg", ".jpeg", ".png", ".webp" };
        private readonly IWebHostEnvironment _env;
        public FileStorage(IWebHostEnvironment env) => _env = env;

        private static bool LooksLikeImage(byte[] head, string ext)
        {
            if (head.Length < 12) return false;
            if (ext is ".jpg" or ".jpeg") return head[0] == 0xFF && head[1] == 0xD8;
            if (ext == ".png") return head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47;
            if (ext == ".webp") return head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'E';
            return false;
        }

        /// <summary>Saves an uploaded photo under wwwroot/uploads/{folder}. Returns (path, error).</summary>
        public async Task<(string? path, string? error)> SaveImageAsync(IFormFile? file, string folder)
        {
            if (file == null || file.Length == 0) return (null, null);
            if (file.Length > MaxBytes) return (null, "The photo must be smaller than 5 MB.");
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!Allowed.Contains(ext)) return (null, "Only JPG, PNG or WEBP photos are allowed.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (!LooksLikeImage(bytes, ext)) return (null, "The uploaded file is not a valid image.");

            var dir = Path.Combine(_env.WebRootPath, "uploads", folder);
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid():N}{ext}";
            await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes);
            return ($"/uploads/{folder}/{name}", null);
        }
    }
}
