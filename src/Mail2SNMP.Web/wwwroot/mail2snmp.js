// Wave C/E: small JS helpers used from Blazor pages.
window.mail2snmp = window.mail2snmp || {};

// UX-1: the theme-toggle helpers (applyStoredTheme/toggleTheme) were removed.
// They set the Bootstrap 5.3 data-bs-theme attribute, but the bundled stylesheet
// is v5.1.0 which does not implement data-bs-theme, so toggling had no visible
// effect. Restore them together with a Bootstrap 5.3+ upgrade or real dark CSS.

// G7: per-table configurable columns. Stores a JSON array of hidden column
// indices in localStorage under key 'mail2snmp-cols-<tableId>' and re-applies
// it on every render via window.mail2snmp.applyColumnVisibility(tableId).
window.mail2snmp.getHiddenColumns = function (tableId) {
    try {
        const raw = localStorage.getItem('mail2snmp-cols-' + tableId);
        return raw ? JSON.parse(raw) : [];
    } catch { return []; }
};
window.mail2snmp.setHiddenColumns = function (tableId, indices) {
    try { localStorage.setItem('mail2snmp-cols-' + tableId, JSON.stringify(indices)); } catch { }
    window.mail2snmp.applyColumnVisibility(tableId);
};
window.mail2snmp.applyColumnVisibility = function (tableId) {
    const table = document.getElementById(tableId);
    if (!table) return;
    const hidden = window.mail2snmp.getHiddenColumns(tableId);
    const set = new Set(hidden);
    table.querySelectorAll('thead tr').forEach(tr => {
        Array.from(tr.children).forEach((c, i) => c.style.display = set.has(i) ? 'none' : '');
    });
    table.querySelectorAll('tbody tr').forEach(tr => {
        Array.from(tr.children).forEach((c, i) => c.style.display = set.has(i) ? 'none' : '');
    });
};
window.mail2snmp.toggleColumn = function (tableId, idx) {
    const cur = window.mail2snmp.getHiddenColumns(tableId);
    const i = cur.indexOf(idx);
    if (i >= 0) cur.splice(i, 1); else cur.push(idx);
    window.mail2snmp.setHiddenColumns(tableId, cur);
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
