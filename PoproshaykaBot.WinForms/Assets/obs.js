const chatContainer = document.getElementById('chat');
let maxMessages = 50;
let showTimestamp = true;
let enableAnimations = true;

const urlParams = new URLSearchParams(window.location.search);
const isPreview = urlParams.get('preview') === 'true';

let showUserTypeBorders = true;
let highlightFirstTimeUsers = true;
let isHighlightMentions = true;
let enableMessageShadows = true;
let enableSpecialEffects = true;

let enableSmoothScroll = true;
let scrollAnimationDuration = 300;
let autoScrollEnabled = true;
let scrollPaused = false;
let scrollToBottomThreshold = 100;
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

const fadeTimers = new WeakMap();

function forceReflow(element) {
    return element.offsetHeight;
}

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

function smoothScrollToBottom() {
    if (!autoScrollEnabled || scrollPaused) return;

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

function handleUserScroll() {
    if (!isNearBottom()) {
        scrollPaused = true;
        setTimeout(() => {
            scrollPaused = false;
        }, 3000);
    }
}

if (chatContainer) {
    let scrollTimeout;
    chatContainer.addEventListener('scroll', () => {
        clearTimeout(scrollTimeout);
        scrollTimeout = setTimeout(handleUserScroll, 50);
    }, { passive: true });
}

function getUserTypeClasses(userStatus) {
    const classes = [];

    if (userStatus & 1) classes.push('broadcaster');
    if (userStatus & 2) classes.push('moderator');
    if (userStatus & 4) classes.push('vip');
    if (userStatus & 8) classes.push('subscriber');

    return classes;
}

const validEntryAnimations = ['no-animation', 'slide-in-right', 'slide-in-left', 'fade-in-up', 'bounce-in'];

function sanitizeEntryAnimation(value, fallback) {
    return validEntryAnimations.includes(value) ? value : fallback;
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
    const eventSource = new EventSource('/events');

    eventSource.addEventListener('message', function (event) {
        try {
            addMessage(JSON.parse(event.data));
        } catch (e) {
            console.error('Ошибка парсинга SSE message:', e);
        }
    });

    eventSource.addEventListener('clear', function () {
        clearChat();
    });

    eventSource.addEventListener('chat_settings_changed', function (event) {
        try {
            updateChatSettings(JSON.parse(event.data));
        } catch (e) {
            console.error('Ошибка парсинга SSE chat_settings_changed:', e);
        }
    });

    eventSource.onerror = function (event) {
        console.error('SSE ошибка:', event);
    };
}

const entryAnimationClasses = ['slide-in-left', 'slide-in-right', 'fade-in-up', 'bounce-in'];
const exitAnimationClasses = ['fade-out', 'slide-out-left', 'slide-out-right', 'scale-down', 'shrink-up'];

function fadeOutMessage(messageDiv) {
    if (!enableMessageFadeOut) return;
    if (messageDiv.dataset.fading === 'true') return;
    messageDiv.dataset.fading = 'true';

    let animationClass = 'fade-out';
    switch (fadeOutAnimationType) {
        case 'fade-out':
            animationClass = 'fade-out';
            break;
        case 'slide-out-left':
            animationClass = 'slide-out-left';
            break;
        case 'slide-out-right':
            animationClass = 'slide-out-right';
            break;
        case 'scale-down':
            animationClass = 'scale-down';
            break;
        case 'shrink-up':
            animationClass = 'shrink-up';
            break;
        case 'no-animation':
        default:
            setTimeout(() => {
                if (messageDiv.parentNode) {
                    messageDiv.parentNode.removeChild(messageDiv);
                }
            }, 100);
            return;
    }

    entryAnimationClasses.forEach(cls => messageDiv.classList.remove(cls));
    exitAnimationClasses.forEach(cls => messageDiv.classList.remove(cls));
    forceReflow(messageDiv);

    messageDiv.classList.add(animationClass);

    setTimeout(() => {
        if (messageDiv.parentNode) {
            messageDiv.parentNode.removeChild(messageDiv);
        }
    }, fadeOutDuration);
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

    const createdAt = parseInt(messageDiv.dataset.createdAt, 10) || Date.now();
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
        messageDiv.classList.add('no-animation');
        messageDiv.classList.add('entry-done');
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
    ];
    toggles.forEach(([enabled, className]) => {
        if (!enabled) messageDiv.classList.add(className);
    });
}

