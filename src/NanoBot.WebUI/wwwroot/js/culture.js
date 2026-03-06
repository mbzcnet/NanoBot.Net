// Culture detection and application script
// Reads the saved culture from cookie and applies it to the HTML element

(function () {
    /**
     * Get cookie value by name
     */
    function getCookie(name) {
        const match = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'));
        if (match) {
            return decodeURIComponent(match[2]);
        }
        return null;
    }

    /**
     * Get culture from localStorage (Blazored.LocalStorage uses this format)
     */
    function getLocalStorageCulture() {
        try {
            const stored = localStorage.getItem('nanobot_language');
            if (stored) {
                return stored;
            }
        } catch (e) {
            console.warn('Failed to read culture from localStorage:', e);
        }
        return 'auto';
    }

    /**
     * Get culture from cookie (for ASP.NET Core localization)
     */
    function getCookieCulture() {
        // Try to get the culture cookie set by ASP.NET Core
        const cookieCulture = getCookie('.AspNetCore.Culture');
        if (cookieCulture) {
            // Cookie format: "c=zh-CN|u=zh-CN" or "c=en-US"
            const parts = cookieCulture.split('|');
            for (let i = 0; i < parts.length; i++) {
                if (parts[i].startsWith('c=')) {
                    return parts[i].substring(2);
                }
            }
        }
        return null;
    }

    /**
     * Apply culture to HTML element
     */
    function applyCulture(culture) {
        const htmlElement = document.documentElement;
        if (htmlElement.lang !== culture) {
            htmlElement.lang = culture;
        }

        // Also set the lang attribute on any Blazor components that might render later
        document.setAttribute('data-culture', culture);
    }

    /**
     * Initialize culture on page load
     */
    function initializeCulture() {
        // Priority order:
        // 1. Cookie culture (set by server)
        // 2. LocalStorage culture (set by user in WebUI)
        // 3. Browser language
        // 4. Default: zh-CN

        let culture = getCookieCulture() || getLocalStorageCulture();

        if (culture === 'auto' || !culture) {
            // Use browser language
            const browserLang = navigator.language || navigator.userLanguage;
            culture = browserLang || 'zh-CN';

            // Normalize culture format (e.g., "zh-CN" instead of "zh")
            if (culture.length > 5) {
                culture = culture.substring(0, 5);
            }
        }

        applyCulture(culture);

        console.log('Culture initialized:', culture);
    }

    // Run on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeCulture);
    } else {
        initializeCulture();
    }

    // Expose function for Blazor to call when culture changes
    window.nanobot = window.nanobot || {};
    window.nanobot.setCulture = function (culture) {
        // Save to localStorage
        try {
            localStorage.setItem('nanobot_language', culture);
        } catch (e) {
            console.warn('Failed to save culture to localStorage:', e);
        }

        // Apply immediately
        applyCulture(culture);

        // Set cookie for server-side requests
        const expires = new Date();
        expires.setFullYear(expires.getFullYear() + 1);
        document.cookie = `.AspNetCore.Culture=c=${culture}|u=${culture}; expires=${expires.toUTCString()}; path=/; SameSite=Lax`;

        console.log('Culture changed to:', culture);
    };
})();
