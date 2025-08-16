const chatContainer = document.getElementById('chat');
let maxMessages = 50;
let showTimestamp = true;
let enableAnimations = true;

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

function addMessage(message, isHistoryMessage = false) {
    const messageDiv = document.createElement('div');
    messageDiv.className = 'message';

    if (isHistoryMessage || !enableAnimations) {
        messageDiv.classList.add('no-animation');
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

        messageDiv.innerHTML = `
            ${timestampHtml}
            ${badgesHtml}
            <span class='username'>${message.username}:</span>
            <span class='message-text'> ${messageWithEmotes}</span>
        `;
    }

    chatContainer.appendChild(messageDiv);

    while (chatContainer.children.length > maxMessages) {
        chatContainer.removeChild(chatContainer.firstChild);
    }

    chatContainer.scrollTop = chatContainer.scrollHeight;
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
    if (!emotes || emotes.length === 0) return escapeHtml(message);

    const sortedEmotes = emotes.sort((a, b) => b.startIndex - a.startIndex);

    let result = message;
    for (const emote of sortedEmotes) {
        if (emote.imageUrl && emote.startIndex >= 0 && emote.endIndex >= emote.startIndex) {
            const before = result.substring(0, emote.startIndex);
            const after = result.substring(emote.endIndex + 1);
            const emoteImg = `<img src=\"${emote.imageUrl}\" alt=\"${emote.name}\" title=\"${emote.name}\" class=\"emote\">`;
            result = before + emoteImg + after;
        }
    }

    return escapeHtml(result, true); // true = не экранировать HTML теги img
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

    if (settings.maxMessages !== undefined) maxMessages = settings.maxMessages;
    if (settings.showTimestamp !== undefined) showTimestamp = settings.showTimestamp;
    if (settings.enableAnimations !== undefined) enableAnimations = settings.enableAnimations;

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
