// Animate timeline bars sliding in on page load
document.addEventListener('DOMContentLoaded', () => {
    const fills = document.querySelectorAll('.tl-fill');
    fills.forEach(el => {
        const target = el.style.width;
        el.style.width = '0%';
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                el.style.width = target;
            });
        });
    });

    // Highlight current nav link
    const path = window.location.pathname.toLowerCase();
    document.querySelectorAll('.nav-links a').forEach(a => {
        if (path.includes(a.getAttribute('href')?.toLowerCase().split('/').pop() ?? '___')) {
            a.style.color = 'var(--text)';
            a.style.borderColor = 'var(--border2)';
            a.style.background = 'var(--bg3)';
        }
    });
});
