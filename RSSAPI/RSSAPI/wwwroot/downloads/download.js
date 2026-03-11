document.addEventListener('DOMContentLoaded', function () {
    document.getElementById('current-year').textContent = new Date().getFullYear();

    const loadingIndicator = document.getElementById('loading-indicator');
    const downloadContent = document.getElementById('download-content');
    const errorMessage = document.getElementById('error-message');
    const downloadLinkElement = document.getElementById("download-link");

    const zipFileUploadUrl = "/api/files/upload?version=0.1.1.24&force=true";
    const setupFileUploadUrl = "/api/files/upload?version=0.1.1.24&force=true&updateVersion=false";

    fetch("/api/files/latest")
        .then(response => {
            if (!response.ok) {
                throw new Error(`Server responded with status: ${response.status}`);
            }
            return response.json();
        })
        .then(data => {
            loadingIndicator.style.display = "none";

            if (!data.success || !data.item || !data.item.version) {
                throw new Error("No valid download information found in response");
            }

            let downloadUrl = "https://remotescreenapi.diviso.dev/api/files/download/setup.exe";

            if (window.location.hostname !== "remotescreenapi.diviso.dev") {
                downloadUrl = downloadUrl.replace("remotescreenapi.diviso.dev", window.location.host);
            }

            downloadLinkElement.href = downloadUrl;
            downloadLinkElement.setAttribute('title', 'Download setup.exe');

            document.getElementById("version-number").textContent = data.item.version;
            document.title = `Download Remote Screen Player v${data.item.version}`;

            downloadContent.style.display = "block";
        })
        .catch(error => {
            console.error("Error fetching latest version:", error);

            loadingIndicator.style.display = "none";
            errorMessage.style.display = "block";
            errorMessage.textContent = `Error: ${error.message || 'Could not fetch version info. Please try again later.'}`;
        });

    downloadLinkElement.addEventListener('click', function () {
        console.log('Download button clicked');
    });
});
