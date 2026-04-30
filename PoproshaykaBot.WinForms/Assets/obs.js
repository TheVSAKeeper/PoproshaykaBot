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
                raw = (userStatus & 1) ? broadcasterMessageAnimation : userMessageAnimation;
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
    void messageDiv.offsetHeight;

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

    if (isHistoryMessage || !enableAnimations) {
        messageDiv.classList.add('no-animation');
        messageDiv.classList.add('entry-done');
    } else {
        const animationType = getAnimationType(message.status || 0, message.messageType, message.isFirstTime);
        messageDiv.classList.add(animationType);

        const onEntryEnd = (event) => {
            if (event.target !== messageDiv) return;
            messageDiv.removeEventListener('animationend', onEntryEnd);
            messageDiv.classList.add('entry-done');
        };
        messageDiv.addEventListener('animationend', onEntryEnd);
    }

    const userTypeClasses = getUserTypeClasses(message.status || 0);
    userTypeClasses.forEach(cls => messageDiv.classList.add(cls));

    if (!showUserTypeBorders) {
        messageDiv.classList.add('no-borders');
    }

    if (!enableMessageShadows) {
        messageDiv.classList.add('no-shadows');
    }

    if (!enableSpecialEffects) {
        messageDiv.classList.add('no-special-effects');
    } else {
        if (message.status & 1) { // Broadcaster
            messageDiv.classList.add('special-effect');
        } else if (message.status & 4) { // VIP
            messageDiv.classList.add('special-effect');
        }
    }

    if (message.isFirstTime) {
        if (highlightFirstTimeUsers) {
            messageDiv.classList.add('first-time');
        } else {
            messageDiv.classList.add('first-time', 'no-first-time-effects');
        }
    }

    if (!isHighlightMentions) {
        messageDiv.classList.add('no-mentions');
    }

    if (!showTimestamp) {
        messageDiv.classList.add('no-timestamp');
    }

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

function updateChatSettings(settings) {
    if (typeof settings === 'string') {
        try {
            settings = JSON.parse(settings);
        } catch (e) {
            console.error('Некорректные настройки чата (ожидался объект):', e);
            return;
        }
    }
    const root = document.documentElement;

    if (settings.backgroundColor) root.style.setProperty('--chat-bg-color', settings.backgroundColor);
    if (settings.textColor) root.style.setProperty('--chat-text-color', settings.textColor);
    if (settings.usernameColor) root.style.setProperty('--chat-username-color', settings.usernameColor);
    if (settings.systemMessageColor) root.style.setProperty('--chat-system-color', settings.systemMessageColor);
    if (settings.timestampColor) root.style.setProperty('--chat-timestamp-color', settings.timestampColor);
    if (settings.fontFamily) root.style.setProperty('--chat-font-family', settings.fontFamily);
    if (settings.fontSize) root.style.setProperty('--chat-font-size', settings.fontSize);
    if (settings.fontWeight) root.style.setProperty('--chat-font-weight', settings.fontWeight);
    if (settings.padding) root.style.setProperty('--chat-padding', settings.padding);
    if (settings.margin) root.style.setProperty('--chat-margin', settings.margin);
    if (settings.borderRadius) root.style.setProperty('--chat-border-radius', settings.borderRadius);
    if (settings.animationDuration) root.style.setProperty('--chat-animation-duration', settings.animationDuration);

    if (settings.emoteSize) root.style.setProperty('--emote-size', settings.emoteSize);
    if (settings.badgeSize) root.style.setProperty('--badge-size', settings.badgeSize);

    if (settings.showUserTypeBorders !== undefined) {
        showUserTypeBorders = settings.showUserTypeBorders;
        const messages = document.querySelectorAll('.message');
        messages.forEach(message => {
            if (showUserTypeBorders) {
                message.classList.remove('no-borders');
            } else {
                message.classList.add('no-borders');
            }
        });
    }
    if (settings.highlightFirstTimeUsers !== undefined) {
        highlightFirstTimeUsers = settings.highlightFirstTimeUsers;
        const firstTimeMessages = document.querySelectorAll('.message.first-time');
        firstTimeMessages.forEach(message => {
            if (highlightFirstTimeUsers) {
                message.classList.remove('no-first-time-effects');
            } else {
                message.classList.add('no-first-time-effects');
            }
        });
    }
    if (settings.highlightMentions !== undefined) {
        isHighlightMentions = settings.highlightMentions;
        const messages = document.querySelectorAll('.message');
        messages.forEach(message => {
            if (isHighlightMentions) {
                message.classList.remove('no-mentions');
            } else {
                message.classList.add('no-mentions');
            }
        });
    }
    if (settings.enableMessageShadows !== undefined) {
        enableMessageShadows = settings.enableMessageShadows;
        const messages = document.querySelectorAll('.message');
        messages.forEach(message => {
            if (enableMessageShadows) {
                message.classList.remove('no-shadows');
            } else {
                message.classList.add('no-shadows');
            }
        });
    }
    if (settings.enableSpecialEffects !== undefined) {
        enableSpecialEffects = settings.enableSpecialEffects;
        const messages = document.querySelectorAll('.message');
        messages.forEach(message => {
            if (enableSpecialEffects) {
                message.classList.remove('no-special-effects');
            } else {
                message.classList.add('no-special-effects');
            }
        });
    }

    if (settings.maxMessages !== undefined) maxMessages = settings.maxMessages;
    if (settings.showTimestamp !== undefined) {
        showTimestamp = settings.showTimestamp;
        const messages = document.querySelectorAll('#chat .message');
        messages.forEach(message => {
            if (showTimestamp) {
                message.classList.remove('no-timestamp');
            } else {
                message.classList.add('no-timestamp');
            }
        });
    }
    if (settings.enableAnimations !== undefined) enableAnimations = settings.enableAnimations;

    if (settings.enableSmoothScroll !== undefined) enableSmoothScroll = settings.enableSmoothScroll;
    if (settings.scrollAnimationDuration !== undefined) scrollAnimationDuration = settings.scrollAnimationDuration;
    if (settings.autoScrollEnabled !== undefined) autoScrollEnabled = settings.autoScrollEnabled;
    if (settings.scrollToBottomThreshold !== undefined) scrollToBottomThreshold = settings.scrollToBottomThreshold;

    if (settings.userMessageAnimation) userMessageAnimation = settings.userMessageAnimation;
    if (settings.botMessageAnimation) botMessageAnimation = settings.botMessageAnimation;
    if (settings.systemMessageAnimation) systemMessageAnimation = settings.systemMessageAnimation;
    if (settings.broadcasterMessageAnimation) broadcasterMessageAnimation = settings.broadcasterMessageAnimation;
    if (settings.firstTimeUserMessageAnimation) firstTimeUserMessageAnimation = settings.firstTimeUserMessageAnimation;

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

    if (fadeRescheduleNeeded) {
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
