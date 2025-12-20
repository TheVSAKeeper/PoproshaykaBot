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

function getAnimationType(userStatus, messageType, isFirstTime = false) {
    if (isFirstTime && firstTimeUserMessageAnimation !== 'slide-in-right') {
        return firstTimeUserMessageAnimation;
    }

    switch (messageType) {
        case 'BotResponse':
            return botMessageAnimation;
        case 'SystemNotification':
            return systemMessageAnimation;
        case 'UserMessage':
            if (userStatus & 1) {
                return broadcasterMessageAnimation;
            }
            return userMessageAnimation;
        default:
            return userMessageAnimation;
    }
}

function initEventSource() {
    const eventSource = new EventSource('/events');

    eventSource.onmessage = function (event) {
        try {
            const data = JSON.parse(event.data);
            if (data.type === 'message') {
                addMessage(data.message);
            } else if (data.type === 'clear') {
                clearChat();
            } else if (data.type === 'chat_settings_changed') {
                updateChatSettings(data.settings);
            }
        } catch (e) {
            console.error('Ошибка парсинга SSE данных:', e);
        }
    };

    eventSource.onerror = function (event) {
        console.error('SSE ошибка:', event);
    };
}

function fadeOutMessage(messageDiv) {
    if (!enableMessageFadeOut) return;

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

    messageDiv.classList.add(animationClass);

    setTimeout(() => {
        if (messageDiv.parentNode) {
            messageDiv.parentNode.removeChild(messageDiv);
        }
    }, fadeOutDuration);
}

function addMessage(message, isHistoryMessage = false) {
    const messageDiv = document.createElement('div');
    messageDiv.className = 'message';

    if (isHistoryMessage || !enableAnimations) {
        messageDiv.classList.add('no-animation');
    } else {
        const animationType = getAnimationType(message.status || 0, message.messageType, message.isFirstTime);
        messageDiv.classList.add(animationType);
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

    const timestamp = new Date(message.timestamp).toLocaleTimeString();
    const isSystemMessage = message.messageType !== 'UserMessage';

    let timestampHtml = showTimestamp ? `<span class='timestamp'>${timestamp}</span>` : '';

    if (isSystemMessage) {
        messageDiv.innerHTML = `
            ${timestampHtml}
            <span class='system-message'>${message.message}</span>
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

    if (!isHistoryMessage && enableMessageFadeOut) {
        setTimeout(() => fadeOutMessage(messageDiv), messageLifetime);
    }

    while (chatContainer.children.length > maxMessages) {
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
    if (settings.showTimestamp !== undefined) showTimestamp = settings.showTimestamp;
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

    if (settings.enableMessageFadeOut !== undefined) {
        enableMessageFadeOut = isPreview ? false : settings.enableMessageFadeOut;
    }
    if (settings.messageLifetimeSeconds !== undefined) messageLifetime = settings.messageLifetimeSeconds * 1000;
    if (settings.fadeOutAnimationType !== undefined) fadeOutAnimationType = settings.fadeOutAnimationType;
    if (settings.fadeOutAnimationDurationMs !== undefined) fadeOutDuration = settings.fadeOutAnimationDurationMs;

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
