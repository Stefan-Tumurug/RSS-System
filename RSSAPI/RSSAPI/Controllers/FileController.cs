using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace RSSAPI.Controllers
{
    [Route("api/files")]
    [ApiController]
    public class FileController : ControllerBase
    {
        private readonly string uploadFolder;
        private readonly string versionFilePath;
        private readonly string downloadPagePath;
        private readonly IConfiguration configuration;
        private readonly ILogger<FileController> logger;
		private static readonly JsonSerializerOptions _jsonOptions = new()
        {
			PropertyNameCaseInsensitive = true
		};

		public FileController(IHostEnvironment env, IConfiguration configuration, ILogger<FileController> logger)
        {
            this.configuration = configuration;
            this.logger = logger;

            string downloadsFolderName = configuration["FileStorage:DownloadsFolder"] ?? "downloads";
            string versionFileName = configuration["FileStorage:VersionFileName"] ?? "version.json";
            string downloadPageName = configuration["FileStorage:DownloadPageName"] ?? "index.html";

            uploadFolder = Path.Combine(env.ContentRootPath,
                configuration["FileStorage:ContentRoot"] ?? "wwwroot",
                downloadsFolderName);

            versionFilePath = Path.Combine(uploadFolder, versionFileName);
            downloadPagePath = Path.Combine(uploadFolder, downloadPageName);

            EnsureUploadDirectoryExists();
        }

        private void EnsureUploadDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(uploadFolder))
                {
                    Directory.CreateDirectory(uploadFolder);
                    logger.LogInformation("Created upload directory: {UploadFolder}", uploadFolder);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create upload directory: {UploadFolder}", uploadFolder);
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string version, [FromQuery] bool force = false, [FromQuery] bool updateVersion = true)
        {
            logger.LogInformation("Upload started for file {FileName} with version {Version}", file?.FileName, version);

            if (file == null || file.Length == 0)
                return BadRequest(CreateErrorResponse("No file uploaded.", null));

            if (string.IsNullOrEmpty(version) || !Version.TryParse(version, out _))
                return BadRequest(CreateErrorResponse("Invalid version format. Use format like 1.0.0.0", null));

            try
            {
                if (!force && await HandleExistingVersion(version, file.FileName))
                {
                    return BadRequest(CreateErrorResponse($"An existing or newer version is already uploaded.", null));
                }

                string filePath = Path.Combine(uploadFolder, file.FileName);
                await SaveUploadedFile(file, filePath);

                string downloadUrl = GenerateDownloadUrl(file.FileName);

                if (updateVersion)
                {
                    await SaveVersionInfo(version, file.FileName, downloadUrl);
                }

                return Ok(CreateSuccessResponse("File uploaded successfully.", version, file.FileName, downloadUrl));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing upload: {Error}", ex.Message);
                return HandleUploadException(ex);
            }
        }
        [HttpPost("publish")]
        public async Task<IActionResult> UploadClickOncePublish(IFormFile zip, [FromQuery] string version)
        {
            if (zip == null || zip.Length == 0)
                return BadRequest(new { Success = false, Message = "No zip file provided." });

            string extractPath = Path.Combine(uploadFolder, "RemoteScreenPlayer");

            try
            {
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, recursive: true);

                Directory.CreateDirectory(extractPath);

                string tempZip = Path.GetTempFileName();
                using (FileStream stream = new(tempZip, FileMode.Create))
                    await zip.CopyToAsync(stream);

                ZipFile.ExtractToDirectory(tempZip, extractPath);

                logger.LogInformation("✅ Extracted ClickOnce publish to: {ExtractPath}", extractPath);
                return Ok(new { Success = true, Message = "ClickOnce publish extracted successfully.", Version = version });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error extracting ClickOnce publish");
                return StatusCode(500, new { Success = false, Message = "Extraction failed", Error = ex.Message });
            }
        }

        [HttpPost("publish-folder")]
        public async Task<IActionResult> UploadClickOnceFolder([FromQuery] string version)
        {
            if (!Request.HasFormContentType)
                return BadRequest(new { Success = false, Message = "Invalid request format. Expected multipart/form-data." });

            IFormCollection form = await Request.ReadFormAsync();
            IFormFileCollection files = form.Files;

            if (files.Count == 0)
                return BadRequest(new { Success = false, Message = "No files provided." });

            string extractPath = Path.Combine(uploadFolder, "RemoteScreenPlayer");

            try
            {
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, recursive: true);

                Directory.CreateDirectory(extractPath);

                foreach (IFormFile file in files)
                {
                    string relativePath = file.FileName.Replace('/', Path.DirectorySeparatorChar);
                    string targetPath = Path.Combine(extractPath, relativePath);

                    string? targetDirectory = Path.GetDirectoryName(targetPath);
                    if (targetDirectory != null && !Directory.Exists(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);
                    using FileStream fileStream = new(targetPath, FileMode.Create);
                    await file.CopyToAsync(fileStream);
                }

                logger.LogInformation("✅ Received folder upload with {FileCount} files to: {ExtractPath}", files.Count, extractPath);
                return Ok(new { Success = true, Message = $"Folder upload successful with {files.Count} files.", Version = version });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing folder upload");
                return StatusCode(500, new { Success = false, Message = "Folder upload failed", Error = ex.Message });
            }
        }

		private async Task<bool> HandleExistingVersion(string newVersion, string newFileName)
		{
			ArgumentNullException.ThrowIfNull(newFileName);

			if (!System.IO.File.Exists(versionFilePath))
				return false;

			try
			{
				string existingVersionJson = await System.IO.File.ReadAllTextAsync(versionFilePath);
				VersionInfo? existingVersionInfo = JsonSerializer.Deserialize<VersionInfo>(existingVersionJson, _jsonOptions);

				if (existingVersionInfo == null)
					return false;

				if (!IsOlderVersion(existingVersionInfo.Version, newVersion))
				{
					string oldFilePath = Path.Combine(uploadFolder, existingVersionInfo.FileName);
					if (System.IO.File.Exists(oldFilePath))
						System.IO.File.Delete(oldFilePath);

					return true;
				}

				return false;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error handling existing version");
				return true;
			}
		}

		private static async Task SaveUploadedFile(IFormFile file, string filePath)
		{
			using FileStream stream = new(filePath, FileMode.Create);
			await file.CopyToAsync(stream);
		}

		private async Task SaveVersionInfo(string version, string fileName, string downloadUrl)
		{
			VersionInfo versionInfo = new()
            {
				Version = version,
				FileName = fileName,
				DownloadUrl = downloadUrl
			};

			string json = JsonSerializer.Serialize(versionInfo);
			await System.IO.File.WriteAllTextAsync(versionFilePath, json);
		}


		[HttpGet("latest")]
        public IActionResult GetLatestFile()
        {
            try
            {
                if (!System.IO.File.Exists(versionFilePath))
                    return NotFound(CreateErrorResponse("No version info available.", null));

                string versionJson = System.IO.File.ReadAllText(versionFilePath);
				VersionInfo? VersionInfo = JsonSerializer.Deserialize<VersionInfo>(versionJson, _jsonOptions);


				if (VersionInfo != null)
                {
                    VersionInfo.FileName = "setup.exe";
                    VersionInfo.DownloadUrl = VersionInfo.DownloadUrl.Replace(VersionInfo.FileName, "setup.exe");
                }

                return VersionInfo == null
                    ? StatusCode(500, CreateErrorResponse("Invalid version information format.", null))
                    : Ok(CreateSuccessResponse(string.Empty, VersionInfo.Version, VersionInfo.FileName, VersionInfo.DownloadUrl));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving latest version info");
                return StatusCode(500, CreateErrorResponse("Error retrieving version information.", null));
            }
        }

        [HttpGet("download-page")]
        public IActionResult DownloadPage()
        {
            try
            {
                if (!System.IO.File.Exists(downloadPagePath))
                    return NotFound(CreateErrorResponse("Download page not found.", null));

                return PhysicalFile(downloadPagePath, "text/html");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error serving download page");
                return StatusCode(500, CreateErrorResponse("Error serving download page.", null));
            }
        }

        [HttpGet("download/{filename}")]
        public IActionResult DownloadFile(string filename)
        {
            try
            {
                string filePath = Path.Combine(uploadFolder, filename);

                if (!System.IO.File.Exists(filePath))
                    return NotFound(CreateErrorResponse("File not found.", null));

                return PhysicalFile(filePath, "application/octet-stream", filename);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error serving download file");
                return StatusCode(500, CreateErrorResponse("Error serving file.", null));
            }
        }

        private string GenerateDownloadUrl(string fileName)
        {
            try
            {
                string[]? baseUrls = configuration.GetSection("FileStorage:DownloadBaseUrl").Get<string[]>();
                bool isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

                string selectedBaseUrl = baseUrls != null && baseUrls.Length > 0
                    ? isDevelopment
                        ? baseUrls[0]
                        : baseUrls[^1]
                    : $"{Request.Scheme}://{Request.Host}/downloads";

                return $"{selectedBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(fileName)}";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Error generating download URL. Falling back to Request-based URL.");
                return $"{Request.Scheme}://{Request.Host}/downloads/{Uri.EscapeDataString(fileName)}";
            }
        }

        private static bool IsOlderVersion(string existingVersion, string newVersion)
        {
            bool existingParseSuccess = Version.TryParse(existingVersion, out Version? existingVer);
            bool newParseSuccess = Version.TryParse(newVersion, out Version? newVer);

            if (!existingParseSuccess || !newParseSuccess || existingVer == null || newVer == null)
                return false;

            return newVer > existingVer;
        }

        private static object CreateSuccessResponse(string message, string version, string fileName, string downloadUrl)
        {
            return new
            {
                Success = true,
                ErrorMessage = string.Empty,
                Item = new
                {
                    Message = message,
                    Version = version,
                    FileName = fileName,
                    DownloadUrl = downloadUrl
                }
            };
        }

        private static object CreateErrorResponse(string errorMessage, object? item)
        {
            return new
            {
                Success = false,
                ErrorMessage = errorMessage,
                Item = item
            };
        }

        private ObjectResult HandleUploadException(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => StatusCode(500, CreateErrorResponse("Permission denied accessing file path.", null)),
                IOException => StatusCode(500, CreateErrorResponse($"IO error occurred: {ex.Message}", null)),
                _ => StatusCode(500, CreateErrorResponse($"An error occurred while processing the file upload: {ex.Message}", null))
            };
        }

        private class VersionInfo
        {
            public required string Version { get; set; }
            public required string FileName { get; set; }
            public required string DownloadUrl { get; set; }
        }
    }
}