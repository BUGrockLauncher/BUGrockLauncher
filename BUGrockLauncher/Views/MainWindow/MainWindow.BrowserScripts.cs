namespace BUGrockLauncher {
    public partial class MainWindow {
        private const string ANTI_COOKIE_AND_POPUP_SCRIPT = @"
            (function() {
                // 1. Enable Native Dark Mode on MCPEDL using the site's own preferences and toggle
                function applyNativeSiteDarkMode() {
                    try {
                        localStorage.setItem('nuxt-color-mode', 'dark');
                        localStorage.setItem('color-mode', 'dark');
                        localStorage.setItem('theme', 'dark');
                        document.cookie = 'nuxt-color-mode=dark; path=/; max-age=31536000';
                        document.cookie = 'color-mode=dark; path=/; max-age=31536000';
                    } catch(e) {}

                    var root = document.documentElement;
                    // If MCPEDL is in light mode, activate its native toggle button (moon icon)
                    if (root && root.classList.contains('light-mode')) {
                        var moonEl = document.querySelector('button .fa-moon, button i[class*=""moon""], button svg[class*=""moon""], .theme-toggle, button[aria-label*=""theme"" i], button[aria-label*=""dark"" i]');
                        if (moonEl) {
                            var btn = moonEl.closest('button, a') || moonEl;
                            try { btn.click(); } catch(e) {}
                        }
                    }
                }

                if (window.location.hostname.indexOf('mcpedl.com') !== -1) {
                    applyNativeSiteDarkMode();
                }

                // 2. Bypass Adblock Detectors (Fake ad properties so CurseForge/MCPEDL never show disable adblock banners)
                try {
                    window.canRunAds = true;
                    window.isAdBlockActive = false;
                    window.adBlockerDetected = false;
                    window.google_ad_client = 'ca-pub-fake';
                    window.adsbygoogle = { push: function() {}, loaded: true };
                } catch(e) {}

                // 3. Inject CSS for Ads, Empty Sidebar & Cookie Banners ONLY (NO manual color or text overrides!)
                var style = document.createElement('style');
                style.innerHTML = `
                    #onetrust-consent-sdk, #onetrust-banner-sdk, .qc-cmp2-container, #cmpbox,
                    .cookie-banner, .cookie-notice, .cookieConsent, div[class*='cookie' i], div[id*='cookie' i],
                    div[class*='consent' i], div[id*='consent' i], .fc-consent-root,
                    div[class*='adblock' i], div[id*='adblock' i], div[class*='ad-block' i], div[id*='ad-block' i],
                    .modal-adblock, .adblock-overlay, #adblock-overlay {
                        display: none !important;
                        visibility: hidden !important;
                        pointer-events: none !important;
                        opacity: 0 !important;
                    }
                    /* Obliterate all ad banners, AdSense, video players and ad wrappers */
                    ins.adsbygoogle, .adsbygoogle, iframe[src*='googlesyndication'],
                    iframe[id*='google_ads'], div[id*='google_ads'], div[class*='ad-container'],
                    div[class*='ad-slot'], div[class*='advertisement'], div[class*='ad-banner'],
                    div[class*='ad-wrapper'], div[id*='ad-wrapper'],
                    div[id*='primis'], div[class*='primis'], div[id*='connatix'], div[class*='connatix'],
                    div[id*='playwire'], div[class*='playwire'], div[id*='anyclip'], div[class*='anyclip'],
                    div[id*='taboola'], div[class*='taboola'], div[id*='outbrain'], div[class*='outbrain'],
                    div[class*='sticky-player'], div[class*='floating-player'], div[id*='floating-player'],
                    div[class*='video-ad'], div[id*='video-ad'], div[class*='jwplayer'], div[id*='jwplayer'],
                    div[class*='vdo'], div[id*='vdo'], lite-youtube, .lite-youtube,
                    iframe[src*='youtube'], iframe[src*='youtu.be'],
                    div[data-ad], div[data-ad-unit], div[id*='gpt-ad'] {
                        display: none !important;
                        visibility: hidden !important;
                        pointer-events: none !important;
                        opacity: 0 !important;
                        height: 0 !important;
                        width: 0 !important;
                        min-height: 0 !important;
                        margin: 0 !important;
                        padding: 0 !important;
                    }
                    /* Remove MCPEDL empty right sidebar and expand main content full-width */
                    aside, .sidebar, .right-sidebar, .layout__sidebar, .post__sidebar,
                    div[class*='sidebar'], div[id*='sidebar'] {
                        display: none !important;
                        visibility: hidden !important;
                        width: 0 !important;
                        max-width: 0 !important;
                        min-width: 0 !important;
                        flex: 0 0 0% !important;
                        margin: 0 !important;
                        padding: 0 !important;
                        pointer-events: none !important;
                    }
                    .layout__main, .layout-main, .content__main, .content-main,
                    main, .main-content, .posts-list, .posts, .app-layout__content,
                    div[class*='layout__main'], div[class*='content__main'] {
                        max-width: 100% !important;
                        width: 100% !important;
                        flex: 1 1 100% !important;
                        margin-right: 0 !important;
                        padding-right: 0 !important;
                    }
                    /* NitroPay, Freestar, AdThrive, and in-article ad slots */
                    div[id*='nitropay'], div[class*='nitropay'], iframe[src*='nitropay'],
                    div[id*='np-'], div[class*='np-'], [id*='mcpedl_post'], [id*='article__ad'],
                    [class*='article__ad'], [class*='post__ad'], [id*='post__ad'],
                    div[class*='ad-container'], div[class*='ad-slot'], div[class*='ad-wrapper'],
                    div[class*='ad-banner'], div[id*='ad-banner'], div[class*='advertisement'],
                    div[id*='advertisement'], div[data-ad-width], div[data-ad-height] {
                        display: none !important;
                        height: 0 !important;
                        width: 0 !important;
                        min-height: 0 !important;
                        margin: 0 !important;
                        padding: 0 !important;
                        pointer-events: none !important;
                        opacity: 0 !important;
                        visibility: hidden !important;
                    }
                    /* Prominent Skip to Downloads button in place of the ad banner */
                    a[href*='#download'], .skip-to-downloads, a.skip-to-downloads {
                        display: flex !important;
                        align-items: center !important;
                        justify-content: center !important;
                        width: 100% !important;
                        box-sizing: border-box !important;
                        background-color: #1F2937 !important;
                        background: #1F2937 !important;
                        color: #10B981 !important;
                        border: 1px solid #374151 !important;
                        border-radius: 8px !important;
                        padding: 12px 20px !important;
                        margin: 12px 0 16px 0 !important;
                        font-size: 15px !important;
                        font-weight: 700 !important;
                        text-decoration: none !important;
                        text-align: center !important;
                        cursor: pointer !important;
                        box-shadow: 0 2px 4px rgba(0,0,0,0.2) !important;
                    }
                    a[href*='#download']:hover, .skip-to-downloads:hover, a.skip-to-downloads:hover {
                        background-color: #283344 !important;
                        border-color: #10B981 !important;
                        color: #34D399 !important;
                    }
                `;
                if (document.head) document.head.appendChild(style);

                // 4. Auto-dismiss any detected consent/adblock popups, ads, floating videos, and sidebars
                function dismissOverlays() {
                    var selectors = [
                        '#onetrust-reject-all-handler',
                        'button#didomi-notice-disagree-button',
                        'button[aria-label=""Reject all""]',
                        'button[aria-label=""Decline all""]',
                        '.qc-cmp2-buttons-desktop button:last-child'
                    ];
                    for (var k = 0; k < selectors.length; k++) {
                        var btn = document.querySelector(selectors[k]);
                        if (btn && typeof btn.click === 'function') {
                            try { btn.click(); } catch(e) {}
                        }
                    }

                    // Purge ads, videos, sidebars, and NitroPay units
                    var bads = document.querySelectorAll(`
                        ins.adsbygoogle, .adsbygoogle, iframe[src*='googlesyndication'],
                        div[id*='google_ads'], div[class*='ad-container'], div[class*='ad-slot'],
                        div[id*='primis'], div[class*='primis'], div[id*='connatix'], div[class*='connatix'],
                        div[id*='playwire'], div[class*='playwire'], div[id*='anyclip'],
                        div[id*='nitropay'], div[class*='nitropay'], iframe[src*='nitropay'],
                        div[id*='np-'], div[class*='np-'], [id*='mcpedl_post'], [id*='article__ad'],
                        [class*='article__ad'], [class*='post__ad'], [id*='post__ad'],
                        aside, .sidebar, .layout__sidebar, .post__sidebar, div[class*='sidebar'],
                        div[class*='floating-player'], div[class*='sticky-player'], div[id*='floating-player'],
                        div[class*='video-ad'], div[id*='video-ad'], div[class*='jwplayer'],
                        lite-youtube, .lite-youtube, iframe[src*='youtube']
                    `);
                    for (var b = 0; b < bads.length; b++) {
                        bads[b].remove();
                    }

                    // Format Skip to Downloads button and obliterate any ad between it and Introduction
                    var allLinks = document.querySelectorAll('a');
                    for (var i = 0; i < allLinks.length; i++) {
                        var aEl = allLinks[i];
                        var aTxt = (aEl.textContent || '').trim().toLowerCase();
                        var aHref = aEl.getAttribute('href') || '';
                        if (aTxt.indexOf('skip to download') !== -1 || (aHref.indexOf('#download') !== -1 && aTxt.indexOf('download') !== -1)) {
                            if (aEl.textContent.indexOf('NOT AD :)') === -1) {
                                aEl.textContent = 'Skip to Downloads - NOT AD :) >';
                            }
                            aEl.style.setProperty('display', 'flex', 'important');
                            aEl.style.setProperty('align-items', 'center', 'important');
                            aEl.style.setProperty('justify-content', 'center', 'important');
                            aEl.style.setProperty('width', '100%', 'important');
                            aEl.style.setProperty('margin', '12px 0 16px 0', 'important');
                            aEl.style.setProperty('padding', '12px 20px', 'important');
                            aEl.style.setProperty('background-color', '#1F2937', 'important');
                            aEl.style.setProperty('background', '#1F2937', 'important');
                            aEl.style.setProperty('color', '#10B981', 'important');
                            aEl.style.setProperty('border', '1px solid #374151', 'important');
                            aEl.style.setProperty('border-radius', '8px', 'important');
                            aEl.style.setProperty('font-size', '15px', 'important');
                            aEl.style.setProperty('font-weight', '700', 'important');
                            aEl.style.setProperty('text-decoration', 'none', 'important');
                            aEl.style.setProperty('box-sizing', 'border-box', 'important');

                            // Remove any sibling element between skip link and INTRODUCTION / DESCRIPTION
                            var currEl = aEl.parentElement ? aEl.parentElement : aEl;
                            var nextEl = currEl.nextElementSibling;
                            while (nextEl) {
                                var nextText = (nextEl.textContent || '').trim().toUpperCase();
                                if (nextText.indexOf('INTRODUCTION') !== -1 || nextText.indexOf('DESCRIPTION') !== -1 || nextEl.querySelector('h2, h3')) {
                                    break;
                                }
                                var toKill = nextEl;
                                nextEl = nextEl.nextElementSibling;
                                toKill.remove();
                            }
                            break;
                        }
                    }

                    // Kill any floating video container docked at bottom-right
                    var fixedEls = document.querySelectorAll('div, iframe');
                    for (var j = 0; j < fixedEls.length; j++) {
                        var el = fixedEls[j];
                        var pos = window.getComputedStyle(el).position;
                        if (pos === 'fixed') {
                            var r = window.getComputedStyle(el).right;
                            var bot = window.getComputedStyle(el).bottom;
                            if (r !== 'auto' && bot !== 'auto' && r !== '0px') {
                                var inner = el.innerHTML || '';
                                if (inner.indexOf('Terms of Use') !== -1 || inner.indexOf('Privacy') !== -1 || inner.indexOf('video') !== -1 || inner.indexOf('player') !== -1) {
                                    el.remove();
                                }
                            }
                        }
                    }

                    if (window.location.hostname.indexOf('mcpedl.com') !== -1) {
                        applyNativeSiteDarkMode();
                    }
                }

                if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', dismissOverlays);
                } else {
                    dismissOverlays();
                }
                setTimeout(dismissOverlays, 300);
                setTimeout(dismissOverlays, 800);
                setTimeout(dismissOverlays, 2000);

                if (window.MutationObserver) {
                    try {
                        var observer = new MutationObserver(function() {
                            dismissOverlays();
                        });
                        if (document.body) {
                            observer.observe(document.body, { childList: true, subtree: true });
                        }
                    } catch(e) {}
                }

                // Notify WPF launcher when mouse is inside webview document
                try {
                    window.addEventListener('mouseenter', function() {
                        try { window.chrome.webview.postMessage('browser_mouseenter'); } catch(e) {}
                    });
                    window.addEventListener('mousemove', function() {
                        try { window.chrome.webview.postMessage('browser_mouseenter'); } catch(e) {}
                    }, { passive: true, once: true });
                } catch(e) {}
            })();
        ";

        private const string SERVERS_VIEW_POLISH_SCRIPT = @"
            (function() {
                // Neutralize Adblock, Admiral, CMP and alert dialogs
                try {
                    window.alert = function() {};
                    window.confirm = function() { return true; };
                    window.prompt = function() { return null; };
                    window.canRunAds = true;
                    window.isAdBlockActive = false;
                    window.adBlockerDetected = false;
                    window.google_ad_client = 'ca-pub-fake';
                    window.adsbygoogle = { push: function() {}, loaded: true };
                    window.admiral = function() {};
                    window.admiral.q = [];
                    window.admiral.v = 2;
                } catch(e) {}

                var style = document.createElement('style');
                style.innerHTML = `
                    nav.navbar, header, footer, #footer, .footer, .card.bg-light, .alert, .adsbygoogle,
                    lite-youtube, .lite-youtube, iframe[src*='youtube'], iframe[src*='youtu.be'],
                    a[href*='youtube.com'], a[href*='youtu.be'], i.fa-youtube-play, .fa-youtube-play,
                    section, article {
                        display: none !important;
                    }
                    /* Eradicate any modals, popups, cookie notices, overlays, Admiral adblock prompts */
                    .modal, .modal-backdrop, .modal-open, .fade.show,
                    div[role='dialog'], div[aria-modal='true'],
                    .fc-dialog-container, .fc-consent-root, .fc-ab-root, .fc-dialog,
                    div[class*='admiral' i], div[id*='admiral' i], iframe[id*='admiral' i],
                    #onetrust-consent-sdk, #onetrust-banner-sdk, .qc-cmp2-container, #cmpbox,
                    .cookie-banner, .cookie-notice, .cookieConsent, div[class*='cookie' i], div[id*='cookie' i],
                    div[class*='consent' i], div[id*='consent' i],
                    div[class*='adblock' i], div[id*='adblock' i], div[class*='ad-block' i], div[id*='ad-block' i],
                    .modal-adblock, .adblock-overlay, #adblock-overlay,
                    .sweet-alert, .swal2-container, .popover, .tooltip {
                        display: none !important;
                        visibility: hidden !important;
                        pointer-events: none !important;
                        opacity: 0 !important;
                    }
                    html, body, .content, .container, .container-fluid, .row, .col, div[class*='col'] {
                        background-color: #12131A !important;
                        background: #12131A !important;
                        color: #E2E8F0 !important;
                        margin: 0 !important;
                        padding-left: 2px !important;
                        padding-right: 2px !important;
                        padding-top: 0 !important;
                        margin-top: 0 !important;
                        max-width: 100% !important;
                        overflow-x: hidden !important;
                    }
                    h1, h2, .server-title-responsive {
                        margin-top: 0 !important;
                        padding-top: 0 !important;
                        margin-bottom: 6px !important;
                    }
                    nav.navbar, header, .breadcrumb, ol.breadcrumb, ul.breadcrumb,
                    .leaderboard, #leaderboard, div[id*='leaderboard'],
                    div[class*='ad-slot'], div[id*='ad-slot'], div[class*='ad-container'],
                    .adsbygoogle, .alert, .my-4, .mt-4 {
                        display: none !important;
                        height: 0 !important;
                        margin: 0 !important;
                        padding: 0 !important;
                    }
                    /* Server Banners and Containers */
                    .server-banner-wrap, .server-banner, div[class*='server-banner'] {
                        max-width: 100% !important;
                        height: auto !important;
                    }
                    /* Cards & Server Containers */
                    .card, .card-header, .card-body, .card-footer, .list-group, .list-group-item,
                    .bg-white, .bg-light, div[class*='server'], div[class*='box'], div[class*='item'],
                    .server-card, .server-banner-wrap {
                        background-color: #151720 !important;
                        background: #151720 !important;
                        color: #E2E8F0 !important;
                        border-color: #232530 !important;
                    }
                    /* Server IP boxes & buttons (bedrock.bosscraft.net:19132) */
                    .btn-server-ip, .btn-server-ip[href], a.btn-server-ip, button.btn-server-ip,
                    .clipboard, .btn-group, .btn-group > .btn,
                    input, input.form-control, .form-control, .input-group, .input-group-text,
                    .input-group-append, .input-group-prepend, code, pre, .copy-ip,
                    .btn-outline-secondary, .btn-secondary {
                        background-color: #1A1C28 !important;
                        background: #1A1C28 !important;
                        color: #48BB78 !important;
                        border: 1px solid #2D3748 !important;
                        border-color: #2D3748 !important;
                        box-shadow: none !important;
                    }
                    .btn-server-ip:hover, .btn-server-ip:focus, .btn-server-ip:active,
                    a.btn-server-ip:hover, button.btn-server-ip:hover, .btn-server-ip[href]:hover {
                        background-color: #222638 !important;
                        background: #222638 !important;
                        color: #68D391 !important;
                        border-color: #4A5568 !important;
                    }
                    /* Server Name & Titles & Headings */
                    .server-title, .server-title a,
                    .server-title-responsive, .server-title-responsive a,
                    .server-about-m h3, .server-about-m h3 a,
                    .server-about, .server-about h3,
                    a.text-dark, .text-dark,
                    h1, h2, h3, h4, h5, h6,
                    .server-card a, .server-about a {
                        color: #F7FAFC !important;
                        background-color: transparent !important;
                        background: transparent !important;
                        font-weight: 700 !important;
                    }
                    .server-title a:hover,
                    .server-title-responsive a:hover,
                    .server-about-m h3 a:hover,
                    a.text-dark:hover {
                        color: #48BB78 !important;
                        text-decoration: underline !important;
                    }
                    /* Tables */
                    .table, .servers-table, table, thead, tbody, tr {
                        background-color: #151720 !important;
                        background: #151720 !important;
                        color: #E2E8F0 !important;
                        border-color: #232530 !important;
                        margin-top: 0 !important;
                        width: 100% !important;
                    }
                    .table td, .table th, table td, table th,
                    .table-bordered td, .table-bordered th {
                        background-color: #151720 !important;
                        background: #151720 !important;
                        color: #E2E8F0 !important;
                        border-color: #232530 !important;
                        padding: 4px 6px !important;
                    }
                    .table-striped tbody tr:nth-of-type(odd),
                    .table-striped tbody tr:nth-of-type(odd) td {
                        background-color: #191B26 !important;
                        background: #191B26 !important;
                    }
                    /* Secondary Buttons like online, stars, player count, versions */
                    .btn-light2, .btn-light2[href], .btn-version-fade {
                        background-color: #1A1C28 !important;
                        background: #1A1C28 !important;
                        color: #CBD5E0 !important;
                        border: 1px solid #2D3748 !important;
                        border-color: #2D3748 !important;
                    }
                    .btn-light2:hover, .btn-light2:focus, .btn-light2:active {
                        background-color: #222638 !important;
                        background: #222638 !important;
                        color: #FFFFFF !important;
                        border-color: #4A5568 !important;
                    }
                    /* PAGINATION CONTROLS */
                    .float-right, div.float-right {
                        float: none !important;
                        display: flex !important;
                        justify-content: center !important;
                        width: 100% !important;
                    }
                    nav[aria-label*='navigation'],
                    nav[aria-label*='Page'],
                    nav[aria-label*='page'],
                    nav {
                        display: flex !important;
                        justify-content: center !important;
                        width: 100% !important;
                        margin: 12px 0 !important;
                        visibility: visible !important;
                        opacity: 1 !important;
                    }
                    .pagination, ul.pagination {
                        display: flex !important;
                        flex-wrap: wrap !important;
                        justify-content: center !important;
                        align-items: center !important;
                        list-style: none !important;
                        padding: 0 !important;
                        margin: 8px auto !important;
                        gap: 4px !important;
                        width: 100% !important;
                        max-width: 100% !important;
                        visibility: visible !important;
                        opacity: 1 !important;
                    }
                    .pagination li,
                    .pagination .page-item {
                        display: inline-flex !important;
                        margin: 2px !important;
                        visibility: visible !important;
                        opacity: 1 !important;
                    }
                    .pagination .page-link,
                    .page-link,
                    a.page-link {
                        display: inline-flex !important;
                        align-items: center !important;
                        justify-content: center !important;
                        min-width: 32px !important;
                        height: 32px !important;
                        padding: 2px 8px !important;
                        background-color: #1A1C28 !important;
                        background: #1A1C28 !important;
                        color: #E2E8F0 !important;
                        border: 1px solid #2D3748 !important;
                        border-radius: 4px !important;
                        font-size: 13px !important;
                        font-weight: 600 !important;
                        text-decoration: none !important;
                        visibility: visible !important;
                        opacity: 1 !important;
                    }
                    .pagination .page-link:hover,
                    .page-link:hover,
                    a.page-link:hover {
                        background-color: #2D3748 !important;
                        color: #48BB78 !important;
                        border-color: #48BB78 !important;
                    }
                    .pagination li.active .page-link,
                    .pagination .page-item.active .page-link,
                    .pagination .active .page-link {
                        background-color: #38A169 !important;
                        background: #38A169 !important;
                        border-color: #38A169 !important;
                        color: #FFFFFF !important;
                        font-weight: bold !important;
                    }
                    .pagination li.disabled .page-link,
                    .pagination .page-item.disabled .page-link {
                        background-color: #12131A !important;
                        color: #718096 !important;
                        border-color: #232530 !important;
                        cursor: not-allowed !important;
                    }
                    .server-description, .server-description-responsive, .text-muted, small, p {
                        color: #A0AEC0 !important;
                    }
                    a {
                        color: #48BB78 !important;
                    }
                `;
                if (document.head) document.head.appendChild(style);

                function killPopupsAndModals() {
                    var badEls = document.querySelectorAll('.modal, .modal-backdrop, div[role=""dialog""], div[aria-modal=""true""], .fc-dialog-container, .fc-consent-root, .fc-ab-root, div[class*=""admiral"" i], div[id*=""admiral"" i], iframe[id*=""admiral"" i], div[class*=""cookie"" i], div[class*=""consent"" i]');
                    for (var i = 0; i < badEls.length; i++) {
                        var el = badEls[i];
                        if (el.id && el.id.toLowerCase().indexOf('connect') !== -1) {
                            el.style.setProperty('display', 'none', 'important');
                            continue;
                        }
                        if (el.querySelector('input')) {
                            el.style.setProperty('display', 'none', 'important');
                            continue;
                        }
                        el.remove();
                    }
                    if (document.body) {
                        document.body.classList.remove('modal-open');
                        document.body.style.removeProperty('overflow');
                        document.body.style.removeProperty('padding-right');
                    }
                }

                function applyDarkStylesDirectly() {
                    killPopupsAndModals();

                    var ipEls = document.querySelectorAll('.btn-server-ip, a.btn-server-ip, button.btn-server-ip, .clipboard, .btn-group, input, .form-control');
                    for (var i = 0; i < ipEls.length; i++) {
                        ipEls[i].style.setProperty('background-color', '#1A1C28', 'important');
                        ipEls[i].style.setProperty('background', '#1A1C28', 'important');
                        ipEls[i].style.setProperty('color', '#48BB78', 'important');
                        ipEls[i].style.setProperty('border-color', '#2D3748', 'important');
                    }

                    var titleEls = document.querySelectorAll('.server-title-responsive a, .server-about-m h3 a, .server-title a, a.text-dark, h3 a, .server-about a');
                    for (var j = 0; j < titleEls.length; j++) {
                        titleEls[j].style.setProperty('color', '#F7FAFC', 'important');
                        titleEls[j].style.setProperty('background-color', 'transparent', 'important');
                        titleEls[j].style.setProperty('background', 'transparent', 'important');
                    }

                    var lightBtnEls = document.querySelectorAll('.btn-light2, .btn-version-fade');
                    for (var k = 0; k < lightBtnEls.length; k++) {
                        lightBtnEls[k].style.setProperty('background-color', '#1A1C28', 'important');
                        lightBtnEls[k].style.setProperty('background', '#1A1C28', 'important');
                        lightBtnEls[k].style.setProperty('color', '#CBD5E0', 'important');
                        lightBtnEls[k].style.setProperty('border-color', '#2D3748', 'important');
                    }

                    var bgEls = document.querySelectorAll('.content, .card, .card-body, .server-card, .table, td, th, tr, .bg-white, .bg-light');
                    for (var m = 0; m < bgEls.length; m++) {
                        bgEls[m].style.setProperty('background-color', '#151720', 'important');
                        bgEls[m].style.setProperty('background', '#151720', 'important');
                        bgEls[m].style.setProperty('border-color', '#232530', 'important');
                    }
                }

                function cleanBottomContent() {
                    killPopupsAndModals();

                    // 1. Remove all YouTube videos, wrappers, and trailer links
                    document.querySelectorAll('lite-youtube, .lite-youtube, iframe[src*=""youtube""], iframe[src*=""youtu.be""], .fa-youtube-play').forEach(function(el) {
                        var container = el.closest('.row, .col-md-4, .form-group, h2');
                        if (container && container !== document.body && container !== document.documentElement) {
                            container.remove();
                        } else {
                            el.remove();
                        }
                    });

                    document.querySelectorAll('h2, h3').forEach(function(h) {
                        var txt = (h.textContent || '');
                        if (txt.indexOf('Gameplay Videos') !== -1 || txt.indexOf('YouTube') !== -1 || txt.indexOf('Videos') !== -1) {
                            h.remove();
                        }
                    });

                    // 2. Format and center pagination
                    document.querySelectorAll('.pagination, ul.pagination').forEach(function(pag) {
                        pag.style.setProperty('display', 'flex', 'important');
                        pag.style.setProperty('flex-wrap', 'wrap', 'important');
                        pag.style.setProperty('justify-content', 'center', 'important');
                        pag.style.setProperty('visibility', 'visible', 'important');
                        pag.style.setProperty('opacity', '1', 'important');

                        var floatDiv = pag.closest('.float-right');
                        if (floatDiv) {
                            floatDiv.classList.remove('float-right');
                            floatDiv.style.setProperty('float', 'none', 'important');
                            floatDiv.style.setProperty('display', 'flex', 'important');
                            floatDiv.style.setProperty('justify-content', 'center', 'important');
                            floatDiv.style.setProperty('width', '100%', 'important');
                        }
                    });

                    // 3. Remove all footer / articles / content after the bottom pagination
                    var allPag = document.querySelectorAll('.pagination, ul.pagination');
                    if (allPag.length > 0) {
                        var lastPag = allPag[allPag.length - 1];
                        var lastRow = lastPag.closest('.row');
                        if (lastRow && lastRow.parentElement) {
                            var nextSibling = lastRow.nextElementSibling;
                            while (nextSibling) {
                                var toRemove = nextSibling;
                                nextSibling = nextSibling.nextElementSibling;
                                toRemove.remove();
                            }
                        }
                    }

                    document.querySelectorAll('footer, #footer, .footer, section, article').forEach(function(el) {
                        el.remove();
                    });

                    applyDarkStylesDirectly();
                }

                function removeTopSpacing() {
                    if (document.body) {
                        document.body.style.setProperty('padding-top', '0px', 'important');
                        document.body.style.setProperty('margin-top', '0px', 'important');
                    }
                    if (document.documentElement) {
                        document.documentElement.style.setProperty('padding-top', '0px', 'important');
                        document.documentElement.style.setProperty('margin-top', '0px', 'important');
                    }
                    var h1 = document.querySelector('h1');
                    if (h1 && h1.parentElement && window.location.pathname.indexOf('/server/') === -1) {
                        h1.style.setProperty('margin-top', '0px', 'important');
                        h1.style.setProperty('padding-top', '0px', 'important');
                        var prev = h1.previousElementSibling;
                        while (prev) {
                            var toRemove = prev;
                            prev = prev.previousElementSibling;
                            if (toRemove.querySelector('.server-banner-wrap, .server-banner, img')) break;
                            var tag = toRemove.tagName.toLowerCase();
                            if (tag === 'nav' || tag === 'header' || tag === 'ol' || !toRemove.textContent.trim()) {
                                toRemove.remove();
                            }
                        }
                    }
                }

                var hasScrolledInitially = false;
                window.addEventListener('wheel', function() { hasScrolledInitially = true; }, { passive: true });
                window.addEventListener('touchmove', function() { hasScrolledInitially = true; }, { passive: true });
                window.addEventListener('keydown', function(e) {
                    if (e.key === 'ArrowDown' || e.key === 'PageDown' || e.key === ' ') hasScrolledInitially = true;
                }, { passive: true });

                function jumpToFirstServer() {
                    if (hasScrolledInitially) return;
                    hasScrolledInitially = true;

                    if (window.location.pathname.indexOf('/server/') !== -1) {
                        window.scrollTo(0, 0);
                        if (document.documentElement) document.documentElement.scrollTop = 0;
                        if (document.body) document.body.scrollTop = 0;
                        return;
                    }
                    var serverTable = document.querySelector('.servers-table');
                    if (serverTable) {
                        serverTable.scrollIntoView({ behavior: 'instant', block: 'start' });
                    }
                }

                function getServerConnectionInfo() {
                    var name = '';
                    var addr = '';
                    var port = '19132';

                    // 1. Try server name from heading or title
                    var titleEl = document.querySelector('h1, .server-title, .server-title-responsive, .server-name');
                    if (titleEl && titleEl.textContent) {
                        name = titleEl.textContent.trim().split('|')[0].split('-')[0].trim();
                    }
                    if (!name) {
                        var docTitle = document.title || '';
                        if (docTitle) name = docTitle.split('|')[0].split('-')[0].trim();
                    }
                    if (!name) name = 'Minecraft Server';
                    name = name.replace(/\|/g, ' - ').trim();

                    // 2. Try #connectModal inputs
                    var connectModal = document.querySelector('#connectModal, div[id*=""connect""]');
                    if (connectModal) {
                        var inputs = connectModal.querySelectorAll('input');
                        for (var i = 0; i < inputs.length; i++) {
                            var val = (inputs[i].value || '').trim();
                            if (!val) continue;
                            if (/^\d{3,5}$/.test(val)) {
                                port = val;
                            } else if (val.indexOf('.') !== -1 || val.indexOf(':') !== -1) {
                                if (val.indexOf(':') !== -1) {
                                    var parts = val.split(':');
                                    addr = parts[0].trim();
                                    port = parts[1].trim();
                                } else {
                                    addr = val;
                                }
                            }
                        }
                    }

                    // 3. Try data-clipboard-text or server IP elements
                    if (!addr) {
                        var clipEls = document.querySelectorAll('[data-clipboard-text], .btn-server-ip, button.btn-server-ip, a.btn-server-ip, .clipboard');
                        for (var j = 0; j < clipEls.length; j++) {
                            var clipVal = clipEls[j].getAttribute('data-clipboard-text') || clipEls[j].textContent || '';
                            clipVal = clipVal.trim().replace(/^ip\s*:\s*/i, '');
                            if (clipVal.indexOf('.') !== -1) {
                                var p = clipVal.split(':');
                                addr = p[0].trim();
                                if (p.length > 1 && /^\d+$/.test(p[1].trim())) {
                                    port = p[1].trim();
                                }
                                break;
                            }
                        }
                    }

                    // 4. Try table rows or detail cards
                    if (!addr) {
                        var rows = document.querySelectorAll('tr, li, .list-group-item');
                        for (var k = 0; k < rows.length; k++) {
                            var rowTxt = (rows[k].textContent || '').toLowerCase();
                            if ((rowTxt.indexOf('ip') !== -1 || rowTxt.indexOf('address') !== -1 || rowTxt.indexOf('host') !== -1) && !addr) {
                                var cells = rows[k].querySelectorAll('td, span, strong, code');
                                for (var c = 0; c < cells.length; c++) {
                                    var cellTxt = cells[c].textContent.trim();
                                    if (cellTxt.indexOf('.') !== -1 && cellTxt.indexOf(' ') === -1) {
                                        addr = cellTxt;
                                        break;
                                    }
                                }
                            }
                            if (rowTxt.indexOf('port') !== -1 && port === '19132') {
                                var cells2 = rows[k].querySelectorAll('td, span, strong, code');
                                for (var c2 = 0; c2 < cells2.length; c2++) {
                                    var pCandidate = cells2[c2].textContent.trim();
                                    if (/^\d{3,5}$/.test(pCandidate)) {
                                        port = pCandidate;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // 5. Fallback regex on page body
                    if (!addr && document.body) {
                        var bodyMatch = document.body.innerHTML.match(/([a-zA-Z0-9\-\.]+\.(?:net|com|org|io|gg|me|fun|xyz|club|online|co|us|uk|de|ru|fr)):(\d{4,5})/i);
                        if (bodyMatch) {
                            addr = bodyMatch[1];
                            port = bodyMatch[2];
                        }
                    }

                    return { name: name, addr: addr, port: port };
                }

                // Intercept 'Connect' button clicks to launch Minecraft Bedrock and add the server
                document.addEventListener('click', function(e) {
                    var targetEl = e.target;
                    var btn = targetEl ? targetEl.closest('button, a, .btn, [data-target], [data-toggle]') : null;
                    if (!btn) return;

                    var txt = (btn.textContent || '').trim().toLowerCase();
                    var dataTarget = (btn.getAttribute('data-target') || '').toLowerCase();
                    var href = (btn.getAttribute('href') || '').toLowerCase();
                    var btnId = (btn.id || '').toLowerCase();

                    var isConnectBtn = (dataTarget.indexOf('connect') !== -1) ||
                                       (btnId.indexOf('connect') !== -1) ||
                                       (txt === 'connect' || txt.indexOf('connect ') === 0 || txt.indexOf(' connect') !== -1 || txt.indexOf('connect\n') !== -1) ||
                                       (href.indexOf('connect') !== -1 || href.indexOf('minecraft:') !== -1);

                    if (isConnectBtn) {
                        e.preventDefault();
                        e.stopPropagation();
                        e.stopImmediatePropagation();

                        // Hide any modal backdrop or popup
                        var modals = document.querySelectorAll('.modal, .modal-backdrop');
                        for (var m = 0; m < modals.length; m++) {
                            modals[m].style.setProperty('display', 'none', 'important');
                            modals[m].classList.remove('show');
                        }
                        if (document.body) {
                            document.body.classList.remove('modal-open');
                            document.body.style.removeProperty('overflow');
                        }

                        var info = getServerConnectionInfo();
                        if (info.addr) {
                            var uri = 'minecraft://?addExternalServer=' + encodeURIComponent(info.name) + '|' + info.addr + ':' + info.port;
                            try {
                                if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                                    window.chrome.webview.postMessage(uri);
                                } else {
                                    window.location.href = uri;
                                }
                            } catch(ex) {
                                try { window.location.href = uri; } catch(e2) {}
                            }
                        }
                        return false;
                    }
                }, true);

                cleanBottomContent();
                removeTopSpacing();
                jumpToFirstServer();
                setTimeout(function() { cleanBottomContent(); removeTopSpacing(); }, 150);
                setTimeout(function() { cleanBottomContent(); removeTopSpacing(); }, 400);
                setTimeout(function() { cleanBottomContent(); removeTopSpacing(); }, 1000);
                setTimeout(function() { cleanBottomContent(); removeTopSpacing(); }, 2500);

                if (window.MutationObserver) {
                    try {
                        var observer = new MutationObserver(function() {
                            killPopupsAndModals();
                            applyDarkStylesDirectly();
                        });
                        if (document.body) {
                            observer.observe(document.body, { childList: true, subtree: true });
                        }
                    } catch(e) {}
                }

                let notified = false;
                function notifyApp() {
                    if (notified) return;
                    notified = true;
                    if (window.requestAnimationFrame) {
                        requestAnimationFrame(function() {
                            requestAnimationFrame(function() {
                                setTimeout(function() {
                                    try { window.chrome.webview.postMessage('CONTENT_READY'); } catch(e) {}
                                }, 150);
                            });
                        });
                    } else {
                        setTimeout(function() {
                            try { window.chrome.webview.postMessage('CONTENT_READY'); } catch(e) {}
                        }, 150);
                    }
                }

                // 1. Immediate check if video or image is already loaded/cached
                function checkImmediate() {
                    var banners = document.querySelectorAll('.server-banner-wrap video, .server-banner-wrap img, video.mp4-fluid');
                    for (var i = 0; i < banners.length; i++) {
                        var el = banners[i];
                        if (el.tagName === 'VIDEO' && (el.readyState >= 2 || el.videoWidth > 0)) {
                            notifyApp();
                            return true;
                        }
                        if (el.tagName === 'IMG' && el.complete && el.naturalWidth > 0) {
                            notifyApp();
                            return true;
                        }
                    }
                    return false;
                }

                if (!checkImmediate()) {
                    // 2. Event capturing for autoplay videos starting playback
                    document.addEventListener('play', function(e) {
                        if (e.target && e.target.tagName === 'VIDEO') {
                            notifyApp();
                        }
                    }, true);

                    document.addEventListener('canplay', function(e) {
                        if (e.target && e.target.tagName === 'VIDEO') {
                            notifyApp();
                        }
                    }, true);

                    document.addEventListener('loadeddata', function(e) {
                        if (e.target && e.target.tagName === 'VIDEO') {
                            notifyApp();
                        }
                    }, true);

                    // 3. Event capturing for banner images loading
                    document.addEventListener('load', function(e) {
                        if (e.target && e.target.tagName === 'IMG') {
                            notifyApp();
                        }
                    }, true);

                    // 4. Polling fallback to check if an element became ready
                    var checkInterval = setInterval(function() {
                        if (checkImmediate() || notified) {
                            clearInterval(checkInterval);
                        }
                    }, 100);

                    // 5. Safety timeout
                    setTimeout(function() {
                        clearInterval(checkInterval);
                        notifyApp();
                    }, 8000);
                }
            })();
        ";
    }
}
