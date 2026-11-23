(function () {
    'use strict';

    const chatContainer = document.getElementById('chat');
    let maxMessages = 50;
    let showTimestamp = true;
    let platformBadgeMode = 'auto';
    let serverShowPlatformBadge = false;
    let showPlatformBadge = false;
    let enableAnimations = true;
    const seenMessagePlatforms = new Set();

    const urlParams = new URLSearchParams(globalThis.location.search);
    const isPreview = urlParams.get('preview') === 'true';

    function normalizeOverlayTheme(value) {
        return typeof value === 'string' && value.trim().toLowerCase() === 'flow' ? 'flow' : 'classic';
    }

    function parseThemeOverride(value) {
        if (typeof value !== 'string') return null;
        const normalized = value.trim().toLowerCase();
        return normalized === 'flow' || normalized === 'classic' ? normalized : null;
    }

    const themeQueryOverride = parseThemeOverride(urlParams.get('theme'));
    let configuredOverlayTheme = 'classic';
    let activeOverlayTheme = themeQueryOverride ?? configuredOverlayTheme;
    document.documentElement.dataset.overlayTheme = activeOverlayTheme;

    function isFlowTheme() {
        return activeOverlayTheme === 'flow';
    }

    const messageMeta = new WeakMap();

    let showUserTypeBorders = true;
    let highlightFirstTimeUsers = true;
    let isHighlightMentions = true;
    let enableMessageShadows = true;
    let enableSpecialEffects = true;
    let showUserAvatars = false;
    let showMessageImages = false;

    let enableSmoothScroll = true;
    let scrollAnimationDuration = 300;
    let autoScrollEnabled = true;
    let scrollPaused = false;
    let scrollPauseTimerId = null;
    let scrollToBottomThreshold = 100;
    let scrollPauseAfterUserMs = 3000;
    let lastScrollTime = 0;
    let scrollAnimationId = null;

    let userMessageAnimation = 'slide-in-right';
    let botMessageAnimation = 'fade-in-up';
    let systemMessageAnimation = 'fade-in-up';
    let broadcasterMessageAnimation = 'slide-in-left';
    let firstTimeUserMessageAnimation = 'bounce-in';

    let enableMessageFadeOut = !isPreview; // Отключаем затухание сразу, если это превью
    let messageLifetime = 30000;
    let fadeOutAnimationType = 'fade-out';
    let fadeOutDuration = 1000;

    const seenMessageIds = new Set();
    const seenMessageIdsOrder = [];
    const seenMessageIdsLimit = 1000;

    let eventSource = null;
    let historyLoadGeneration = 0;
    let historyFetchController = null;
    let lastEventTime = Date.now();
    let staleThresholdMs = 70000;
    let reconnecting = false;
    let watchdogIntervalId = null;
    let reconnectTimeoutId = null;
    let reconnectDelayMs = 3000;
    const reconnectMinDelayMs = 3000;
    const reconnectMaxDelayMs = 30000;
    const watchdogTickMs = 10000;

    function markAlive() {
        lastEventTime = Date.now();
    }

    const avatarMemoryCache = new Map();
    const avatarStorageKeyPrefix = 'poproshaykabot.avatar.';
    const avatarStorageTtlMs = 24 * 60 * 60 * 1000;
    const avatarMissTtlMs = 30 * 60 * 1000;

    function formatTimestamp(date) {
        if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
            return '';
        }
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');
        const seconds = String(date.getSeconds()).padStart(2, '0');
        return `${hours}:${minutes}:${seconds}`;
    }

    function readAvatarFromStorage(userId) {
        try {
            const raw = globalThis.localStorage.getItem(avatarStorageKeyPrefix + userId);
            if (!raw) return undefined;
            const parsed = JSON.parse(raw);
            if (!parsed || typeof parsed.exp !== 'number' || Date.now() > parsed.exp) {
                globalThis.localStorage.removeItem(avatarStorageKeyPrefix + userId);
                return undefined;
            }
            const url = typeof parsed.url === 'string' ? parsed.url : '';
            const maxTtl = url ? avatarStorageTtlMs : avatarMissTtlMs;
            const maxExp = Date.now() + maxTtl;
            const exp = Math.min(parsed.exp, maxExp);
            if (parsed.exp > maxExp) {
                writeAvatarToStorage(userId, url, maxTtl);
            }
            return { url, exp };
        } catch (e) {
            console.debug('Не удалось прочитать аватар из localStorage', e);
            return undefined;
        }
    }

    function writeAvatarToStorage(userId, url, ttlMs = avatarStorageTtlMs) {
        try {
            globalThis.localStorage.setItem(
                avatarStorageKeyPrefix + userId,
                JSON.stringify({ url: url || '', exp: Date.now() + ttlMs })
            );
        } catch (e) {
            console.debug('localStorage недоступен или переполнен', e);
        }
    }

    function isTwitchPlatform(platform) {
        return !platform || platform === 'Twitch';
    }

    function normalizePlatformBadgeMode(value) {
        if (typeof value !== 'string') return 'auto';
        const normalized = value.trim().toLowerCase();
        return normalized === 'always' || normalized === 'hidden' ? normalized : 'auto';
    }

    function normalizePlatformName(platform) {
        return typeof platform === 'string' ? platform.trim().toLowerCase() : '';
    }

    function resolvePlatformBadgeVisibility() {
        if (platformBadgeMode === 'always') return true;
        if (platformBadgeMode === 'hidden') return false;
        return serverShowPlatformBadge
            || seenMessagePlatforms.size > 1
            || (seenMessagePlatforms.size === 1 && !seenMessagePlatforms.has('twitch'));
    }

    function syncPlatformBadgeVisibility() {
        const next = resolvePlatformBadgeVisibility();
        const changed = next !== showPlatformBadge;
        showPlatformBadge = next;
        toggleNegationClass('.message', 'no-platform-badge', showPlatformBadge);
        if (changed && isFlowTheme() && chatContainer) {
            chatContainer.querySelectorAll('.message.flow').forEach(scheduleFlowShape);
        }
    }

    function renderPlatformBadge(platform) {
        if (!platform) return '';
        const normalized = normalizePlatformName(platform);
        if (!normalized) return '';
        if (normalized === 'twitch') {
            return '<span class="platform-badge platform-badge--twitch" title="Twitch" aria-label="Twitch">'
                + '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M4.3 3H20v11.7l-4.7 4.7h-3.9l-2.5 2.6H6.5v-2.6H3V6.2L4.3 3zm1.7 2v11.6h3.5v2.3l2.3-2.3h4.2l2.3-2.3V5H6zm8.8 3.2h1.9v5.2h-1.9V8.2zm-4.9 0h1.9v5.2H9.9V8.2z"/></svg>'
                + '</span>';
        }
        if (normalized === 'youtube') {
            return '<span class="platform-badge platform-badge--youtube" title="YouTube" aria-label="YouTube">'
                + '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M21.6 7.2a2.7 2.7 0 0 0-1.9-1.9C18 4.8 12 4.8 12 4.8s-6 0-7.7.5a2.7 2.7 0 0 0-1.9 1.9C2 8.9 2 12 2 12s0 3.1.4 4.8a2.7 2.7 0 0 0 1.9 1.9c1.7.5 7.7.5 7.7.5s6 0 7.7-.5a2.7 2.7 0 0 0 1.9-1.9c.4-1.7.4-4.8.4-4.8s0-3.1-.4-4.8zM10 15.1V8.9L15.4 12 10 15.1z"/></svg>'
                + '</span>';
        }
        const safeLabel = escapeAttr(platform);
        return `<span class="platform-badge platform-badge--other" title="${safeLabel}" aria-label="${safeLabel}">${escapeHtml(platform)}</span>`;
    }

    function resolveAvatar(userId, platform) {
        if (!userId || !isTwitchPlatform(platform)) return Promise.resolve(null);

        const cached = avatarMemoryCache.get(userId);
        if (cached) {
            if (cached.exp === null || Date.now() < cached.exp) {
                return cached.promise;
            }
            avatarMemoryCache.delete(userId);
        }

        const stored = readAvatarFromStorage(userId);
        if (stored !== undefined) {
            const resolved = Promise.resolve(stored.url || null);
            avatarMemoryCache.set(userId, { promise: resolved, exp: stored.exp });
            return resolved;
        }

        const entry = { promise: null, exp: null };
        entry.promise = fetch('/api/user-avatar?id=' + encodeURIComponent(userId))
            .then(response => {
                if (response.status === 404) {
                    writeAvatarToStorage(userId, '', avatarMissTtlMs);
                    entry.exp = Date.now() + avatarMissTtlMs;
                    return null;
                }
                if (!response.ok) {
                    if (avatarMemoryCache.get(userId) === entry) {
                        avatarMemoryCache.delete(userId);
                    }
                    return null;
                }
                return response.json().then(payload => {
                    const url = payload?.url ?? null;
                    if (url) {
                        writeAvatarToStorage(userId, url, avatarStorageTtlMs);
                        entry.exp = Date.now() + avatarStorageTtlMs;
                    } else if (avatarMemoryCache.get(userId) === entry) {
                        avatarMemoryCache.delete(userId);
                    }
                    return url;
                });
            })
            .catch(() => {
                if (avatarMemoryCache.get(userId) === entry) {
                    avatarMemoryCache.delete(userId);
                }
                return null;
            });

        avatarMemoryCache.set(userId, entry);
        return entry.promise;
    }

    function attachAvatar(messageDiv, placeholder, userId, platform) {
        if (!placeholder) return;

        resolveAvatar(userId, platform).then(url => {
            if (!url) {
                placeholder.classList.add('avatar-missing');
                scheduleFlowShape(messageDiv);
                return;
            }
            placeholder.addEventListener('load', () => scheduleFlowShape(messageDiv), { once: true });
            placeholder.addEventListener('error', () => {
                placeholder.classList.add('avatar-missing');
                scheduleFlowShape(messageDiv);
            }, { once: true });
            placeholder.src = url;
            placeholder.classList.add('avatar-loaded');
            scheduleFlowShape(messageDiv);
        });
    }

    function renderAvatarPlaceholder(userId, platform) {
        if (!showUserAvatars || !userId || !isTwitchPlatform(platform)) return '';
        const safeId = escapeAttr(userId);
        return `<img class="avatar" data-user-id="${safeId}" alt="" src="" loading="lazy">`;
    }

    const fadeTimers = new WeakMap();

    const flowSvgNs = 'http://www.w3.org/2000/svg';
    const flowPieces = ['♚', '♛', '♜', '♝', '♞', '♟'];
    const flowRoman = ['', 'I', 'II', 'III'];
    const flowChips = { BotResponse: '✦', SystemNotification: '!', reload: '↻' };
    const flowPad = { x: 10, top: 2, bottom: 4 };
    const flowStepFloor = 12;
    const flowSmoothing = 8;
    const pendingShapeNodes = new Set();
    let shapeRafId = null;

    const flowResizeObserver = typeof ResizeObserver !== 'undefined'
        ? new ResizeObserver(entries => entries.forEach(entry => {
            if (isFlowTheme() && entry.target instanceof HTMLElement && entry.target.classList.contains('flow')) {
                shapeFlowMessage(entry.target);
            }
        }))
        : null;

    if (document.fonts && typeof document.fonts.addEventListener === 'function') {
        document.fonts.addEventListener('loadingdone', () => {
            if (!isFlowTheme() || !chatContainer) return;
            chatContainer.querySelectorAll('.message.flow').forEach(scheduleFlowShape);
        });
    }

    function removeMessageNode(node) {
        if (!node) return;
        cancelFadeOut(node);
        flowResizeObserver?.unobserve(node);
        pendingShapeNodes.delete(node);
        node.remove();
    }

    function createFlowMedal(rank) {
        const icon = typeof rank?.emoji === 'string' ? rank.emoji.trim() : '';
        if (!icon) return null;

        const tier = Math.max(0, Math.min(3, Math.trunc(Number(rank.tier) || 0)));
        const medal = document.createElement('span');
        medal.className = 'flow-medal';
        medal.dataset.tier = String(tier);
        medal.title = tier ? `${rank.name} ${flowRoman[tier]}` : String(rank.name ?? '');

        const glyph = document.createElement('b');
        const piece = icon.replace(/[\uFE0E\uFE0F]$/, '');
        glyph.textContent = flowPieces.includes(piece) ? `${piece}\uFE0E` : icon;
        medal.append(glyph);

        if (tier) {
            const dots = document.createElement('span');
            dots.className = 'flow-tier';
            for (let i = 0; i < tier; i++) {
                dots.append(document.createElement('i'));
            }
            medal.append(dots);
        }

        return medal;
    }

    function ensureFlowHead(node, meta, existingAvatar) {
        let head = node.querySelector(':scope > .flow-head');
        if (head) return head;

        const name = meta?.displayName || node.querySelector('.username')?.textContent || '';
        head = document.createElement('span');
        head.className = 'flow-head';
        head.dataset.initial = name.trim().charAt(0).toUpperCase();

        const avatar = existingAvatar || node.querySelector(':scope > .avatar');
        if (avatar) {
            head.append(avatar);
        } else if (showUserAvatars && meta?.userId && isTwitchPlatform(meta.platform)) {
            const img = document.createElement('img');
            img.className = 'avatar';
            img.dataset.userId = meta.userId;
            img.alt = '';
            img.src = '';
            img.loading = 'lazy';
            head.append(img);
            attachAvatar(node, img, meta.userId, meta.platform);
        }

        const content = node.querySelector(':scope > .message-content');
        if (content) {
            node.insertBefore(head, content);
        } else {
            node.prepend(head);
        }

        return head;
    }

    function syncFlowMedalPlacement(node) {
        const medal = node.querySelector('.flow-medal');
        if (!medal) return;

        const head = node.querySelector(':scope > .flow-head');
        const header = node.querySelector('.message-header');
        if (showUserAvatars && head) {
            if (medal.parentElement !== head) {
                head.append(medal);
            }
        } else if (header && medal.parentElement !== header) {
            header.prepend(medal);
        }
    }

    function decorateFlowMessage(node) {
        if (!(node instanceof HTMLElement) || !node.classList.contains('message') || node.classList.contains('flow')) {
            return;
        }

        const meta = messageMeta.get(node);
        node.classList.add('flow');

        if (node.querySelector('.message-content')) {
            const avatar = node.querySelector(':scope > .avatar');
            const medal = createFlowMedal(meta?.rank);
            const needHead = Boolean(avatar) || (showUserAvatars && (Boolean(meta?.displayName) || !isTwitchPlatform(meta?.platform)));
            const head = needHead ? ensureFlowHead(node, meta, avatar) : null;

            if (medal) {
                if (showUserAvatars && head) {
                    head.append(medal);
                } else {
                    node.querySelector('.message-header')?.prepend(medal);
                }
            }
        } else {
            const kind = meta?.isReloadBanner || node.classList.contains('reload-banner')
                ? 'reload'
                : meta?.messageType === 'BotResponse'
                    ? 'BotResponse'
                    : 'SystemNotification';
            const chip = document.createElement('span');
            chip.className = 'flow-chip';
            chip.dataset.kind = kind;
            chip.setAttribute('aria-hidden', 'true');
            chip.textContent = flowChips[kind] || flowChips.SystemNotification;
            node.classList.add('flow-capsule');
            node.prepend(chip);
        }

        const svg = document.createElementNS(flowSvgNs, 'svg');
        svg.classList.add('flow-shape');
        svg.setAttribute('aria-hidden', 'true');
        svg.append(document.createElementNS(flowSvgNs, 'path'));
        node.prepend(svg);

        flowResizeObserver?.observe(node);
    }

    function undecorateFlowMessage(node) {
        if (!(node instanceof HTMLElement) || !node.classList.contains('flow')) {
            return;
        }

        flowResizeObserver?.unobserve(node);
        pendingShapeNodes.delete(node);

        node.querySelector(':scope > .flow-shape')?.remove();
        node.querySelector(':scope > .flow-chip')?.remove();
        node.querySelector('.flow-medal')?.remove();

        const head = node.querySelector(':scope > .flow-head');
        if (head) {
            const avatar = head.querySelector(':scope > .avatar');
            const content = node.querySelector(':scope > .message-content');
            if (avatar && content) {
                node.insertBefore(avatar, content);
            }
            head.remove();
        }

        node.classList.remove('flow', 'flow-capsule');
        ['--flow-path', '--flow-h', '--flow-reach', '--hx', '--hy', '--hr'].forEach(prop => node.style.removeProperty(prop));
    }

    function addRectToBoxes(r, boxes) {
        if (r.width < 1 || r.height < 1) return;
        const row = boxes.find(b => Math.min(b.bottom, r.bottom) - Math.max(b.top, r.top) > Math.min(b.bottom - b.top, r.height) / 2);
        if (row) {
            row.left = Math.min(row.left, r.left);
            row.right = Math.max(row.right, r.right);
            row.top = Math.min(row.top, r.top);
            row.bottom = Math.max(row.bottom, r.bottom);
        } else {
            boxes.push({ left: r.left, right: r.right, top: r.top, bottom: r.bottom });
        }
    }

    function lineBoxes(element, boxes) {
        if (!element || !element.getClientRects().length) return;
        const range = document.createRange();
        range.selectNodeContents(element);
        for (const r of range.getClientRects()) {
            addRectToBoxes(r, boxes);
        }
    }

    function bandsOf(node) {
        const origin = node.getBoundingClientRect();
        const boxes = [];

        if (node.classList.contains('flow-capsule')) {
            const ts = node.querySelector(':scope > .timestamp');
            const badge = node.querySelector(':scope > .platform-badge');
            const sys = node.querySelector(':scope > .system-message');
            if (ts && ts.offsetHeight > 0) lineBoxes(ts, boxes);
            if (badge && badge.offsetWidth > 0 && badge.offsetHeight > 0) {
                addRectToBoxes(badge.getBoundingClientRect(), boxes);
            }
            if (sys && sys.offsetHeight > 0) lineBoxes(sys, boxes);
        } else {
            const header = node.querySelector('.message-header');
            if (header) {
                lineBoxes(header, boxes);
                const badge = header.querySelector('.platform-badge');
                if (badge && badge.offsetWidth > 0 && badge.offsetHeight > 0) {
                    addRectToBoxes(badge.getBoundingClientRect(), boxes);
                }
            }
            const msgText = node.querySelector('.message-text');
            if (msgText && msgText.offsetHeight > 0) {
                lineBoxes(msgText, boxes);
            }
            const picture = node.querySelector('.message-images img');
            if (picture && picture.offsetHeight > 0) {
                addRectToBoxes(picture.getBoundingClientRect(), boxes);
            }
        }

        boxes.sort((a, b) => a.top - b.top);
        const bands = boxes.map((box, i) => ({
            left: box.left - origin.left - flowPad.x,
            right: box.right - origin.left + flowPad.x,
            top: (i ? (boxes[i - 1].bottom + box.top) / 2 : box.top - flowPad.top) - origin.top,
            bottom: (i < boxes.length - 1 ? (box.bottom + boxes[i + 1].top) / 2 : box.bottom + flowPad.bottom) - origin.top,
        }));

        for (let changed = true; changed;) {
            changed = false;
            for (let i = 1; i < bands.length; i++) {
                const a = bands[i - 1];
                const b = bands[i];
                if (a.right !== b.right && Math.abs(a.right - b.right) < flowStepFloor) {
                    a.right = b.right = Math.max(a.right, b.right);
                    changed = true;
                }
                if (a.left !== b.left && Math.abs(a.left - b.left) < flowStepFloor) {
                    a.left = b.left = Math.min(a.left, b.left);
                    changed = true;
                }
            }
        }

        return bands;
    }

    const flowEase = t => t * t * (3 - 2 * t);

    function flowEdge(bands, circle, y, top, bottom, side) {
        const key = side > 0 ? 'right' : 'left';
        const reach = i => side * bands[i][key];
        const disc = circle && Math.abs(y - circle.y) < circle.r
            ? side * circle.x + Math.sqrt(circle.r ** 2 - (y - circle.y) ** 2)
            : -Infinity;
        const at = bands.findIndex(b => y >= b.top && y <= b.bottom);
        const floor = Math.max(disc, at < 0 ? -Infinity : reach(at));
        if (floor === -Infinity) return side * floor;

        let x = floor;
        bands.forEach((band, i) => {
            const v = reach(i);
            if (v <= floor) return;
            for (const dir of [1, -1]) {
                const gap = dir > 0 ? y - band.bottom : band.top - y;
                if (gap <= 0) continue;
                const n = i + dir;
                let span;
                if (n < 0 || n >= bands.length) {
                    span = dir > 0 ? bottom - band.bottom : band.top - top;
                } else {
                    if (reach(n) >= v) continue;
                    span = bands[n].bottom - bands[n].top;
                    const far = n + dir;
                    if (far >= 0 && far < bands.length && reach(far) > reach(n)) span /= 2;
                }
                if (span > 0 && gap < span) x = Math.max(x, floor + (v - floor) * (1 - flowEase(gap / span)));
            }
        });

        return side * x;
    }

    function flowOutline(bands, circle) {
        const top = Math.min(bands.length ? bands[0].top : Infinity, circle ? circle.y - circle.r : Infinity);
        const bottom = Math.max(bands.length ? bands[bands.length - 1].bottom : -Infinity, circle ? circle.y + circle.r : -Infinity);
        if (!Number.isFinite(top) || !Number.isFinite(bottom) || bottom <= top) return '';

        const right = [];
        const left = [];
        for (let y = top; ; y = Math.min(y + 1, bottom)) {
            const l = flowEdge(bands, circle, y, top, bottom, -1);
            const r = flowEdge(bands, circle, y, top, bottom, 1);
            if (l < r) {
                right.push([r, y]);
                left.push([l, y]);
            }
            if (y >= bottom) break;
        }

        const corners = right.concat(left.reverse());
        if (corners.length < 3) return '';

        const even = [];
        corners.forEach(([x1, y1], i) => {
            const [x2, y2] = corners[(i + 1) % corners.length];
            const steps = Math.max(1, Math.round(Math.hypot(x2 - x1, y2 - y1)));
            for (let s = 0; s < steps; s++) {
                even.push([x1 + (x2 - x1) * s / steps, y1 + (y2 - y1) * s / steps]);
            }
        });

        let points = even;
        for (let pass = 0; pass < 2; pass++) {
            const n = points.length;
            points = points.map((_, i) => {
                let sx = 0;
                let sy = 0;
                for (let j = -flowSmoothing; j <= flowSmoothing; j++) {
                    const [px, py] = points[(i + j + n) % n];
                    sx += px;
                    sy += py;
                }
                return [sx / (2 * flowSmoothing + 1), sy / (2 * flowSmoothing + 1)];
            });
        }

        return 'M' + points.filter((_, i) => i % 2 === 0).map(([x, y]) => `${x.toFixed(1)} ${y.toFixed(1)}`).join('L') + 'Z';
    }

    function shapeFlowMessage(node) {
        if (!node || !node.isConnected || !node.classList.contains('flow') || node.dataset.exitAnim) {
            return;
        }

        const head = node.querySelector(':scope > .flow-head, :scope > .flow-chip');
        const circle = head && head.offsetWidth
            ? { x: head.offsetLeft + head.offsetWidth / 2, y: head.offsetTop + head.offsetHeight / 2, r: head.offsetWidth / 2 }
            : null;
        const bands = bandsOf(node);
        if (!bands.length && !circle) return;

        const d = flowOutline(bands, circle);
        if (!d) return;

        const svg = node.querySelector(':scope > .flow-shape');
        if (!svg || !svg.firstChild) return;

        const width = node.offsetWidth;
        const height = node.offsetHeight;
        const { x, y, r } = circle ?? { x: 30, y: 30, r: 30 };
        const reach = Math.ceil(Math.hypot(width, height)) + Math.max(r, flowPad.x);

        svg.setAttribute('width', String(width));
        svg.setAttribute('height', String(height));
        svg.firstChild.setAttribute('d', d);
        node.style.setProperty('--flow-path', `path("${d}")`);
        node.style.setProperty('--flow-h', `${height}px`);
        node.style.setProperty('--flow-reach', `${reach}px`);
        node.style.setProperty('--hx', `${x}px`);
        node.style.setProperty('--hy', `${y}px`);
        node.style.setProperty('--hr', `${r}px`);
    }

    function scheduleFlowShape(node) {
        if (!node || !isFlowTheme()) return;
        pendingShapeNodes.add(node);
        if (shapeRafId !== null) return;
        shapeRafId = requestAnimationFrame(() => {
            shapeRafId = null;
            const nodes = Array.from(pendingShapeNodes);
            pendingShapeNodes.clear();
            if (!isFlowTheme()) return;
            nodes.forEach(shapeFlowMessage);
        });
    }

    function syncAllFlowMessages() {
        if (!chatContainer) return;
        const messages = chatContainer.querySelectorAll('.message');
        if (isFlowTheme()) {
            messages.forEach(node => {
                if (!node.classList.contains('flow')) {
                    decorateFlowMessage(node);
                } else if (node.querySelector('.message-content')) {
                    if (showUserAvatars && !node.querySelector(':scope > .flow-head')) {
                        const meta = messageMeta.get(node);
                        ensureFlowHead(node, meta, null);
                    }
                    syncFlowMedalPlacement(node);
                }
                scheduleFlowShape(node);
            });
        } else {
            messages.forEach(undecorateFlowMessage);
        }
    }

    const newMessagesIndicator = document.getElementById('new-messages-indicator');
    const newMessagesIndicatorCount = document.getElementById('new-messages-indicator-count');
    let pendingNewMessages = 0;

    function rememberMessageId(id) {
        if (!id) return false;
        if (seenMessageIds.has(id)) return true;
        seenMessageIds.add(id);
        seenMessageIdsOrder.push(id);
        if (seenMessageIdsOrder.length > seenMessageIdsLimit) {
            const evicted = seenMessageIdsOrder.shift();
            seenMessageIds.delete(evicted);
        }
        return false;
    }

    function smoothScrollToBottom(force = false) {
        if (!force && (!autoScrollEnabled || scrollPaused)) return;

        const currentTime = Date.now();
        if (currentTime - lastScrollTime < 16) return; // Throttle to ~60fps
        lastScrollTime = currentTime;

        if (scrollAnimationId) {
            cancelAnimationFrame(scrollAnimationId);
        }

        if (enableSmoothScroll && 'scrollBehavior' in document.documentElement.style) {
            chatContainer.scrollTo({
                top: chatContainer.scrollHeight,
                behavior: 'smooth'
            });
        } else {
            const startTop = chatContainer.scrollTop;
            const targetTop = chatContainer.scrollHeight - chatContainer.clientHeight;
            const distance = targetTop - startTop;

            if (Math.abs(distance) < 1) {
                chatContainer.scrollTop = targetTop;
                return;
            }

            const startTime = performance.now();

            function animateScroll(currentTime) {
                const elapsed = currentTime - startTime;
                const progress = Math.min(elapsed / scrollAnimationDuration, 1);

                const easeOut = 1 - Math.pow(1 - progress, 3);

                chatContainer.scrollTop = startTop + distance * easeOut;

                if (progress < 1) {
                    scrollAnimationId = requestAnimationFrame(animateScroll);
                } else {
                    scrollAnimationId = null;
                }
            }

            scrollAnimationId = requestAnimationFrame(animateScroll);
        }
    }

    function isNearBottom() {
        return chatContainer.scrollHeight - chatContainer.scrollTop - chatContainer.clientHeight <= scrollToBottomThreshold;
    }

    function clearScrollPauseTimer() {
        if (scrollPauseTimerId !== null) {
            clearTimeout(scrollPauseTimerId);
            scrollPauseTimerId = null;
        }
    }

    function handleUserScroll() {
        if (!isNearBottom()) {
            scrollPaused = true;
            clearScrollPauseTimer();
            if (scrollPauseAfterUserMs > 0) {
                scrollPauseTimerId = setTimeout(() => {
                    scrollPauseTimerId = null;
                    scrollPaused = false;
                }, scrollPauseAfterUserMs);
            }
            return;
        }

        clearScrollPauseTimer();
        scrollPaused = false;
        hideNewMessagesIndicator();
    }

    function showNewMessagesIndicator() {
        if (!newMessagesIndicator) return;
        newMessagesIndicator.hidden = false;
        newMessagesIndicator.classList.add('visible');
    }

    function hideNewMessagesIndicator() {
        if (!newMessagesIndicator) return;
        newMessagesIndicator.classList.remove('visible');
        pendingNewMessages = 0;
        if (newMessagesIndicatorCount) {
            newMessagesIndicatorCount.textContent = '0';
        }
    }

    function bumpNewMessagesIndicator() {
        if (!newMessagesIndicator) return;
        pendingNewMessages += 1;
        if (newMessagesIndicatorCount) {
            newMessagesIndicatorCount.textContent = String(pendingNewMessages);
        }
        showNewMessagesIndicator();
    }

    if (chatContainer) {
        let scrollTimeout;
        chatContainer.addEventListener('scroll', () => {
            clearTimeout(scrollTimeout);
            scrollTimeout = setTimeout(handleUserScroll, 50);
        }, { passive: true });
    }

    if (newMessagesIndicator) {
        newMessagesIndicator.addEventListener('click', () => {
            hideNewMessagesIndicator();
            clearScrollPauseTimer();
            scrollPaused = false;
            smoothScrollToBottom(true);
        });
    }

    function getUserTypeClasses(userStatus) {
        const classes = [];

        if (userStatus & 1) classes.push('broadcaster');
        if (userStatus & 2) classes.push('moderator');
        if (userStatus & 4) classes.push('vip');
        if (userStatus & 8) classes.push('subscriber');

        return classes;
    }

    const validEntryAnimations = new Set([
        'no-animation',
        'slide-in-right',
        'slide-in-left',
        'fade-in-up',
        'bounce-in',
        'pop-in',
        'rubber-band',
        'tada',
        'zoom-blur-in',
        'neon-pulse-in',
        'materialize',
        'glitch-in',
        'power-up',
        'slam-in',
        'notification-bell',
        'hologram',
        'flip-in-x',
        'roll-in',
        'coin-flip',
        'swoop-in',
    ]);

    function sanitizeEntryAnimation(value, fallback) {
        return validEntryAnimations.has(value) ? value : fallback;
    }

    function getAnimationType(userStatus, messageType, isFirstTime = false) {
        let raw;
        if (isFirstTime) {
            raw = firstTimeUserMessageAnimation;
        } else {
            switch (messageType) {
                case 'BotResponse':
                    raw = botMessageAnimation;
                    break;
                case 'SystemNotification':
                    raw = systemMessageAnimation;
                    break;
                case 'UserMessage':
                    raw = userStatus & 1 ? broadcasterMessageAnimation : userMessageAnimation;
                    break;
                default:
                    raw = userMessageAnimation;
            }
        }
        return sanitizeEntryAnimation(raw, 'slide-in-right');
    }

    function initEventSource() {
        if (eventSource) {
            try {
                eventSource.close();
            } catch (e) { /* noop */
            }
        }

        const es = new EventSource('/events?reload=1');
        eventSource = es;

        es.onopen = function () {
            reconnectDelayMs = reconnectMinDelayMs;
            reconnecting = false;
            markAlive();
            showReloadBanner();
        };

        es.addEventListener('message', function (event) {
            markAlive();
            try {
                addMessage(JSON.parse(event.data));
            } catch (e) {
                console.error('Ошибка парсинга SSE message:', e);
            }
        });

        es.addEventListener('clear', function (event) {
            markAlive();
            let payload = null;
            if (event && event.data) {
                try {
                    payload = JSON.parse(event.data);
                } catch {
                    payload = null;
                }
            }
            clearChat(payload);
            if (payload && payload.reloadHistory === true) {
                loadHistoryAndBackfill()
                    .catch(error => console.error('Ошибка обновления истории оверлея:', error));
            }
        });

        es.addEventListener('delete', function (event) {
            markAlive();
            try {
                deleteChatMessages(JSON.parse(event.data));
            } catch (e) {
                console.error('Ошибка парсинга SSE delete:', e);
            }
        });

        es.addEventListener('chat_settings_changed', function (event) {
            markAlive();
            try {
                updateChatSettings(JSON.parse(event.data));
            } catch (e) {
                console.error('Ошибка парсинга SSE chat_settings_changed:', e);
            }
        });

        es.addEventListener('ping', function (event) {
            markAlive();
            try {
                const payload = JSON.parse(event.data);
                if (typeof payload.intervalSeconds === 'number' && payload.intervalSeconds > 0) {
                    staleThresholdMs = Math.max(70000, payload.intervalSeconds * 1000 * 2 + 10000);
                }
            } catch (e) {
                console.debug('Не удалось разобрать ping SSE:', e);
            }
        });

        es.addEventListener('dropped', function (event) {
            markAlive();
            try {
                const payload = JSON.parse(event.data);
                console.warn(
                    'Сервер сбросил сообщения SSE. Глобальная очередь:',
                    payload.count,
                    '| По клиентам:',
                    payload.clientCount);
            } catch (e) {
                console.warn('Сервер сбросил сообщения SSE (детали недоступны).', e);
            }
        });

        es.onerror = function (event) {
            console.error('SSE ошибка:', event);
            if (es === eventSource && es.readyState === EventSource.CLOSED) {
                scheduleReconnect();
            }
        };
    }

    const validExitAnimations = new Set([
        'fade-out',
        'slide-out-left',
        'slide-out-right',
        'scale-down',
        'shrink-up',
        'dissolve',
        'slide-out-up',
        'slide-out-down',
        'rotate-out',
        'pixelate-out',
    ]);

    function fadeOutMessage(messageDiv) {
        if (!enableMessageFadeOut) return;
        if (messageDiv.dataset.fading === 'true') return;
        messageDiv.dataset.fading = 'true';

        if (fadeOutAnimationType === 'no-animation' || !validExitAnimations.has(fadeOutAnimationType)) {
            setTimeout(() => removeMessageNode(messageDiv), 100);
            return;
        }

        const measuredHeight = Math.ceil(messageDiv.offsetHeight);
        if (measuredHeight > 0) {
            messageDiv.style.setProperty('--exit-max-height', `${measuredHeight}px`);
        }

        messageDiv.dataset.exitAnim = fadeOutAnimationType;

        setTimeout(() => removeMessageNode(messageDiv), fadeOutDuration);
    }

    function cancelFadeOut(messageDiv) {
        const id = fadeTimers.get(messageDiv);
        if (id !== undefined) {
            clearTimeout(id);
            fadeTimers.delete(messageDiv);
        }
    }

    function scheduleFadeOut(messageDiv) {
        cancelFadeOut(messageDiv);
        if (!enableMessageFadeOut) return;
        if (messageDiv.dataset.fading === 'true') return;

        const createdAt = Number.parseInt(messageDiv.dataset.createdAt, 10) || Date.now();
        const remaining = Math.max(0, messageLifetime - (Date.now() - createdAt));

        const id = setTimeout(() => {
            fadeTimers.delete(messageDiv);
            fadeOutMessage(messageDiv);
        }, remaining);
        fadeTimers.set(messageDiv, id);
    }

    function rescheduleAllFades() {
        document.querySelectorAll('#chat .message').forEach(messageDiv => {
            if (enableMessageFadeOut) {
                scheduleFadeOut(messageDiv);
            } else {
                cancelFadeOut(messageDiv);
            }
        });
    }

    function applyEntryAnimation(messageDiv, message, isHistoryMessage) {
        if (isHistoryMessage || !enableAnimations) {
            messageDiv.classList.add('no-animation', 'entry-done');
            return;
        }

        const animationType = getAnimationType(message.status || 0, message.messageType, message.isFirstTime);
        messageDiv.classList.add(animationType);

        const onEntryEnd = event => {
            if (event.target !== messageDiv) return;
            messageDiv.removeEventListener('animationend', onEntryEnd);
            messageDiv.classList.add('entry-done');
        };
        messageDiv.addEventListener('animationend', onEntryEnd);
    }

    function applySpecialEffects(messageDiv, message) {
        if (!enableSpecialEffects) {
            messageDiv.classList.add('no-special-effects');
            return;
        }

        const isBroadcasterOrVip = message.status & 1 || message.status & 4;
        if (isBroadcasterOrVip) {
            messageDiv.classList.add('special-effect');
        }
    }

    function applyFirstTimeMarker(messageDiv, message) {
        if (!message.isFirstTime) {
            return;
        }

        if (highlightFirstTimeUsers) {
            messageDiv.classList.add('first-time');
        } else {
            messageDiv.classList.add('first-time', 'no-first-time-effects');
        }
    }

    function applyToggleClasses(messageDiv) {
        const toggles = [
            [showUserTypeBorders, 'no-borders'],
            [enableMessageShadows, 'no-shadows'],
            [isHighlightMentions, 'no-mentions'],
            [showTimestamp, 'no-timestamp'],
            [showPlatformBadge, 'no-platform-badge'],
            [showUserAvatars, 'no-avatars'],
            [showMessageImages, 'no-message-images'],
        ];
        toggles.forEach(([enabled, className]) => {
            if (!enabled) messageDiv.classList.add(className);
        });
    }

    function addMessage(message, isHistoryMessage = false) {
        if (rememberMessageId(message.messageId)) {
            return;
        }

        const isImportedHistory = message.isHistory === true;
        const suppressEntryAnimation = isHistoryMessage || isImportedHistory;
        const parsedTimestampMs = new Date(message.timestamp).getTime();
        const msgTimestampMs = Number.isNaN(parsedTimestampMs) ? Date.now() : parsedTimestampMs;

        let insertBeforeEl = null;
        if (isImportedHistory) {
            const existingMessages = Array.from(chatContainer.querySelectorAll('.message:not(.reload-banner)'));
            if (existingMessages.length >= maxMessages) {
                const oldestTs = Number(existingMessages[0].dataset.timestampMs || '0');
                if (msgTimestampMs <= oldestTs) {
                    return;
                }
            }

            insertBeforeEl = existingMessages.find(el => Number(el.dataset.timestampMs || '0') > msgTimestampMs) || null;
        }

        const messageDiv = document.createElement('div');
        messageDiv.className = 'message';
        messageMeta.set(messageDiv, {
            messageType: message.messageType || 'UserMessage',
            rank: message.rank ?? null,
            displayName: message.displayName || message.username || '',
            platform: message.platform || '',
            userId: message.userId || '',
            isReloadBanner: false,
        });
        messageDiv.dataset.messageId = message.messageId || '';
        messageDiv.dataset.userId = message.userId || '';
        messageDiv.dataset.platform = message.platform || '';
        messageDiv.dataset.channelId = message.channelId || '';
        messageDiv.dataset.chatSessionId = message.chatSessionId || '';
        messageDiv.dataset.timestampMs = String(msgTimestampMs);

        const createdAtMs = isHistoryMessage && !isImportedHistory && !Number.isNaN(parsedTimestampMs)
            ? parsedTimestampMs
            : Date.now();
        messageDiv.dataset.createdAt = String(createdAtMs);

        const normalizedPlatform = normalizePlatformName(message.platform);
        if (normalizedPlatform && !seenMessagePlatforms.has(normalizedPlatform)) {
            seenMessagePlatforms.add(normalizedPlatform);
            if (resolvePlatformBadgeVisibility() !== showPlatformBadge) {
                syncPlatformBadgeVisibility();
            }
        }

        applyEntryAnimation(messageDiv, message, suppressEntryAnimation);

        const userTypeClasses = getUserTypeClasses(message.status || 0);
        userTypeClasses.forEach(cls => messageDiv.classList.add(cls));

        applyToggleClasses(messageDiv);
        applySpecialEffects(messageDiv, message);
        applyFirstTimeMarker(messageDiv, message);

        const timestampDate = new Date(message.timestamp);
        const timestampLabel = formatTimestamp(timestampDate);
        const timestampIso = Number.isNaN(timestampDate.getTime()) ? '' : timestampDate.toISOString();
        const isSystemMessage = message.messageType !== 'UserMessage';

        const timestampHtml = `<time class="timestamp" datetime="${escapeAttr(timestampIso)}">${escapeHtml(timestampLabel)}</time>`;
        const platformBadgeHtml = renderPlatformBadge(message.platform);

        if (isSystemMessage) {
            messageDiv.innerHTML = `
            ${timestampHtml}
            ${platformBadgeHtml}
            <span class="system-message">${escapeHtml(message.message)}</span>
        `;
        } else {
            const badgesHtml = renderBadges(message.badges || []);
            const messageText = String(message.message ?? '');
            const image = pickMessageImage(message.images, message.imageLinks, messageText);
            const messageWithEmotes = renderMessageWithEmotes(messageText, message.emotes || [], image);
            const avatarHtml = renderAvatarPlaceholder(message.userId, message.platform);

            const usernameClasses = ['username', ...userTypeClasses].join(' ');
            const usernameColor = sanitizeUsernameColor(message.color);
            const usernameStyle = usernameColor ? ` style="color: ${usernameColor}"` : '';
            const usernameHtml = `<span class="${usernameClasses}"${usernameStyle}>${escapeHtml(message.displayName || message.username || '')}</span>`;

            const headerHtml = `
            <div class="message-header">
                ${timestampHtml}
                ${platformBadgeHtml}
                ${badgesHtml}
                ${usernameHtml}
            </div>`;

            const textClasses = image && image.linkOnly ? 'message-text message-text-link-only' : 'message-text';
            const bodyHtml = `<div class="${textClasses}">${messageWithEmotes}</div>`;
            const imagesHtml = renderMessageImage(image);

            const contentHtml = `
            <div class="message-content">
                ${headerHtml}
                ${bodyHtml}
                ${imagesHtml}
            </div>`;

            if (avatarHtml) {
                messageDiv.innerHTML = `
            ${avatarHtml}
            ${contentHtml}
        `;

                attachAvatar(messageDiv, messageDiv.querySelector(':scope > .avatar'), message.userId, message.platform);
            } else {
                messageDiv.innerHTML = contentHtml;
            }

            attachMessageImage(messageDiv);
        }

        if (insertBeforeEl) {
            chatContainer.insertBefore(messageDiv, insertBeforeEl);
        } else {
            chatContainer.appendChild(messageDiv);
        }

        if (isFlowTheme()) {
            decorateFlowMessage(messageDiv);
            shapeFlowMessage(messageDiv);
        }

        scheduleFadeOut(messageDiv);

        while (chatContainer.children.length > maxMessages) {
            const removed = chatContainer.firstChild;
            if (removed) {
                removeMessageNode(removed);
            }
        }

        if (suppressEntryAnimation) {
            chatContainer.scrollTop = chatContainer.scrollHeight;
            return;
        }

        if (scrollPaused && autoScrollEnabled) {
            bumpNewMessagesIndicator();
            return;
        }

        requestAnimationFrame(() => smoothScrollToBottom());
    }

    function clearChat(payload) {
        historyLoadGeneration++;
        if (historyFetchController) {
            try {
                historyFetchController.abort();
            } catch { /* noop */ }
            historyFetchController = null;
        }

        const cutoffMs = payload && payload.clearedAtUtc ? new Date(payload.clearedAtUtc).getTime() : NaN;
        if (!Number.isNaN(cutoffMs)) {
            chatContainer.querySelectorAll('.message').forEach(el => {
                const msgTs = Number(el.dataset.timestampMs || '0');
                if (el.classList.contains('reload-banner') || msgTs <= cutoffMs) {
                    removeMessageNode(el);
                }
            });

            const survivingIds = new Set();
            chatContainer.querySelectorAll('.message').forEach(el => {
                if (el.dataset.messageId) {
                    survivingIds.add(el.dataset.messageId);
                }
            });

            seenMessageIds.clear();
            seenMessageIdsOrder.length = 0;
            survivingIds.forEach(id => {
                seenMessageIds.add(id);
                seenMessageIdsOrder.push(id);
            });

            if (chatContainer.children.length === 0) {
                clearScrollPauseTimer();
                scrollPaused = false;
                hideNewMessagesIndicator();
            }

            return;
        }

        chatContainer.querySelectorAll('.message').forEach(removeMessageNode);
        chatContainer.replaceChildren();
        seenMessageIds.clear();
        seenMessageIdsOrder.length = 0;
        clearScrollPauseTimer();
        scrollPaused = false;
        hideNewMessagesIndicator();
    }

    function deleteChatMessages(payload) {
        if (!payload) return;

        const messageIds = new Set(Array.isArray(payload.messageIds) ? payload.messageIds.filter(Boolean) : []);
        const targetUserId = payload.userId || '';
        const targetPlatform = payload.platform || '';
        const targetChannelId = payload.channelId || '';
        const targetSessionId = payload.sessionScopeId || '';
        const allowReplacement = payload.allowReplacement === true;
        const cutoffMs = payload.cutoffUtc ? new Date(payload.cutoffUtc).getTime() : NaN;
        const hasCutoff = !Number.isNaN(cutoffMs);

        if (allowReplacement && messageIds.size > 0) {
            messageIds.forEach(id => seenMessageIds.delete(id));
        }

        document.querySelectorAll('#chat .message').forEach(messageDiv => {
            const msgId = messageDiv.dataset.messageId || '';
            const msgUserId = messageDiv.dataset.userId || '';
            const msgPlatform = messageDiv.dataset.platform || '';
            const msgChannelId = messageDiv.dataset.channelId || '';
            const msgSessionId = messageDiv.dataset.chatSessionId || '';
            const msgTimestampMs = Number(messageDiv.dataset.timestampMs || '0');

            const platformMatches = !targetPlatform || !msgPlatform || msgPlatform === targetPlatform;
            const channelMatches = !targetChannelId || !msgChannelId || msgChannelId === targetChannelId;
            const sessionMatches = !targetSessionId || !msgSessionId || msgSessionId === targetSessionId;
            if (!platformMatches || !channelMatches || !sessionMatches) return;

            const matchesById = Boolean(msgId && messageIds.has(msgId));
            const matchesByUser = Boolean(targetUserId && msgUserId === targetUserId && (!hasCutoff || msgTimestampMs <= cutoffMs));
            if (matchesById || matchesByUser) {
                removeMessageNode(messageDiv);
            }
        });
    }

    function renderBadges(badges) {
        if (!badges || badges.length === 0) return '';

        return badges.map(badge => {
            if (!badge.imageUrl) return '';
            const src = escapeAttr(badge.imageUrl);
            const type = escapeAttr(badge.type);
            const version = escapeAttr(badge.version);
            return `<img src="${src}" alt="${type}" title="${type} ${version}" class="badge">`;
        }).join('');
    }

    const messageImageProxyPrefix = '/api/image?';

    function pickMessageImage(images, imageLinks, text) {
        if (!showMessageImages || !Array.isArray(images) || images.length === 0) {
            return null;
        }

        const url = images[0];

        if (typeof url !== 'string' || !url.startsWith(messageImageProxyPrefix)) {
            return null;
        }

        const range = Array.isArray(imageLinks) ? imageLinks[0] : null;

        if (!range || !isValidRange(range.startIndex, range.endIndex, text.length)) {
            return { url, link: null, linkOnly: false };
        }

        const link = { startIndex: range.startIndex, endIndex: range.endIndex };
        const linkOnly = text.substring(0, link.startIndex).trim() === ''
            && text.substring(link.endIndex + 1).trim() === '';

        return { url, link, linkOnly };
    }

    function isValidRange(startIndex, endIndex, length) {
        return Number.isInteger(startIndex) && Number.isInteger(endIndex)
            && startIndex >= 0 && endIndex >= startIndex && endIndex < length;
    }

    function renderMessageImage(image) {
        if (!image) {
            return '';
        }

        return `<div class="message-images"><img class="message-image" src="${escapeAttr(image.url)}" alt="" loading="lazy"></div>`;
    }

    function attachMessageImage(messageDiv) {
        const image = messageDiv.querySelector('.message-image');

        if (!image) {
            return;
        }

        image.addEventListener('error', () => {
            const container = image.closest('.message-images');
            if (container) container.remove();
            scheduleFlowShape(messageDiv);
        }, { once: true });

        image.addEventListener('load', () => {
            image.classList.add('message-image-loaded');
            messageDiv.classList.add('has-message-image');
            scheduleFlowShape(messageDiv);
        }, { once: true });
    }

    function renderMessageWithEmotes(message, emotes, image) {
        const fragments = [];

        for (const emote of emotes) {
            if (!emote.imageUrl) {
                continue;
            }

            if (!isValidRange(emote.startIndex, emote.endIndex, message.length)) {
                console.warn('Пропущен эмоут с некорректными индексами:', emote);
                continue;
            }

            const animatedClass = enableSpecialEffects && isAnimatedEmote(emote.name) ? ' animated' : '';
            const src = escapeAttr(emote.imageUrl);
            const name = escapeAttr(emote.name);

            fragments.push({
                start: emote.startIndex,
                end: emote.endIndex,
                html: `<img src="${src}" alt="${name}" title="${name}" class="emote${animatedClass}">`,
            });
        }

        const link = image && image.link;

        if (link) {
            const linkText = escapeHtml(message.substring(link.startIndex, link.endIndex + 1));

            fragments.push({
                start: link.startIndex,
                end: link.endIndex,
                html: `<span class="message-image-link">${linkText}</span>`,
            });
        }

        fragments.sort((a, b) => a.start - b.start);

        let html = '';
        let cursor = 0;

        for (const fragment of fragments) {
            if (fragment.start < cursor) {
                continue;
            }

            html += highlightMentions(escapeHtml(message.substring(cursor, fragment.start))) + fragment.html;
            cursor = fragment.end + 1;
        }

        return html + highlightMentions(escapeHtml(message.substring(cursor)));
    }

    function isAnimatedEmote(emoteName) {
        const animatedEmotes = ['Kappa', 'PogChamp', 'KEKW', 'EZ', 'PauseChamp'];
        return animatedEmotes.includes(emoteName);
    }

    function highlightMentions(text) {
        if (!isHighlightMentions) return text;

        return text.replace(/@(\w+)/g, '<span class="mention">@$1</span>');
    }

    const usernameColorPattern = /^#[0-9a-fA-F]{6}$/;

    function sanitizeUsernameColor(value) {
        if (typeof value !== 'string') return '';
        const trimmed = value.trim();
        return usernameColorPattern.test(trimmed) ? trimmed : '';
    }

    function escapeAttr(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;');
    }

    function escapeHtml(text) {
        return text.replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    const cssVariableMap = [
        ['backgroundColor', '--chat-bg-color'],
        ['textColor', '--chat-text-color'],
        ['usernameColor', '--chat-username-color'],
        ['systemMessageColor', '--chat-system-color'],
        ['timestampColor', '--chat-timestamp-color'],
        ['fontFamily', '--chat-font-family'],
        ['fontSize', '--chat-font-size'],
        ['fontWeight', '--chat-font-weight'],
        ['padding', '--chat-padding'],
        ['margin', '--chat-margin'],
        ['borderRadius', '--chat-border-radius'],
        ['animationDuration', '--chat-animation-duration'],
        ['emoteSize', '--emote-size'],
        ['badgeSize', '--badge-size'],
        ['userAvatarSize', '--avatar-size'],
        ['messageImageMaxHeight', '--message-image-max-height'],
    ];

    const animationFieldMap = {
        userMessageAnimation: v => userMessageAnimation = v,
        botMessageAnimation: v => botMessageAnimation = v,
        systemMessageAnimation: v => systemMessageAnimation = v,
        broadcasterMessageAnimation: v => broadcasterMessageAnimation = v,
        firstTimeUserMessageAnimation: v => firstTimeUserMessageAnimation = v,
    };

    function toggleNegationClass(selector, className, enabled) {
        document.querySelectorAll(selector).forEach(message => {
            if (enabled) {
                message.classList.remove(className);
            } else {
                message.classList.add(className);
            }
        });
    }

    function parseSettings(settings) {
        if (typeof settings !== 'string') {
            return settings;
        }
        try {
            return JSON.parse(settings);
        } catch (e) {
            console.error('Некорректные настройки чата (ожидался объект):', e);
            return null;
        }
    }

    function applyCssVariables(settings, root) {
        cssVariableMap.forEach(([key, cssVar]) => {
            if (settings[key]) {
                root.style.setProperty(cssVar, settings[key]);
            }
        });
    }

    function applyClassToggleSettings(settings) {
        const toggles = [
            ['showUserTypeBorders', '.message', 'no-borders', v => showUserTypeBorders = v, () => showUserTypeBorders],
            ['highlightFirstTimeUsers', '.message.first-time', 'no-first-time-effects', v => highlightFirstTimeUsers = v, () => highlightFirstTimeUsers],
            ['highlightMentions', '.message', 'no-mentions', v => isHighlightMentions = v, () => isHighlightMentions],
            ['enableMessageShadows', '.message', 'no-shadows', v => enableMessageShadows = v, () => enableMessageShadows],
            ['enableSpecialEffects', '.message', 'no-special-effects', v => enableSpecialEffects = v, () => enableSpecialEffects],
            ['showTimestamp', '#chat .message', 'no-timestamp', v => showTimestamp = v, () => showTimestamp],
            ['showUserAvatars', '.message', 'no-avatars', v => showUserAvatars = v, () => showUserAvatars],
            ['showMessageImages', '.message', 'no-message-images', v => showMessageImages = v, () => showMessageImages],
        ];

        toggles.forEach(([key, selector, className, setter, getter]) => {
            if (settings[key] !== undefined) {
                setter(settings[key]);
                toggleNegationClass(selector, className, getter());
            }
        });

        if (settings.platformBadgeMode !== undefined) {
            platformBadgeMode = normalizePlatformBadgeMode(settings.platformBadgeMode);
        }
        if (settings.showPlatformBadge !== undefined) {
            serverShowPlatformBadge = Boolean(settings.showPlatformBadge);
        }
        if (settings.platformBadgeMode !== undefined || settings.showPlatformBadge !== undefined) {
            syncPlatformBadgeVisibility();
        }
    }

    function applyAnimationSettings(settings) {
        if (settings.enableAnimations !== undefined) enableAnimations = settings.enableAnimations;

        Object.keys(animationFieldMap).forEach(key => {
            if (settings[key]) animationFieldMap[key](settings[key]);
        });
    }

    function applyScrollSettings(settings) {
        if (settings.enableSmoothScroll !== undefined) enableSmoothScroll = settings.enableSmoothScroll;
        if (settings.scrollAnimationDuration !== undefined) scrollAnimationDuration = settings.scrollAnimationDuration;
        if (settings.autoScrollEnabled !== undefined) autoScrollEnabled = settings.autoScrollEnabled;
        if (settings.scrollToBottomThreshold !== undefined) scrollToBottomThreshold = settings.scrollToBottomThreshold;
        if (settings.scrollPauseAfterUserMs !== undefined) scrollPauseAfterUserMs = settings.scrollPauseAfterUserMs;
    }

    function applyFadeSettings(settings, root) {
        let fadeRescheduleNeeded = false;

        if (settings.enableMessageFadeOut !== undefined) {
            const next = isPreview ? false : settings.enableMessageFadeOut;
            if (next !== enableMessageFadeOut) fadeRescheduleNeeded = true;
            enableMessageFadeOut = next;
        }
        if (settings.messageLifetimeSeconds !== undefined) {
            const next = settings.messageLifetimeSeconds * 1000;
            if (next !== messageLifetime) fadeRescheduleNeeded = true;
            messageLifetime = next;
        }
        if (settings.fadeOutAnimationType !== undefined) fadeOutAnimationType = settings.fadeOutAnimationType;
        if (settings.fadeOutAnimationDurationMs !== undefined) {
            fadeOutDuration = settings.fadeOutAnimationDurationMs;
            root.style.setProperty('--fade-out-duration', fadeOutDuration + 'ms');
        }

        return fadeRescheduleNeeded;
    }

    function updateChatSettings(settings) {
        settings = parseSettings(settings);
        if (settings === null) {
            return;
        }
        const root = document.documentElement;

        if (settings.overlayTheme !== undefined) {
            configuredOverlayTheme = normalizeOverlayTheme(settings.overlayTheme);
            activeOverlayTheme = themeQueryOverride ?? configuredOverlayTheme;
        }
        root.dataset.overlayTheme = activeOverlayTheme;

        applyCssVariables(settings, root);
        applyClassToggleSettings(settings);

        if (settings.maxMessages !== undefined) maxMessages = settings.maxMessages;

        applyAnimationSettings(settings);
        applyScrollSettings(settings);

        if (applyFadeSettings(settings, root)) {
            rescheduleAllFades();
        }

        syncAllFlowMessages();

        console.log('Настройки чата обновлены:', settings);
    }

    const reloadBannerLifetimeMs = 5000;
    const reloadBannerFadeMs = 600;

    function showReloadBanner() {
        const now = new Date();

        const banner = document.createElement('div');
        banner.classList.add('message', 'reload-banner', 'no-animation', 'entry-done');
        banner.style.transition = 'opacity ' + reloadBannerFadeMs + 'ms ease';
        messageMeta.set(banner, {
            messageType: 'SystemNotification',
            rank: null,
            displayName: '',
            platform: '',
            userId: '',
            isReloadBanner: true,
        });

        const timeElement = document.createElement('time');
        timeElement.className = 'timestamp';
        timeElement.dateTime = now.toISOString();
        timeElement.textContent = formatTimestamp(now);

        const text = document.createElement('span');
        text.className = 'system-message';
        text.textContent = '🔁 Чат-источник перезагружен';

        banner.appendChild(timeElement);
        banner.appendChild(text);
        chatContainer.appendChild(banner);

        if (isFlowTheme()) {
            decorateFlowMessage(banner);
            shapeFlowMessage(banner);
        }

        requestAnimationFrame(() => smoothScrollToBottom(true));

        setTimeout(() => {
            banner.style.opacity = '0';
            setTimeout(() => removeMessageNode(banner), reloadBannerFadeMs);
        }, reloadBannerLifetimeMs);
    }

    function loadHistoryAndBackfill() {
        const generation = ++historyLoadGeneration;
        if (historyFetchController) {
            try {
                historyFetchController.abort();
            } catch { /* noop */ }
            historyFetchController = null;
        }

        const controller = typeof AbortController === 'function' ? new AbortController() : null;
        historyFetchController = controller;
        const historyUrl = isPreview ? '/api/history?preview=true' : '/api/history';
        const fetchOptions = controller ? { signal: controller.signal } : undefined;

        return fetch(historyUrl, fetchOptions)
            .then(response => {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            })
            .then(messages => {
                if (generation !== historyLoadGeneration) {
                    return;
                }
                messages.forEach(message => addMessage(message, true));
            })
            .catch(error => {
                if (error && error.name === 'AbortError') {
                    return;
                }
                throw error;
            })
            .finally(() => {
                if (historyFetchController === controller) {
                    historyFetchController = null;
                }
            });
    }

    function scheduleReconnect() {
        if (reconnecting) {
            return;
        }
        reconnecting = true;

        if (reconnectTimeoutId !== null) {
            clearTimeout(reconnectTimeoutId);
        }

        console.warn('SSE: переподключение оверлея через', reconnectDelayMs, 'мс');
        reconnectTimeoutId = setTimeout(() => {
            reconnectTimeoutId = null;
            reconnecting = false;
            reconnectDelayMs = Math.min(reconnectMaxDelayMs, reconnectDelayMs * 2);
            loadHistoryAndBackfill()
                .catch(error => console.error('Ошибка догрузки истории при реконнекте:', error))
                .finally(() => {
                    initEventSource();
                    markAlive();
                });
        }, reconnectDelayMs);
    }

    function startWatchdog() {
        if (watchdogIntervalId !== null) {
            return;
        }
        watchdogIntervalId = setInterval(() => {
            if (reconnecting) {
                return;
            }
            const stale = Date.now() - lastEventTime > staleThresholdMs;
            const closed = eventSource !== null && eventSource.readyState === EventSource.CLOSED;
            if (stale || closed) {
                scheduleReconnect();
            }
        }, watchdogTickMs);
    }

    function initOverlay() {
        fetch('/api/chat-settings')
            .then(response => response.json())
            .then(settings => updateChatSettings(settings))
            .then(() => loadHistoryAndBackfill())
            .catch(error => console.error('Ошибка инициализации оверлея:', error))
            .finally(() => {
                initEventSource();
                markAlive();
                startWatchdog();
            });
    }

    initOverlay();

}());
