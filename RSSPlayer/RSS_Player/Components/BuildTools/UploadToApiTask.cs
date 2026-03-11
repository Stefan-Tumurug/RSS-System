#nullable enable

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace RSSPlayer.Components.BuildTools
{
    public class UploadToApiTask : Microsoft.Build.Utilities.Task
    {
        [Required]
        public string PublishDir { get; set; } = string.Empty;

        [Required]
        public string Version { get; set; } = string.Empty;

        public string ApiUrl { get; set; } = "https://remotescreenapi.diviso.dev/api/files/upload";

        public string PublishUrl { get; set; } = "https://remotescreenapi.diviso.dev/api/files/publish";
        public override bool Execute()
        {
            try
            {
                base.Log.LogMessage(MessageImportance.High, $"📦 Zipping publish folder: {PublishDir}");

                if (string.IsNullOrWhiteSpace(PublishDir) || !Directory.Exists(PublishDir))
                    throw new DirectoryNotFoundException($"PublishDir does not exist: {PublishDir}");

                string zipPath = Path.Combine(Path.GetTempPath(), "Remote Screen Player Release.zip");

                base.Log.LogMessage(MessageImportance.High, $"📁 Temp ZIP path: {zipPath}");

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(PublishDir, zipPath);
                base.Log.LogMessage(MessageImportance.High, $"✅ Created ZIP: {zipPath}");

                Task.Run(() => UploadZipAsync(zipPath)).Wait();

                string setupFilePath = Path.Combine(PublishDir, "setup.exe");
                if (File.Exists(setupFilePath))
                {
                    base.Log.LogMessage(MessageImportance.High, $"📤 Uploading setup.exe to {ApiUrl}");
                    Task.Run(() => UploadSetupAsync(setupFilePath, updateVersion: false)).Wait();
                }

                return true;
            }
            catch (System.Exception ex)
            {
                base.Log.LogErrorFromException(ex, true);
                return false;
            }
        }

        private async Task UploadSetupAsync(string setupFilePath, bool updateVersion = true)
        {
            using HttpClient client = new HttpClient();
            using MultipartFormDataContent content = new MultipartFormDataContent();
            using StreamContent fileContent = new StreamContent(File.OpenRead(setupFilePath));

            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-msdownload");
            content.Add(fileContent, "file", "setup.exe");

            string fullUrl = $"{ApiUrl}?version={Version}&force=true";
            if (!updateVersion)
                fullUrl += "&updateVersion=false";
            base.Log.LogMessage(MessageImportance.High, $"🚀 Uploading to {fullUrl}");

            try
            {
                HttpResponseMessage response = await client.PostAsync(fullUrl, content);
                string result = await response.Content.ReadAsStringAsync();

                base.Log.LogMessage(MessageImportance.High, $"🔍 Upload API Response: {response.StatusCode} - {result}");

                if (!response.IsSuccessStatusCode)
                    throw new System.Exception($"❌ Upload failed: {response.StatusCode} - {result}");

                base.Log.LogMessage(MessageImportance.High, $"✅ Setup upload success: {result}");
            }
            catch (System.Exception ex)
            {
                base.Log.LogErrorFromException(ex, true);
                throw;
            }
        }

        private async Task UploadZipAsync(string zipPath)
        {
            using (HttpClient client = new HttpClient())
            using (MultipartFormDataContent content = new MultipartFormDataContent())
            using (StreamContent fileContent = new StreamContent(File.OpenRead(zipPath)))
            {
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
                content.Add(fileContent, "file", "Remote Screen Player Release.zip");
                string fullUrl = $"{ApiUrl}?version={Version}&force=true";
                base.Log.LogMessage(MessageImportance.High, $"🚀 Uploading to {fullUrl}");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(fullUrl, content);
                    string result = await response.Content.ReadAsStringAsync();

                    base.Log.LogMessage(MessageImportance.High, $"🔍 Upload API Response: {response.StatusCode} - {result}");

                    if (!response.IsSuccessStatusCode)
                        throw new System.Exception($"❌ Upload failed: {response.StatusCode} - {result}");

                    base.Log.LogMessage(MessageImportance.High, $"✅ ZIP upload success: {result}");
                }
                catch (System.Exception ex)
                {
                    base.Log.LogErrorFromException(ex, true);
                    throw;
                }
            }

            await ExtractZipAsync(zipPath);

            await VerifyLatestVersionAsync(Version);
        }

        private async Task ExtractZipAsync(string zipPath)
        {
            using HttpClient client = new HttpClient();
            using MultipartFormDataContent content = new MultipartFormDataContent();
            using StreamContent fileContent = new StreamContent(File.OpenRead(zipPath));

            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
            content.Add(fileContent, "zip", Path.GetFileName(zipPath));

            string fullUrl = $"{PublishUrl}?version={Version}&force=true";
            base.Log.LogMessage(MessageImportance.High, $"🚀 Extracting ZIP to {fullUrl}");

            try
            {
                HttpResponseMessage response = await client.PostAsync(fullUrl, content);
                string result = await response.Content.ReadAsStringAsync();

                base.Log.LogMessage(MessageImportance.High, $"🔍 Publish API Response: {response.StatusCode} - {result}");

                if (!response.IsSuccessStatusCode)
                    throw new System.Exception($"❌ Publish failed: {response.StatusCode} - {result}");

                base.Log.LogMessage(MessageImportance.High, $"✅ Publish success: {result}");
            }
            catch (System.Exception ex)
            {
                base.Log.LogErrorFromException(ex, true);
                throw;
            }
        }

        private async Task VerifyLatestVersionAsync(string expectedVersion)
        {
            using HttpClient client = new HttpClient();
            string latestUrl = "https://remotescreenapi.diviso.dev/api/files/latest";
            HttpResponseMessage response = await client.GetAsync(latestUrl);
            string result = await response.Content.ReadAsStringAsync();
            base.Log.LogMessage(MessageImportance.High, $"✅ Latest version check: {result}");
        }
    }
}