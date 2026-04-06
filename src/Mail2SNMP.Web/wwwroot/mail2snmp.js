// Wave C: small JS interop helpers used from Blazor pages.
window.mail2snmp = window.mail2snmp || {};

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