function addMessage(message, isHistoryMessage = false) {
    if (rememberMessageId(message.messageId)) {
        return;
    }

    const messageDiv = document.createElement('div');
    messageDiv.className = 'message';

    const createdAtMs = isHistoryMessage
        ? new Date(message.timestamp).getTime()
        : Date.now();
    messageDiv.dataset.createdAt = String(createdAtMs);

    applyEntryAnimation(messageDiv, message, isHistoryMessage);

    const userTypeClasses = getUserTypeClasses(message.status || 0);
    userTypeClasses.forEach(cls => messageDiv.classList.add(cls));

    applyToggleClasses(messageDiv);
    applySpecialEffects(messageDiv, message);
    applyFirstTimeMarker(messageDiv, message);

    const timestamp = new Date(message.timestamp).toLocaleTimeString();
    const isSystemMessage = message.messageType !== 'UserMessage';

    const timestampHtml = `<span class='timestamp'>${timestamp}</span>`;

    if (isSystemMessage) {
        messageDiv.innerHTML = `
            ${timestampHtml}
            <span class='system-message'>${escapeHtml(message.message)}</span>
        `;
    } else {
        const badgesHtml = renderBadges(message.badges || []);
        const messageWithEmotes = renderMessageWithEmotes(message.message, message.emotes || []);

        const usernameClasses = ['username', ...userTypeClasses].join(' ');
        const usernameHtml = `<span class='${usernameClasses}'>${message.displayName || message.username}:</span>`;

        messageDiv.innerHTML = `
            ${timestampHtml}
            ${badgesHtml}
            ${usernameHtml}
            <span class='message-text'> ${messageWithEmotes}</span>
        `;
    }

    chatContainer.appendChild(messageDiv);

    scheduleFadeOut(messageDiv);

    while (chatContainer.children.length > maxMessages) {
        const removed = chatContainer.firstChild;
        if (removed) {
            cancelFadeOut(removed);
        }
        chatContainer.removeChild(chatContainer.firstChild);
    }

    if (!isHistoryMessage) {
        requestAnimationFrame(() => smoothScrollToBottom());
    } else {
        chatContainer.scrollTop = chatContainer.scrollHeight;
    }
}

function clearChat() {
    chatContainer.innerHTML = '';
    seenMessageIds.clear();
    seenMessageIdsOrder.length = 0;
}

function renderBadges(badges) {
    if (!badges || badges.length === 0) return '';

    return badges.map(badge => {
        if (!badge.imageUrl) return '';
        return `<img src="${badge.imageUrl}" alt="${badge.type}" title="${badge.type} ${badge.version}" class="badge">`;
    }).join('');
}

function renderMessageWithEmotes(message, emotes) {
    if (!emotes || emotes.length === 0) {
        return highlightMentions(escapeHtml(message));
    }

    const sortedEmotes = emotes.sort((a, b) => b.startIndex - a.startIndex);

    let result = message;
    for (const emote of sortedEmotes) {
        if (emote.imageUrl && emote.startIndex >= 0 && emote.endIndex >= emote.startIndex) {
            const before = result.substring(0, emote.startIndex);
            const after = result.substring(emote.endIndex + 1);

            const animatedClass = enableSpecialEffects && isAnimatedEmote(emote.name) ? ' animated' : '';
            const emoteImg = `<img src=\"${emote.imageUrl}\" alt=\"${emote.name}\" title=\"${emote.name}\" class=\"emote${animatedClass}\">`;

            result = before + emoteImg + after;
        }
    }

    return highlightMentions(escapeHtml(result, true));
}

function isAnimatedEmote(emoteName) {
    const animatedEmotes = ['Kappa', 'PogChamp', 'KEKW', 'EZ', 'PauseChamp'];
    return animatedEmotes.includes(emoteName);
}

function highlightMentions(text) {
    if (!isHighlightMentions) return text;

    return text.replace(/@(\w+)/g, '<span class="mention">@$1</span>');
}

function escapeHtml(text, preserveImgTags = false) {
    if (preserveImgTags) {
        const imgTags = [];
        text = text.replace(/<img[^>]*>/g, match => {
            imgTags.push(match);
            return `__IMG_PLACEHOLDER_${imgTags.length - 1}__`;
        });

        text = text.replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/\"/g, '&quot;')
            .replace(/'/g, '&#039;');

        text = text.replace(/__IMG_PLACEHOLDER_(\d+)__/g, (match, index) => imgTags[parseInt(index)]);

        return text;
    }

    return text.replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/\"/g, '&quot;')
        .replace(/'/g, '&#039;');
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
    ];

    toggles.forEach(([key, selector, className, setter, getter]) => {
        if (settings[key] !== undefined) {
            setter(settings[key]);
            toggleNegationClass(selector, className, getter());
        }
    });
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

    applyCssVariables(settings, root);
    applyClassToggleSettings(settings);

    if (settings.maxMessages !== undefined) maxMessages = settings.maxMessages;

    applyAnimationSettings(settings);
    applyScrollSettings(settings);

    if (applyFadeSettings(settings, root)) {
        rescheduleAllFades();
    }

    console.log('Настройки чата обновлены:', settings);
}

function initOverlay() {
    fetch('/api/chat-settings')
        .then(response => response.json())
        .then(settings => updateChatSettings(settings))
        .then(() => fetch('/api/history'))
        .then(response => response.json())
        .then(messages => messages.forEach(message => addMessage(message, true)))
        .then(() => initEventSource())
        .catch(error => console.error('Ошибка инициализации оверлея:', error));
}

initOverlay();
