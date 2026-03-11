// simpler version of swagger-auth.js
window.onload = function () {
    // Add auth header to Swagger requests
    function addAuthorization() {
        const token = localStorage.getItem('jwt_token');
        if (token && window.ui) {
            window.ui.preauthorizeApiKey("Bearer", `Bearer ${token}`);
            console.log("Added authorization token to Swagger UI");
        }
    }

    // Check every 100ms until Swagger UI is loaded
    const checkInterval = setInterval(function () {
        if (window.ui) {
            console.log("Swagger UI loaded, applying authorization");
            clearInterval(checkInterval);
            addAuthorization();

            // Add logout button
            const topbarElement = document.querySelector('.topbar');
            if (topbarElement) {
                const logoutButton = document.createElement('button');
                logoutButton.innerHTML = 'Logout';
                logoutButton.className = 'btn authorize';
                logoutButton.style.marginRight = '10px';
                logoutButton.onclick = function () {
                    // Remove token and redirect to login
                    localStorage.removeItem('jwt_token');
                    localStorage.removeItem('userRole');
                    document.cookie = "AuthToken=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
                    window.location.href = '/swagger-ui/login.html';
                };

                topbarElement.appendChild(logoutButton);
            }
        }
    }, 100);
};