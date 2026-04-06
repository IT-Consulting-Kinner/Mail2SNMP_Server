// Wave C/E: small JS helpers used from Blazor pages.
window.mail2snmp = window.mail2snmp || {};

// Wave E (17): Bootstrap 5.3 dark-mode support via data-bs-theme on <html>.
// The chosen theme is persisted in localStorage and re-applied immediately on
// every page load (avoids the "flash of light theme" on cold reload).
(function applyStoredTheme() {
    try {
        const stored = localStorage.getItem('mail2snmp-theme');
        if (stored === 'dark' || stored === 'light') {
            document.documentElement.setAttribute('data-bs-theme', stored);
        }
    } catch { /* localStorage may be blocked */ }
})();

window.mail2snmp.toggleTheme = function () {
    const html = document.documentElement;
    const current = html.getAttribute('data-bs-theme') || 'light';
    const next = current === 'dark' ? 'light' : 'dark';
    html.setAttribute('data-bs-theme', next);
    try { localStorage.setItem('mail2snmp-theme', next); } catch { /* ignored */ }
};

// Trigger a browser download for an in-memory text payload.
window.mail2snmp.downloadText = function (filename, mimeType, content) {
    const blob = new Blob([content], { type: mimeType || 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename || 'download.txt';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
