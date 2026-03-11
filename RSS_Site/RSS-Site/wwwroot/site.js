window.applyTheme = function (theme) {
    console.log(` Applying theme: ${theme}`); 

    if (!theme || theme === 'system') {
        theme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    document.documentElement.setAttribute('data-theme', theme);

    try {
        let settings = localStorage.getItem('rss-site-settings-v2');
        settings = settings ? JSON.parse(settings) : {};
        settings.siteTheme = theme;
        localStorage.setItem('rss-site-settings-v2', JSON.stringify(settings));
    } catch (e) {
        console.error(" Error saving theme:", e);
    }

    console.log(` Theme applied: ${document.documentElement.getAttribute('data-theme')}`);
};

